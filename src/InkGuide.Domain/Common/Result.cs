using System;

namespace InkGuide.Domain.Common;

/// <summary>
/// Outcome of an operation that yields a value on success or a typed failure otherwise.
/// Expected failures are returned, never thrown; exceptions are reserved for bugs.
/// Create values with the non-generic Result factory class.
/// </summary>
/// <typeparam name="TValue">Success value type.</typeparam>
/// <typeparam name="TFailure">Failure value type (usually Failure).</typeparam>
public readonly record struct Result<TValue, TFailure>
{
    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Success value. Set only when <see cref="IsSuccess"/> is true.</summary>
    public TValue? Value { get; init; }

    /// <summary>Failure value. Set only when <see cref="IsSuccess"/> is false.</summary>
    public TFailure? Error { get; init; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Folds the result into a single value. A null success value reaches onSuccess only when TValue is nullable.</summary>
    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<TFailure, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        if (IsSuccess)
        {
            return onSuccess(Value!);
        }

        ArgumentNullException.ThrowIfNull(Error);
        return onFailure(Error);
    }

    /// <summary>Transforms the success value, preserving any failure.</summary>
    public Result<TNext, TFailure> Map<TNext>(Func<TValue, TNext> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (IsSuccess)
        {
            TNext next = map(Value!);
            return new Result<TNext, TFailure> { IsSuccess = true, Value = next };
        }

        ArgumentNullException.ThrowIfNull(Error);
        return new Result<TNext, TFailure> { IsSuccess = false, Error = Error };
    }

    /// <summary>Chains another fallible operation, preserving any failure.</summary>
    public Result<TNext, TFailure> Bind<TNext>(Func<TValue, Result<TNext, TFailure>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        if (IsSuccess)
        {
            return bind(Value!);
        }

        ArgumentNullException.ThrowIfNull(Error);
        return new Result<TNext, TFailure> { IsSuccess = false, Error = Error };
    }
}

/// <summary>Factory methods for results with Failure failures.</summary>
public static class Result
{
    /// <summary>Creates a successful result. A null value is allowed only when TValue is nullable.</summary>
    public static Result<TValue, Failure> Success<TValue>(TValue value)
    {
        return new Result<TValue, Failure> { IsSuccess = true, Value = value };
    }

    /// <summary>Creates a failed result. The error must not be null.</summary>
    public static Result<TValue, Failure> Failure<TValue>(Failure error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue, Failure> { IsSuccess = false, Error = error };
    }

    /// <summary>Creates a successful result for operations without a payload.</summary>
    public static Result<Unit, Failure> Success() => new() { IsSuccess = true };

    /// <summary>Creates a failed result for operations without a payload.</summary>
    public static Result<Unit, Failure> Failure(Failure error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<Unit, Failure> { IsSuccess = false, Error = error };
    }
}
