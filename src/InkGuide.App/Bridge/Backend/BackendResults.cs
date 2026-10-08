using System;
using System.Collections.Generic;
using BridgeCommon = InkGuide.Bridge.Common;
using DomainCommon = InkGuide.Domain.Common;
using PersistenceCommon = InkGuide.Infrastructure.Persistence.Common;
using PlatformCommon = InkGuide.Infrastructure.Platform.Common;
using StorageCommon = InkGuide.Infrastructure.Storage.Common;

namespace InkGuide.App.Bridge.Backend;

/// <summary>
/// Maps the fragmented Result flavors (Domain, Persistence, Storage, Platform)
/// onto the bridge Result. All layers already use Appendix C codes, so mapping
/// is a field copy — never a reinterpretation.
/// </summary>
internal static class BackendResults
{
    public static BridgeCommon.OperationError ToBridge(PersistenceCommon.OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new BridgeCommon.OperationError(error.Code, error.Message, error.Retryable, error.Details);
    }

    public static BridgeCommon.OperationError ToBridge(StorageCommon.OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new BridgeCommon.OperationError(error.Code, error.Message, error.Retryable, error.Details);
    }

    public static BridgeCommon.OperationError ToBridge(PlatformCommon.OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new BridgeCommon.OperationError(error.Code, error.Message, error.Retryable, error.Details);
    }

    public static BridgeCommon.OperationError ToBridge(DomainCommon.Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new BridgeCommon.OperationError(failure.Code, failure.Message, failure.Retryable, failure.Details);
    }

    public static BridgeCommon.Result<T> FromPersistence<T>(PersistenceCommon.Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return BridgeCommon.Result.Ok(result.Value);
        }

        ArgumentNullException.ThrowIfNull(result.Failure);
        return BridgeCommon.Result.Fail<T>(ToBridge(result.Failure));
    }

    public static BridgeCommon.Result FromPersistence(PersistenceCommon.Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return BridgeCommon.Result.Ok();
        }

        ArgumentNullException.ThrowIfNull(result.Failure);
        return BridgeCommon.Result.Fail(ToBridge(result.Failure));
    }

    public static BridgeCommon.Result<T> FromStorage<T>(StorageCommon.Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return BridgeCommon.Result.Ok(result.Value);
        }

        ArgumentNullException.ThrowIfNull(result.Failure);
        return BridgeCommon.Result.Fail<T>(ToBridge(result.Failure));
    }

    public static BridgeCommon.Result FromStorage(StorageCommon.Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return BridgeCommon.Result.Ok();
        }

        ArgumentNullException.ThrowIfNull(result.Failure);
        return BridgeCommon.Result.Fail(ToBridge(result.Failure));
    }

    public static BridgeCommon.Result<T> FromPlatform<T>(PlatformCommon.Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return BridgeCommon.Result.Ok(result.Value);
        }

        ArgumentNullException.ThrowIfNull(result.Failure);
        return BridgeCommon.Result.Fail<T>(ToBridge(result.Failure));
    }

    public static BridgeCommon.Result FromPlatform(PlatformCommon.Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return BridgeCommon.Result.Ok();
        }

        ArgumentNullException.ThrowIfNull(result.Failure);
        return BridgeCommon.Result.Fail(ToBridge(result.Failure));
    }

    public static BridgeCommon.Result<T> FromDomain<T>(DomainCommon.Result<T, DomainCommon.Failure> result)
    {
        if (result.IsSuccess)
        {
            return BridgeCommon.Result.Ok(result.Value);
        }

        ArgumentNullException.ThrowIfNull(result.Error);
        return BridgeCommon.Result.Fail<T>(ToBridge(result.Error));
    }

    public static BridgeCommon.OperationError NotFound(string message) => new("NOT_FOUND", message, false, null);

    public static BridgeCommon.OperationError InvalidParams(string message) => new("INVALID_PARAMS", message, false, null);

    public static BridgeCommon.OperationError Internal(string message) => new("INTERNAL", message, false, null);

    public static BridgeCommon.OperationError Offline(string message) => new("OFFLINE", message, false, null);

    public static BridgeCommon.OperationError ConfirmationRequired(string action, string scope, string token, string summary)
    {
        return new BridgeCommon.OperationError(
            "CONFIRMATION_REQUIRED",
            summary,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["confirmationToken"] = token,
                ["action"] = action,
                ["scope"] = scope,
            });
    }
}
