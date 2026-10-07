namespace InkGuide.Domain.Common;

/// <summary>
/// Stable bridge error codes (MASTER_PROMPT Appendix C) with factory helpers.
/// Codes are the only failure information that crosses the bridge for UI use.
/// </summary>
public static class ErrorCodes
{
    /// <summary>Uncategorised failure.</summary>
    public const string Unknown = "UNKNOWN";

    /// <summary>Unexpected internal failure (bug).</summary>
    public const string Internal = "INTERNAL";

    /// <summary>Caller supplied invalid parameters.</summary>
    public const string InvalidParams = "INVALID_PARAMS";

    /// <summary>Requested item does not exist.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Item already exists and the naming policy forbids a duplicate.</summary>
    public const string AlreadyExists = "ALREADY_EXISTS";

    /// <summary>Write was based on a stale version; the remote version moved.</summary>
    public const string ConflictVersion = "CONFLICT_VERSION";

    /// <summary>Operation is not permitted on this item.</summary>
    public const string PermissionDenied = "PERMISSION_DENIED";

    /// <summary>Authentication is required.</summary>
    public const string AuthRequired = "AUTH_REQUIRED";

    /// <summary>No OAuth client is configured; the owner must import one.</summary>
    public const string OAuthNotConfigured = "OAUTH_NOT_CONFIGURED";

    /// <summary>Stored grant was revoked or expired; re-authentication is required.</summary>
    public const string ReauthRequired = "REAUTH_REQUIRED";

    /// <summary>Device or provider is offline; the intent was queued when possible.</summary>
    public const string Offline = "OFFLINE";

    /// <summary>Provider rate limit hit; back off and retry.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>Storage quota is exhausted.</summary>
    public const string QuotaExceeded = "QUOTA_EXCEEDED";

    /// <summary>File is too large for in-app editing; use external edit.</summary>
    public const string TooLargeToEdit = "TOO_LARGE_TO_EDIT";

    /// <summary>Operation is not supported by this provider or for this item.</summary>
    public const string UnsupportedOperation = "UNSUPPORTED_OPERATION";

    /// <summary>Operation was cancelled by the caller.</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>Destructive operation needs an explicit confirmation token.</summary>
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";

    /// <summary>Storage provider returned an error.</summary>
    public const string ProviderError = "PROVIDER_ERROR";

    /// <summary>Cached data failed an integrity check and must be rebuilt.</summary>
    public const string IntegrityError = "INTEGRITY_ERROR";

    /// <summary>File name or path was rejected by the safety rules.</summary>
    public const string PathRejected = "PATH_REJECTED";

    /// <summary>Operation refused because the node is classified Sensitive.</summary>
    public const string SensitiveContentBlocked = "SENSITIVE_CONTENT_BLOCKED";

    /// <summary>AI describer has no API key configured.</summary>
    public const string AiNotConfigured = "AI_NOT_CONFIGURED";

    /// <summary>AI describer needs explicit data-sharing consent first.</summary>
    public const string AiConsentRequired = "AI_CONSENT_REQUIRED";

    /// <summary>Daily AI request budget is exhausted.</summary>
    public const string AiBudgetExceeded = "AI_BUDGET_EXCEEDED";

    /// <summary>UI contract version is incompatible with the host.</summary>
    public const string UiIncompatible = "UI_INCOMPATIBLE";
}
