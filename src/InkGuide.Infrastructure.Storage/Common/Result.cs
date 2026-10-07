using System;
using System.Collections.Generic;

namespace InkGuide.Infrastructure.Storage.Common;

// Temporary local shim: canonical Result/Error types are owned by Domain/Application
// (see docs/DECISIONS.md D-006). Shape matches the Persistence shim for unification.

/// <summary>Machine-readable failure. <see cref="Code"/> uses Appendix C bridge codes.</summary>
public sealed record OperationError(string Code, string Message, bool Retryable = false, IReadOnlyDictionary<string, string>? Details = null)
{
    public static OperationError NotFound(string message) => new("NOT_FOUND", message);

    public static OperationError AlreadyExists(string message) => new("ALREADY_EXISTS", message);

    public static OperationError Conflict(string message) => new("CONFLICT_VERSION", message);

    public static OperationError Offline(string message) => new("OFFLINE", message, true);

    public static OperationError RateLimited(string message) => new("RATE_LIMITED", message, true);

    public static OperationError QuotaExceeded(string message) => new("QUOTA_EXCEEDED", message);

    public static OperationError Cancelled(string message) => new("CANCELLED", message);

    public static OperationError AuthRequired(string message) => new("AUTH_REQUIRED", message);

    public static OperationError ReauthRequired(string message) => new("REAUTH_REQUIRED", message);

    public static OperationError Provider(string message, bool retryable = false) => new("PROVIDER_ERROR", message, retryable);

    public static OperationError Unsupported(string message) => new("UNSUPPORTED_OPERATION", message);

    public static OperationError Internal(string message) => new("INTERNAL", message);

    public static OperationError Invalid(string message) => new("INVALID_PARAMS", message);
}

/// <summary>Success/failure wrapper for a value-returning operation. Create via <see cref="Result"/>.</summary>
/// <typeparam name="T">Value type on success.</typeparam>
public sealed class Result<T>
{
    public bool IsSuccess { get; }

    public T? Value { get; }

    public OperationError? Failure { get; }

    internal Result(bool isSuccess, T? value, OperationError? failure)
    {
        IsSuccess = isSuccess;
        Value = value;
        Failure = failure;
    }
}

/// <summary>Factories for <see cref="Result{T}"/> and the void result.</summary>
public sealed class Result
{
    public bool IsSuccess { get; }

    public OperationError? Failure { get; }

    private Result(bool isSuccess, OperationError? failure)
    {
        IsSuccess = isSuccess;
        Failure = failure;
    }

    public static Result Ok() => new(true, null);

    public static Result<T> Ok<T>(T? value) => new(true, value, null);

    public static Result Fail(OperationError failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Result(false, failure);
    }

    public static Result<T> Fail<T>(OperationError failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Result<T>(false, default, failure);
    }

    public static Result Fail(string code, string message, bool retryable = false)
    {
        return new Result(false, new OperationError(code, message, retryable));
    }

    public static Result<T> Fail<T>(string code, string message, bool retryable = false)
    {
        return new Result<T>(false, default, new OperationError(code, message, retryable));
    }
}
