using System.Globalization;
using InkGuide.Domain.Common;
using Xunit;

namespace InkGuide.UnitTests.Domain;

public sealed class ResultTests
{
    [Fact]
    public void SuccessCarriesValue()
    {
        Result<int, Failure> result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FailureCarriesError()
    {
        Failure error = Errors.NotFound("node", "n1");

        Result<int, Failure> result = Result.Failure<int>(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void SuccessAcceptsNullForNullableValueType()
    {
        Result<string?, Failure> result = Result.Success<string?>(null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void MatchFoldsSuccess()
    {
        Result<int, Failure> result = Result.Success(7);

        string folded = result.Match(value => "ok", error => "bad");

        Assert.Equal("ok", folded);
    }

    [Fact]
    public void MatchFoldsFailure()
    {
        Result<int, Failure> result = Result.Failure<int>(Errors.Offline());

        string folded = result.Match(value => "ok", error => error.Code);

        Assert.Equal(ErrorCodes.Offline, folded);
    }

    [Fact]
    public void MapTransformsSuccess()
    {
        Result<int, Failure> result = Result.Success(7);

        Result<string, Failure> mapped = result.Map(value => value.ToString(CultureInfo.InvariantCulture));

        Assert.True(mapped.IsSuccess);
        Assert.Equal("7", mapped.Value);
    }

    [Fact]
    public void MapPreservesFailure()
    {
        Failure error = Errors.RateLimited();
        Result<int, Failure> result = Result.Failure<int>(error);

        Result<string, Failure> mapped = result.Map(value => value.ToString(CultureInfo.InvariantCulture));

        Assert.True(mapped.IsFailure);
        Assert.Equal(error, mapped.Error);
    }

    [Fact]
    public void BindChainsSuccess()
    {
        Result<int, Failure> result = Result.Success(7);

        Result<string, Failure> bound = result.Bind(value => Result.Success(value.ToString(CultureInfo.InvariantCulture)));

        Assert.True(bound.IsSuccess);
        Assert.Equal("7", bound.Value);
    }

    [Fact]
    public void BindShortCircuitsFailure()
    {
        Failure error = Errors.Offline();
        Result<int, Failure> result = Result.Failure<int>(error);

        Result<string, Failure> bound = result.Bind(value => Result.Success(value.ToString(CultureInfo.InvariantCulture)));

        Assert.True(bound.IsFailure);
        Assert.Equal(error, bound.Error);
    }

    [Fact]
    public void UnitSuccessHasNoPayload()
    {
        Result<Unit, Failure> result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void UnitFailureCarriesError()
    {
        Failure error = Errors.ConfirmationRequired("delete");

        Result<Unit, Failure> result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ConfirmationRequired, result.Error!.Code);
    }

    [Fact]
    public void ToResultLiftsFailure()
    {
        Failure error = Errors.NotFound("node", "n1");

        Result<int, Failure> result = error.ToResult<int>();

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }
}
