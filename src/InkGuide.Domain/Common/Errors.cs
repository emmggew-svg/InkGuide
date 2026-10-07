namespace InkGuide.Domain.Common;

/// <summary>Factory helpers for <see cref="Failure"/> values using the <see cref="ErrorCodes"/> catalog.</summary>
public static class Errors
{
    /// <summary>Builds a NOT_FOUND error.</summary>
    public static Failure NotFound(string entity, string id) =>
        new(ErrorCodes.NotFound, $"No {entity} with id '{id}' exists.", Retryable: false);

    /// <summary>Builds an ALREADY_EXISTS error.</summary>
    public static Failure AlreadyExists(string entity, string name) =>
        new(ErrorCodes.AlreadyExists, $"A {entity} named '{name}' already exists.", Retryable: false);

    /// <summary>Builds an INVALID_PARAMS error.</summary>
    public static Failure InvalidParams(string reason) =>
        new(ErrorCodes.InvalidParams, $"Invalid parameters: {reason}.", Retryable: false);

    /// <summary>Builds a CONFLICT_VERSION error.</summary>
    public static Failure ConflictVersion(string nodeId) =>
        new(ErrorCodes.ConflictVersion, $"Node '{nodeId}' changed remotely; the local write was kept as a conflict copy.", Retryable: false);

    /// <summary>Builds a PERMISSION_DENIED error.</summary>
    public static Failure PermissionDenied(string action) =>
        new(ErrorCodes.PermissionDenied, $"The '{action}' operation is not permitted.", Retryable: false);

    /// <summary>Builds an OAUTH_NOT_CONFIGURED error.</summary>
    public static Failure OAuthNotConfigured() =>
        new(ErrorCodes.OAuthNotConfigured, "No OAuth client is configured. Import a Desktop-app client JSON first.", Retryable: false);

    /// <summary>Builds a REAUTH_REQUIRED error.</summary>
    public static Failure ReauthRequired(string accountId) =>
        new(ErrorCodes.ReauthRequired, $"Account '{accountId}' needs re-authentication.", Retryable: false);

    /// <summary>Builds an OFFLINE error.</summary>
    public static Failure Offline() =>
        new(ErrorCodes.Offline, "The provider is unreachable. The intent was queued where possible.", Retryable: true);

    /// <summary>Builds a RATE_LIMITED error.</summary>
    public static Failure RateLimited() =>
        new(ErrorCodes.RateLimited, "The provider rate limit was hit. Back off and retry.", Retryable: true);

    /// <summary>Builds a PROVIDER_ERROR error.</summary>
    public static Failure ProviderError(string detail, bool retryable) =>
        new(ErrorCodes.ProviderError, $"The storage provider failed: {detail}.", retryable);

    /// <summary>Builds an INTEGRITY_ERROR error.</summary>
    public static Failure IntegrityError(string reason) =>
        new(ErrorCodes.IntegrityError, $"An integrity check failed: {reason}.", Retryable: false);

    /// <summary>Builds a PATH_REJECTED error.</summary>
    public static Failure PathRejected(string path, string reason) =>
        new(ErrorCodes.PathRejected, $"The path '{path}' was rejected: {reason}.", Retryable: false);

    /// <summary>Builds a SENSITIVE_CONTENT_BLOCKED error.</summary>
    public static Failure SensitiveBlocked(string nodeName) =>
        new(ErrorCodes.SensitiveContentBlocked, $"Node '{nodeName}' is classified Sensitive and is excluded from this operation.", Retryable: false);

    /// <summary>Builds a CONFIRMATION_REQUIRED error.</summary>
    public static Failure ConfirmationRequired(string action) =>
        new(ErrorCodes.ConfirmationRequired, $"The '{action}' operation requires an explicit confirmation token.", Retryable: false);

    /// <summary>Builds an UNSUPPORTED_OPERATION error.</summary>
    public static Failure Unsupported(string operation) =>
        new(ErrorCodes.UnsupportedOperation, $"The '{operation}' operation is not supported here.", Retryable: false);

    /// <summary>Builds an INTERNAL error for unexpected failures.</summary>
    public static Failure Internal(string detail) =>
        new(ErrorCodes.Internal, $"An unexpected internal error occurred: {detail}.", Retryable: false);
}
