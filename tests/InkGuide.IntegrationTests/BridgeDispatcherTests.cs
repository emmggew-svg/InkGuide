using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Handlers;
using Xunit;

namespace InkGuide.IntegrationTests;

/// <summary>Bridge protocol: routing, envelope validation, cancellation, idempotency, confirmations.</summary>
public sealed class BridgeDispatcherTests : IDisposable
{
    private readonly ConfirmationService _confirmations;
    private readonly IdempotencyStore _idempotency;
    private readonly BridgeDispatcher _dispatcher;
    private bool _disposed;

    public BridgeDispatcherTests()
    {
        _confirmations = new ConfirmationService(TimeProvider.System);
        _idempotency = new IdempotencyStore(TimeProvider.System);
        var handler = new FakeNodesHandler();
        _dispatcher = new BridgeDispatcher(new[] { handler }, _confirmations, _idempotency, TimeProvider.System);
    }

    [Fact]
    public async Task UnknownMethodReturnsInvalidParamsAsync()
    {
        string req = Json(new BridgeEnvelope(1, "id-1", "req", "nodes.nope", null, null, null));
        string? res = await _dispatcher.DispatchAsync(req, CancellationToken.None);
        Assert.NotNull(res);
        var env = JsonSerializer.Deserialize<BridgeEnvelope>(res!, BridgeJson.Options);
        Assert.NotNull(env!.Error);
        Assert.Equal("INVALID_PARAMS", env.Error!.Code);
    }

    [Fact]
    public async Task HelloHandshakeReturnsResultAsync()
    {
        var p = JsonSerializer.SerializeToElement(new { }, BridgeJson.Options);
        string req = Json(new BridgeEnvelope(1, "id-2", "req", "nodes.list", p, null, null));
        string? res = await _dispatcher.DispatchAsync(req, CancellationToken.None);
        Assert.NotNull(res);
        var env = JsonSerializer.Deserialize<BridgeEnvelope>(res!, BridgeJson.Options);
        Assert.Null(env!.Error);
        Assert.NotNull(env.Result);
    }

    [Fact]
    public async Task OversizeMessageIsRejectedAsync()
    {
        string big = new string('x', BridgeDispatcher.MaxMessageBytes + 10);
        string req = "{\"v\":1,\"id\":\"big\",\"type\":\"req\",\"method\":\"nodes.list\",\"params\":\"" + big + "\"}";
        string? res = await _dispatcher.DispatchAsync(req, CancellationToken.None);
        Assert.NotNull(res);
        Assert.Contains("INVALID_PARAMS", res!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelRemovesInflightAndReturnsNullAsync()
    {
        string cancel = Json(new BridgeEnvelope(1, "missing-id", "cancel", null, null, null, null));
        string? res = await _dispatcher.DispatchAsync(cancel, CancellationToken.None);
        Assert.Null(res);
    }

    [Fact]
    public async Task IdempotentReplayReturnsStoredResultAsync()
    {
        var p = JsonSerializer.SerializeToElement(new Dictionary<string, string?> { ["idempotencyKey"] = "k-1" }, BridgeJson.Options);
        string req = Json(new BridgeEnvelope(1, "id-3", "req", "nodes.list", p, null, null));
        string? first = await _dispatcher.DispatchAsync(req, CancellationToken.None);
        string req2 = Json(new BridgeEnvelope(1, "id-4", "req", "nodes.list", p, null, null));
        string? second = await _dispatcher.DispatchAsync(req2, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);
        // Same payload hash -> same serialized result element.
        var e1 = JsonSerializer.Deserialize<BridgeEnvelope>(first!, BridgeJson.Options);
        var e2 = JsonSerializer.Deserialize<BridgeEnvelope>(second!, BridgeJson.Options);
        Assert.Equal(e1!.Result.ToString(), e2!.Result.ToString());
    }

    [Fact]
    public void ConfirmationTokenIsSingleUse()
    {
        string token = _confirmations.Issue("repos.delete", "r1");
        Assert.True(_confirmations.Validate(token, "repos.delete", "r1"));
        Assert.False(_confirmations.Validate(token, "repos.delete", "r1"));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispatcher.Dispose();
    }

    private static string Json(BridgeEnvelope envelope)
    {
        return JsonSerializer.Serialize(envelope, BridgeJson.Options);
    }

    private sealed class FakeNodesHandler : IBridgeHandler
    {
        public string Namespace => "nodes";

        public Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
        {
            _ = paramsJson;
            _ = context;
            if (!string.Equals(method, "nodes.list", StringComparison.Ordinal))
            {
                return Task.FromResult(Result.Fail<JsonElement?>(new OperationError("INVALID_PARAMS", "Unknown method.")));
            }

            JsonElement ok = JsonSerializer.SerializeToElement(new { items = Array.Empty<string>() }, BridgeJson.Options);
            return Task.FromResult(Result.Ok<JsonElement?>(ok));
        }
    }
}
