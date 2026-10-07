using System;
using InkGuide.Domain.Common;

namespace InkGuide.Domain.ValueObjects;

/// <summary>Validated 0-1 confidence score for an insight or detection.</summary>
public readonly record struct Confidence
{
    /// <summary>Score in the inclusive range 0-1.</summary>
    public double Value { get; }

    /// <summary>Creates a confidence score. Throws <see cref="ArgumentOutOfRangeException"/> outside 0-1.</summary>
    public Confidence(double value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1.0);
        Value = value;
    }

    /// <summary>Zero confidence.</summary>
    public static Confidence Zero => new(0.0);

    /// <summary>Full confidence.</summary>
    public static Confidence Full => new(1.0);

    /// <summary>Clamps any value into the 0-1 range without failing.</summary>
    public static Confidence Clamp(double value) => new(Math.Clamp(value, 0.0, 1.0));

    /// <summary>Creates a confidence score, returning an INVALID_PARAMS error when out of range.</summary>
    public static Result<Confidence, Failure> Create(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 || value > 1.0)
        {
            return Result.Failure<Confidence>(Errors.InvalidParams($"confidence {value} is outside 0-1"));
        }

        return Result.Success(new Confidence(value));
    }
}
