using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using InkGuide.App.Hosting;
using InkGuide.Bridge.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace InkGuide.App.Bridge;

/// <summary>
/// WebView2 message transport (MASTER_PROMPT Section 14.2): origin-checked,
/// size-limited (1 MB) postMessage JSON in both directions. Requests are
/// dispatched through the versioned <see cref="BridgeDispatcher"/> (backed by
/// the composed real backend); host-to-UI events are coalesced per topic
/// (minimum 100 ms between posts). WebView2 calls run on the UI thread;
/// request handling runs on background threads.
/// </summary>
internal sealed class BridgeTransport : IDisposable
{
    private static readonly TimeSpan EventCoalesceWindow = TimeSpan.FromMilliseconds(100);

    private readonly ILogger<BridgeTransport> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Dispatcher _dispatcher;
    private readonly DragDropHandler _dragDrop;
    private readonly object _eventLock = new();
    private readonly Dictionary<string, EventEntry> _events = new(StringComparer.Ordinal);

    private CoreWebView2? _core;
    private BridgeDispatcher? _bridge;
    private Uri? _devOrigin;
    private bool _disposed;

    public BridgeTransport(
        ILogger<BridgeTransport> logger,
        TimeProvider timeProvider,
        Dispatcher dispatcher,
        DragDropHandler dragDrop)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _dispatcher = dispatcher;
        _dragDrop = dragDrop;
    }

    public void Configure(BridgeDispatcher dispatcher, string? devUrl)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _bridge = dispatcher;
        _devOrigin = null;
        if (!string.IsNullOrWhiteSpace(devUrl) && Uri.TryCreate(devUrl, UriKind.Absolute, out Uri? dev))
        {
            _devOrigin = new Uri(dev.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
        }
    }

    public void Attach(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        Detach();
        _core = core;
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    public void Detach()
    {
        if (_core is not null)
        {
            _core.WebMessageReceived -= OnWebMessageReceived;
            _core = null;
        }
    }

    /// <summary>Host-to-UI event, coalesced per topic (method) to at most one post per 100 ms.</summary>
    public Task EmitEventAsync(string method, string paramsJson)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(paramsJson);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool sendNow = false;
        lock (_eventLock)
        {
            if (_events.TryGetValue(method, out EventEntry? entry) &&
                now - entry.LastSent < EventCoalesceWindow)
            {
                _events[method] = entry with { PendingJson = paramsJson };
                if (!entry.FlushScheduled)
                {
                    _events[method] = _events[method] with { FlushScheduled = true };
                    TimeSpan delay = EventCoalesceWindow - (now - entry.LastSent);
                    _ = FlushLaterAsync(method, delay);
                }

                return Task.CompletedTask;
            }

            _events[method] = new EventEntry(now, PendingJson: null, FlushScheduled: false);
            sendNow = true;
        }

        return sendNow ? PostAsync(BridgeEnvelope.Event(method, paramsJson)) : Task.CompletedTask;
    }

    private async Task FlushLaterAsync(string method, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        string? pending = null;
        lock (_eventLock)
        {
            if (_events.TryGetValue(method, out EventEntry? entry) && entry.PendingJson is not null)
            {
                pending = entry.PendingJson;
                _events[method] = entry with
                {
                    LastSent = _timeProvider.GetUtcNow(),
                    PendingJson = null,
                    FlushScheduled = false,
                };
            }
            else if (_events.TryGetValue(method, out EventEntry? idle))
            {
                _events[method] = idle with { FlushScheduled = false };
            }
        }

        if (pending is not null)
        {
            await PostAsync(BridgeEnvelope.Event(method, pending)).ConfigureAwait(false);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeDispatcher? bridge = _bridge;
        if (_disposed || bridge is null)
        {
            return;
        }

        try
        {
            if (!IsAllowedOrigin(e.Source))
            {
                AppLogMessages.BadOrigin(_logger);
                return;
            }

            string json = e.WebMessageAsJson ?? string.Empty;
            int byteCount = Encoding.UTF8.GetByteCount(json);
            if (byteCount > AppIdentity.BridgeMaxMessageBytes)
            {
                AppLogMessages.Oversized(_logger, byteCount);
                return;
            }

            if (!BridgeEnvelope.TryParseRequest(json, out BridgeRequest? request) || request is null)
            {
                AppLogMessages.Malformed(_logger);
                return;
            }

            string dispatchJson = json;
            string[] dropped = ExtractNativePaths(e);
            if (dropped.Length > 0)
            {
                // Explorer drops carry native paths outside JS: route them as an
                // upload into the Inbox (unsorted uploads land there by design).
                DragDropResult validated = _dragDrop.Validate(dropped);
                AppLogMessages.DropStats(_logger, validated.Accepted.Length, validated.Rejected.Length);
                string pathsJson = System.Text.Json.JsonSerializer.Serialize(validated.Accepted, BridgeEnvelope.Options);
                dispatchJson = "{\"v\":1,\"id\":" + System.Text.Json.JsonSerializer.Serialize(request.Id, BridgeEnvelope.Options)
                    + ",\"type\":\"req\",\"method\":\"transfers.uploadPaths\",\"params\":{\"parentId\":\"\",\"localPaths\":" + pathsJson + "}}";
            }

            // The dispatcher owns routing, cancellation, idempotency and replies;
            // a null response means the message was a cancellation.
            string? response = await bridge.DispatchAsync(dispatchJson, CancellationToken.None).ConfigureAwait(false);
            if (response is not null)
            {
                await PostAsync(response).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            AppLogMessages.ReceiveFailed(_logger, ex);
        }
    }

    private Task PostAsync(string json)
    {
        CoreWebView2? core = _core;
        if (core is null)
        {
            AppLogMessages.PostDropped(_logger);
            return Task.CompletedTask;
        }

        if (_dispatcher.CheckAccess())
        {
            try
            {
                core.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                AppLogMessages.PostFailed(_logger, ex);
            }

            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(() =>
        {
            try
            {
                _core?.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                AppLogMessages.PostFailed(_logger, ex);
            }
        }).Task;
    }

    private bool IsAllowedOrigin(string? source)
    {
        if (!Uri.TryCreate(source ?? string.Empty, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals(AppIdentity.UiHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_devOrigin is not null &&
            uri.Scheme == _devOrigin.Scheme &&
            uri.Host.Equals(_devOrigin.Host, StringComparison.OrdinalIgnoreCase) &&
            uri.Port == _devOrigin.Port)
        {
            return true;
        }

        return false;
    }

    private static string[] ExtractNativePaths(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var files = e.AdditionalObjects?.OfType<CoreWebView2File>().ToList();
            if (files is null || files.Count == 0)
            {
                return Array.Empty<string>();
            }

            return files.Select(f => f.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
    }

    private sealed record EventEntry(DateTimeOffset LastSent, string? PendingJson, bool FlushScheduled);
}
