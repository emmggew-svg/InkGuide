using InkGuide.Domain.Common;
using Xunit;

namespace InkGuide.UnitTests.Domain;

public sealed class ErrorsTests
{
    [Fact]
    public void FactoriesUseAppendixCCodes()
    {
        Assert.Equal(ErrorCodes.NotFound, Errors.NotFound("node", "n1").Code);
        Assert.Equal(ErrorCodes.AlreadyExists, Errors.AlreadyExists("folder", "docs").Code);
        Assert.Equal(ErrorCodes.InvalidParams, Errors.InvalidParams("bad").Code);
        Assert.Equal(ErrorCodes.ConflictVersion, Errors.ConflictVersion("n1").Code);
        Assert.Equal(ErrorCodes.PermissionDenied, Errors.PermissionDenied("delete").Code);
        Assert.Equal(ErrorCodes.OAuthNotConfigured, Errors.OAuthNotConfigured().Code);
        Assert.Equal(ErrorCodes.ReauthRequired, Errors.ReauthRequired("a1").Code);
        Assert.Equal(ErrorCodes.Offline, Errors.Offline().Code);
        Assert.Equal(ErrorCodes.RateLimited, Errors.RateLimited().Code);
        Assert.Equal(ErrorCodes.ProviderError, Errors.ProviderError("boom", true).Code);
        Assert.Equal(ErrorCodes.IntegrityError, Errors.IntegrityError("hash").Code);
        Assert.Equal(ErrorCodes.PathRejected, Errors.PathRejected("a/b", "why").Code);
        Assert.Equal(ErrorCodes.SensitiveContentBlocked, Errors.SensitiveBlocked(".env").Code);
        Assert.Equal(ErrorCodes.ConfirmationRequired, Errors.ConfirmationRequired("wipe").Code);
        Assert.Equal(ErrorCodes.UnsupportedOperation, Errors.Unsupported("fly").Code);
        Assert.Equal(ErrorCodes.Internal, Errors.Internal("bug").Code);
    }

    [Fact]
    public void RetryableFlagsAreHonest()
    {
        Assert.True(Errors.Offline().Retryable);
        Assert.True(Errors.RateLimited().Retryable);
        Assert.True(Errors.ProviderError("x", true).Retryable);
        Assert.False(Errors.ProviderError("x", false).Retryable);
        Assert.False(Errors.NotFound("n", "1").Retryable);
        Assert.False(Errors.InvalidParams("x").Retryable);
    }

    [Fact]
    public void ErrorCodesMatchAppendixC()
    {
        Assert.Equal("UNKNOWN", ErrorCodes.Unknown);
        Assert.Equal("INTERNAL", ErrorCodes.Internal);
        Assert.Equal("INVALID_PARAMS", ErrorCodes.InvalidParams);
        Assert.Equal("NOT_FOUND", ErrorCodes.NotFound);
        Assert.Equal("ALREADY_EXISTS", ErrorCodes.AlreadyExists);
        Assert.Equal("CONFLICT_VERSION", ErrorCodes.ConflictVersion);
        Assert.Equal("PERMISSION_DENIED", ErrorCodes.PermissionDenied);
        Assert.Equal("AUTH_REQUIRED", ErrorCodes.AuthRequired);
        Assert.Equal("OAUTH_NOT_CONFIGURED", ErrorCodes.OAuthNotConfigured);
        Assert.Equal("REAUTH_REQUIRED", ErrorCodes.ReauthRequired);
        Assert.Equal("OFFLINE", ErrorCodes.Offline);
        Assert.Equal("RATE_LIMITED", ErrorCodes.RateLimited);
        Assert.Equal("QUOTA_EXCEEDED", ErrorCodes.QuotaExceeded);
        Assert.Equal("TOO_LARGE_TO_EDIT", ErrorCodes.TooLargeToEdit);
        Assert.Equal("UNSUPPORTED_OPERATION", ErrorCodes.UnsupportedOperation);
        Assert.Equal("CANCELLED", ErrorCodes.Cancelled);
        Assert.Equal("CONFIRMATION_REQUIRED", ErrorCodes.ConfirmationRequired);
        Assert.Equal("PROVIDER_ERROR", ErrorCodes.ProviderError);
        Assert.Equal("INTEGRITY_ERROR", ErrorCodes.IntegrityError);
        Assert.Equal("PATH_REJECTED", ErrorCodes.PathRejected);
        Assert.Equal("SENSITIVE_CONTENT_BLOCKED", ErrorCodes.SensitiveContentBlocked);
        Assert.Equal("AI_NOT_CONFIGURED", ErrorCodes.AiNotConfigured);
        Assert.Equal("AI_CONSENT_REQUIRED", ErrorCodes.AiConsentRequired);
        Assert.Equal("AI_BUDGET_EXCEEDED", ErrorCodes.AiBudgetExceeded);
        Assert.Equal("UI_INCOMPATIBLE", ErrorCodes.UiIncompatible);
    }

    [Fact]
    public void DetailsDefaultToEmpty()
    {
        Failure error = Errors.NotFound("node", "n1");

        Assert.NotNull(error.Details);
        Assert.Empty(error.Details);
    }

    [Fact]
    public void MessagesNameTheSubject()
    {
        Assert.Contains("n1", Errors.NotFound("node", "n1").Message);
        Assert.Contains("docs", Errors.AlreadyExists("folder", "docs").Message);
    }
}
