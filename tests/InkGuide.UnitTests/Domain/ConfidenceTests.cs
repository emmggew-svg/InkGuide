using System;
using InkGuide.Domain.Common;
using InkGuide.Domain.ValueObjects;
using Xunit;

namespace InkGuide.UnitTests.Domain;

public sealed class ConfidenceTests
{
    [Fact]
    public void CreateAcceptsBoundaries()
    {
        Assert.Equal(0.0, Confidence.Create(0.0).Value!.Value);
        Assert.Equal(1.0, Confidence.Create(1.0).Value!.Value);
        Assert.Equal(0.5, Confidence.Create(0.5).Value!.Value);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(42.0)]
    public void CreateRejectsOutOfRange(double value)
    {
        Result<Confidence, Failure> result = Confidence.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.InvalidParams, result.Error!.Code);
    }

    [Fact]
    public void CreateRejectsNotANumber()
    {
        Assert.True(Confidence.Create(double.NaN).IsFailure);
        Assert.True(Confidence.Create(double.PositiveInfinity).IsFailure);
    }

    [Fact]
    public void ClampKeepsValuesInsideRange()
    {
        Assert.Equal(0.0, Confidence.Clamp(-3.0).Value);
        Assert.Equal(1.0, Confidence.Clamp(99.0).Value);
        Assert.Equal(0.4, Confidence.Clamp(0.4).Value);
    }

    [Fact]
    public void ConstructorGuardsRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Confidence(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Confidence(-0.5));
    }

    [Fact]
    public void WellKnownValuesExist()
    {
        Assert.Equal(0.0, Confidence.Zero.Value);
        Assert.Equal(1.0, Confidence.Full.Value);
    }
}
