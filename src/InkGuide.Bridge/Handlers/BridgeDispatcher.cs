using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;

namespace InkGuide.Bridge.Handlers;

/// <summary>One namespace handler (method dispatch within the namespace).</summary>
public interface IBridgeHandler
{
    /// <summary>Namespace prefix (e.g. "nodes").</summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Namespace is the bridge-protocol term (Sec 15); renamed would diverge from docs/contract. See DECISIONS D-004.")]
    string Namespace { get; }

    Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context);
}

/// <summary>Versioned dispatcher: envelope validation, routing, cancellation, idempotency.</summary>
public sealed class BridgeDispatcher : IDisposable
{
    public const int MaxMessageBytes = 1024 * 1024;

    private readonly Dictionary<string, IBridgeHandler> _handlers;
    private readonly ConfirmationService _confirmations;
    private readonly IdempotencyStore _idempotency;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflight = new(StringComparer.Ordinal);
    private bool _disposed;

    public BridgeDispatcher(IEnumerable<IBridgeHandler> handlers, ConfirmationService confirmations, IdempotencyStore idempotency, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(confirmations);
        ArgumentNullException.ThrowIfNull(idempotency);
        ArgumentNullException.ThrowIfNull(clock);
        _handlers = new Dictionary<string, IBridgeHandler>(StringComparer.Ordinal);
        foreach (IBridgeHandler handler in handlers)
        {
            _handlers[handler.Namespace] = handler;
        }

        _confirmations = confirmations;
        _idempotency = idempotency;
        _clock = clock;
    }

    /// <summary>Confirmation service (shared with handlers).</summary>
    public ConfirmationService Confirmations => _confirmations;

    /// <summary>Dispatches one raw JSON message and returns the raw JSON response (or null for cancel).</summary>
    public async Task<string?> DispatchAsync(string json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (EncodingLength(json) > MaxMessageBytes)
        {
            return SerializeError("unknown", new OperationError(ErrorCodes.InvalidParams, "Message exceeds 1 MB."));
        }

        BridgeEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<BridgeEnvelope>(json, BridgeJson.Options);
        }
        catch (JsonException ex)
        {
            return SerializeError("unknown", new OperationError(ErrorCodes.InvalidParams, "Invalid envelope: " + ex.Message));
        }

        if (envelope is null || envelope.V != 1 || string.IsNullOrEmpty(envelope.Id) || string.IsNullOrEmpty(envelope.Type))
        {
            return SerializeError(envelope?.Id ?? "unknown", new OperationError(ErrorCodes.InvalidParams, "Envelope must be v1 with id and type."));
        }

        if (string.Equals(envelope.Type, BridgeMessageTypes.Cancel, StringComparison.Ordinal))
        {
            if (_inflight.TryRemove(envelope.Id, out CancellationTokenSource? source))
            {
                source.Cancel();
                source.Dispose();
            }

            return null;
        }

        if (!string.Equals(envelope.Type, BridgeMessageTypes.Req, StringComparison.Ordinal) || string.IsNullOrEmpty(envelope.Method))
        {
            return SerializeError(envelope.Id, new OperationError(ErrorCodes.InvalidParams, "Only req messages with a method are accepted."));
        }

        int dot = envelope.Method.IndexOf('.');
        if (dot <= 0)
        {
            return SerializeError(envelope.Id, new OperationError(ErrorCodes.InvalidParams, $"Unknown method '{envelope.Method}'."));
        }

        string ns = envelope.Method.Substring(0, dot);
        if (!_handlers.TryGetValue(ns, out IBridgeHandler? handler))
        {
            return SerializeError(envelope.Id, new OperationError(ErrorCodes.InvalidParams, $"Unknown method '{envelope.Method}'."));
        }

        (string? idempotencyKey, string? confirmationToken) = ExtractOptions(envelope.Params, ns);
        if (idempotencyKey is not null && _idempotency.TryGet(envelope.Method + ":" + idempotencyKey, out JsonElement? stored))
        {
            return SerializeResult(envelope.Id, stored);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = _inflight.TryAdd(envelope.Id, linked);
        try
        {
            var context = new BridgeRequestContext(envelope.Id, idempotencyKey, confirmationToken, _clock, linked.Token);
            Result<JsonElement?> result = await handler.HandleAsync(envelope.Method, envelope.Params, context).ConfigureAwait(false);
            if (!result.IsSuccess || result.Failure is null)
            {
                if (result.IsSuccess && idempotencyKey is not null)
                {
                    _idempotency.Store(envelope.Method + ":" + idempotencyKey, result.Value);
                }

                return result.IsSuccess
                    ? SerializeResult(envelope.Id, result.Value)
                    : SerializeError(envelope.Id, result.Failure!);
            }

            return SerializeError(envelope.Id, result.Failure);
        }
        catch (OperationCanceledException) when (linked.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return SerializeError(envelope.Id, new OperationError(ErrorCodes.Cancelled, "Request was cancelled."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return SerializeError(envelope.Id, new OperationError(ErrorCodes.Internal, "Unexpected failure: " + ex.GetType().Name + "."));
        }
        finally
        {
            _ = _inflight.TryRemove(envelope.Id, out _);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (CancellationTokenSource source in _inflight.Values)
        {
            source.Cancel();
            source.Dispose();
        }

        _inflight.Clear();
        GC.SuppressFinalize(this);
    }

    private static (string? IdempotencyKey, string? ConfirmationToken) ExtractOptions(JsonElement? paramsJson, string ns)
    {
        _ = ns;
        if (paramsJson is null || paramsJson.Value.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        string? idempotencyKey = null;
        string? confirmationToken = null;
        if (paramsJson.Value.TryGetProperty("idempotencyKey", out JsonElement idem) && idem.ValueKind == JsonValueKind.String)
        {
            idempotencyKey = idem.GetString();
        }

        if (paramsJson.Value.TryGetProperty("confirmationToken", out JsonElement confirm) && confirm.ValueKind == JsonValueKind.String)
        {
            confirmationToken = confirm.GetString();
        }

        return (idempotencyKey, confirmationToken);
    }

    private static string SerializeResult(string id, JsonElement? result)
    {
        var envelope = new BridgeEnvelope(1, id, BridgeMessageTypes.Res, null, null, result, null);
        return JsonSerializer.Serialize(envelope, BridgeJson.Options);
    }

    private static string SerializeError(string id, OperationError failure)
    {
        var error = new BridgeErrorDto(failure.Code, failure.Message, failure.Retryable, failure.Details);
        var envelope = new BridgeEnvelope(1, id, BridgeMessageTypes.Res, null, null, null, error);
        return JsonSerializer.Serialize(envelope, BridgeJson.Options);
    }

    private static int EncodingLength(string json)
    {
        return System.Text.Encoding.UTF8.GetByteCount(json);
    }
}
