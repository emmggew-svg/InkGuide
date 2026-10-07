using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;

namespace InkGuide.Bridge.Handlers;

/// <summary>Per-request context (cancellation, idempotency, confirmation).</summary>
public sealed record BridgeRequestContext(
    string RequestId,
    string? IdempotencyKey,
    string? ConfirmationToken,
    TimeProvider TimeProvider,
    CancellationToken CancellationToken);

/// <summary>Host → UI event sink (implemented by the WebView2 host).</summary>
public interface IBridgeEventSink
{
    Task EmitAsync(string topic, JsonElement? payload, CancellationToken cancellationToken);
}

/// <summary>Coalescing event bus (≥100 ms per topic; last payload wins).</summary>
public sealed class BridgeEventBus : IDisposable
{
    private readonly IBridgeEventSink _sink;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, TopicState> _topics = new(StringComparer.Ordinal);
    private bool _disposed;

    public BridgeEventBus(IBridgeEventSink sink, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(clock);
        _sink = sink;
        _clock = clock;
    }

    /// <summary>Known host → UI event topics.</summary>
    public static class Topics
    {
        public const string AccountStateChanged = "account.stateChanged";
        public const string SyncStateChanged = "sync.stateChanged";
        public const string SyncProgress = "sync.progress";
        public const string NodeChanged = "node.changed";
        public const string TransferProgress = "transfer.progress";
        public const string AnalysisProgress = "analysis.progress";
        public const string InsightUpdated = "insight.updated";
        public const string ConflictCreated = "conflict.created";
        public const string ExternalSessionChanged = "external.sessionChanged";
        public const string AppOpenPaths = "app.openPaths";
        public const string SettingsChanged = "settings.changed";
        public const string ErrorGlobal = "error.global";
    }

    /// <summary>Emits an event, coalescing bursts per topic.</summary>
    public void Emit(string topic, JsonElement? payload)
    {
        ArgumentNullException.ThrowIfNull(topic);
        DateTimeOffset now = _clock.GetUtcNow();
        TopicState state = _topics.GetOrAdd(topic, _ => new TopicState());
        lock (state.Lock)
        {
            state.Pending = payload;
            if (now - state.LastEmit < TimeSpan.FromMilliseconds(100))
            {
                if (!state.Scheduled)
                {
                    state.Scheduled = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                            await FlushAsync(topic).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    });
                }

                return;
            }

            state.LastEmit = now;
            state.Pending = null;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _sink.EmitAsync(topic, payload, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task FlushAsync(string topic)
    {
        JsonElement? payload;
        lock (_topics.GetOrAdd(topic, _ => new TopicState()).Lock)
        {
            TopicState state = _topics[topic];
            payload = state.Pending;
            state.Pending = null;
            state.Scheduled = false;
            state.LastEmit = _clock.GetUtcNow();
        }

        try
        {
            await _sink.EmitAsync(topic, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed class TopicState
    {
        public object Lock { get; } = new();

        public JsonElement? Pending { get; set; }

        public DateTimeOffset LastEmit { get; set; } = DateTimeOffset.MinValue;

        public bool Scheduled { get; set; }
    }
}

/// <summary>Confirmation-token service for destructive operations.</summary>
public sealed class ConfirmationService
{
    private readonly ConcurrentDictionary<string, Confirmation> _tokens = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public ConfirmationService(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>Issues a scoped confirmation token (10 min).</summary>
    public string Issue(string action, string scope)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(scope);
        Prune();
        string token = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        _tokens[token] = new Confirmation(action, scope, _clock.GetUtcNow().AddMinutes(10));
        return token;
    }

    /// <summary>Validates a token for the action+scope; single use.</summary>
    public bool Validate(string? token, string action, string scope)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (!_tokens.TryRemove(token!, out Confirmation? confirmation))
        {
            return false;
        }

        return string.Equals(confirmation.Action, action, StringComparison.Ordinal)
            && string.Equals(confirmation.Scope, scope, StringComparison.Ordinal)
            && confirmation.ExpiresAtUtc > _clock.GetUtcNow();
    }

    /// <summary>Builds the CONFIRMATION_REQUIRED error carrying a pre-issued token.</summary>
    public OperationError Required(string action, string scope, string summary)
    {
        string token = Issue(action, scope);
        return new OperationError(
            ErrorCodes.ConfirmationRequired,
            summary,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["confirmationToken"] = token,
                ["action"] = action,
            });
    }

    private void Prune()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        foreach (KeyValuePair<string, Confirmation> kv in _tokens)
        {
            if (kv.Value.ExpiresAtUtc <= now)
            {
                _ = _tokens.TryRemove(kv.Key, out _);
            }
        }
    }

    private sealed record Confirmation(string Action, string Scope, DateTimeOffset ExpiresAtUtc);
}

/// <summary>Idempotency-key store (1 h result retention).</summary>
public sealed class IdempotencyStore
{
    private readonly ConcurrentDictionary<string, StoredResult> _results = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public IdempotencyStore(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>Returns a stored result when the key was seen.</summary>
    public bool TryGet(string key, out JsonElement? result)
    {
        ArgumentNullException.ThrowIfNull(key);
        Prune();
        if (_results.TryGetValue(key, out StoredResult? stored))
        {
            result = stored.Result;
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>Stores a result for the key.</summary>
    public void Store(string key, JsonElement? result)
    {
        ArgumentNullException.ThrowIfNull(key);
        _results[key] = new StoredResult(result, _clock.GetUtcNow().AddHours(1));
    }

    private void Prune()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        foreach (KeyValuePair<string, StoredResult> kv in _results)
        {
            if (kv.Value.ExpiresAtUtc <= now)
            {
                _ = _results.TryRemove(kv.Key, out _);
            }
        }
    }

    private sealed record StoredResult(JsonElement? Result, DateTimeOffset ExpiresAtUtc);
}
