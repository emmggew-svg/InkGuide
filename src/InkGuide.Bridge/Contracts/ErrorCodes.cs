namespace InkGuide.Bridge.Contracts;

/// <summary>Bridge error codes (Appendix C). Only codes cross the bridge for UI use.</summary>
public static class ErrorCodes
{
    public const string Unknown = "UNKNOWN";
    public const string Internal = "INTERNAL";
    public const string InvalidParams = "INVALID_PARAMS";
    public const string NotFound = "NOT_FOUND";
    public const string AlreadyExists = "ALREADY_EXISTS";
    public const string ConflictVersion = "CONFLICT_VERSION";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string AuthRequired = "AUTH_REQUIRED";
    public const string OAuthNotConfigured = "OAUTH_NOT_CONFIGURED";
    public const string ReauthRequired = "REAUTH_REQUIRED";
    public const string Offline = "OFFLINE";
    public const string RateLimited = "RATE_LIMITED";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
    public const string TooLargeToEdit = "TOO_LARGE_TO_EDIT";
    public const string UnsupportedOperation = "UNSUPPORTED_OPERATION";
    public const string Cancelled = "CANCELLED";
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
    public const string ProviderError = "PROVIDER_ERROR";
    public const string IntegrityError = "INTEGRITY_ERROR";
    public const string PathRejected = "PATH_REJECTED";
    public const string SensitiveContentBlocked = "SENSITIVE_CONTENT_BLOCKED";
    public const string AiNotConfigured = "AI_NOT_CONFIGURED";
    public const string AiConsentRequired = "AI_CONSENT_REQUIRED";
    public const string AiBudgetExceeded = "AI_BUDGET_EXCEEDED";
    public const string UiIncompatible = "UI_INCOMPATIBLE";
}
