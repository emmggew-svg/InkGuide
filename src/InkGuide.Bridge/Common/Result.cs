using System;
using System.Collections.Generic;

namespace InkGuide.Bridge.Common;

// Temporary local shim: canonical Result/Error types are owned by Domain/Application
// (see docs/DECISIONS.md D-006). Shape matches the other infrastructure shims.

/// <summary>Machine-readable failure. <see cref="Code"/> uses Appendix C bridge codes.</summary>
public sealed record OperationError(string Code, string Message, bool Retryable = false, IReadOnlyDictionary<string, string>? Details = null)
{
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
