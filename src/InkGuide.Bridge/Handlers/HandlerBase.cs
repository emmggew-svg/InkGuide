using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Services;

namespace InkGuide.Bridge.Handlers;

/// <summary>Base class for namespace handlers (param parsing + result wrapping).</summary>
public abstract class HandlerBase : IBridgeHandler
{
    protected HandlerBase(IBridgeBackend backend, ConfirmationService confirmations)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(confirmations);
        Backend = backend;
        Confirmations = confirmations;
    }

    /// <inheritdoc />
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Namespace is the bridge-protocol term (Sec 15); renamed would diverge from docs/contract. See DECISIONS D-004.")]
    public abstract string Namespace { get; }

    /// <inheritdoc />
    public abstract Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context);

    /// <summary>Backend seam.</summary>
    protected IBridgeBackend Backend { get; }

    /// <summary>Confirmation tokens.</summary>
    protected ConfirmationService Confirmations { get; }

    /// <summary>Calls a backend method with parsed params.</summary>
    protected static async Task<Result<JsonElement?>> Call<TReq, TRes>(JsonElement? json, BridgeRequestContext context, Func<TReq, CancellationToken, Task<Result<TRes>>> invoke)
        where TReq : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);
        Result<TReq> parsed = Parse<TReq>(json);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Result.Fail<JsonElement?>(parsed.Failure!);
        }

        Result<TRes> result = await invoke(parsed.Value, context.CancellationToken).ConfigureAwait(false);
        return Wrap(result);
    }

    /// <summary>Calls a parameterless backend method.</summary>
    protected static async Task<Result<JsonElement?>> Call<TRes>(BridgeRequestContext context, Func<CancellationToken, Task<Result<TRes>>> invoke)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);
        Result<TRes> result = await invoke(context.CancellationToken).ConfigureAwait(false);
        return Wrap(result);
    }

    /// <summary>Wraps a backend result as a JSON element.</summary>
    protected static Result<JsonElement?> Wrap<T>(Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsSuccess || result.Failure is not null)
        {
            return Result.Fail<JsonElement?>(result.Failure ?? new OperationError(ErrorCodes.Internal, "Empty failure."));
        }

        JsonElement? element = result.Value is null
            ? null
            : JsonSerializer.SerializeToElement(result.Value, BridgeJson.Options);
        return Result.Ok(element);
    }

    /// <summary>Parses params or returns INVALID_PARAMS.</summary>
    protected static Result<T> Parse<T>(JsonElement? json) where T : class
    {
        if (json is null || json.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Result.Fail<T>(new OperationError(ErrorCodes.InvalidParams, "Missing params."));
        }

        try
        {
            T? value = json.Value.Deserialize<T>(BridgeJson.Options);
            return value is null
                ? Result.Fail<T>(new OperationError(ErrorCodes.InvalidParams, "Params did not match the contract."))
                : Result.Ok(value);
        }
        catch (JsonException ex)
        {
            return Result.Fail<T>(new OperationError(ErrorCodes.InvalidParams, "Invalid params: " + ex.Message));
        }
    }

    /// <summary>Returns an error result.</summary>
    protected static Result<JsonElement?> Fail(OperationError failure)
    {
        return Result.Fail<JsonElement?>(failure);
    }

    /// <summary>Unknown-method error.</summary>
    protected static Result<JsonElement?> Unknown(string method)
    {
        return Result.Fail<JsonElement?>(new OperationError(ErrorCodes.InvalidParams, $"Unknown method '{method}'."));
    }
}
