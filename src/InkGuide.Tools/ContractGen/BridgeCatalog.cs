using System;
using System.Collections.Generic;

namespace InkGuide.Tools.ContractGen;

/// <summary>
/// Baseline bridge catalog (MASTER_PROMPT Sections 14.2, 15 and Appendix C).
/// The Bridge owner refines params/result shapes via C# DTOs; this table
/// guarantees the generated contract is always complete and checkable.
/// </summary>
internal static class BridgeCatalog
{
    public const int ContractMajor = 1;
    public const int ContractMinor = 0;
    public const int MaxMessageBytes = 1024 * 1024;

    public static IReadOnlyList<BridgeNamespace> Namespaces { get; } = new List<BridgeNamespace>
    {
        new("app", "Host identity and lifecycle.", new[]
        {
            "hello", "getInfo", "getUiStatus", "quit", "restart", "openLogsFolder", "openExternalUrl",
        }),
        new("window", "Native window surface.", new[] { "setBackground", "setTitle" }),
        new("settings", "Configuration and durable UI state.", new[] { "get", "update", "reset", "getUiState", "setUiState" }),
        new("accounts", "Google account connections.", new[] { "list", "connect", "cancelConnect", "disconnect", "setActive", "importOAuthClient", "getOAuthStatus" }),
        new("workspace", "Workspace root.", new[] { "get", "ensure", "getStats" }),
        new("repos", "Repositories.", new[] { "list", "create", "rename", "delete", "get", "getOverview", "exportZip", "adoptExistingFolder" }),
        new("projects", "Projects inside mono repos.", new[] { "list", "get", "confirm", "update", "remove", "move" }),
        new("nodes", "Files and folders.", new[]
        {
            "list", "get", "getPath", "createFolder", "createFile", "rename", "move", "copy",
            "trash", "restore", "prepareDelete", "deletePermanent", "readText", "writeText",
            "getPreview", "openExternal", "openInBrowser", "revisions", "restoreRevision",
            "setFavorite", "listFavorites", "listTrash", "emptyTrash",
        }),
        new("transfers", "Uploads and downloads.", new[] { "uploadPaths", "download", "list", "pause", "resume", "cancel", "retry", "clearFinished" }),
        new("import", "Local folder import.", new[] { "plan", "execute", "status" }),
        new("insight", "File insights.", new[] { "get", "reanalyze", "setSummary", "setKind", "bulkReanalyze", "getQueueStatus" }),
        new("tags", "Tags.", new[] { "list", "create", "update", "merge", "delete", "assign", "unassign", "bulkAssign", "getUsage", "listSuggestions", "acceptSuggestion", "dismissSuggestion" }),
        new("search", "Search.", new[] { "query", "suggest" }),
        new("sync", "Sync engine and outbox.", new[] { "getStatus", "syncNow", "pause", "resume", "listOutbox", "retryOutbox", "discardOutbox", "listConflicts", "getConflict", "resolveConflict" }),
        new("snapshots", "Snapshots.", new[] { "create", "list", "get", "diff", "textDiff", "restore", "delete" }),
        new("activity", "Activity journal and undo.", new[] { "list", "undo" }),
        new("organize", "Inbox and organizer.", new[] { "listInbox", "suggest", "apply", "getSettings" }),
        new("duplicates", "Duplicate finder.", new[] { "scan", "list", "resolve" }),
        new("external", "External edit sessions.", new[] { "listSessions", "endSession" }),
        new("ai", "Optional AI describer.", new[] { "getStatus", "setKey", "clearKey", "acknowledgeDataSharing", "test", "getUsage" }),
        new("storage", "Storage and cache.", new[] { "getUsage", "getCacheStats", "clearCache" }),
        new("rules", "Analysis rule catalogs.", new[] { "getCatalogInfo", "reload", "validate" }),
        new("diagnostics", "Diagnostics.", new[] { "exportBundle", "getHealth", "log" }),
    };

    public static IReadOnlyList<BridgeEvent> Events { get; } = new List<BridgeEvent>
    {
        new("account.stateChanged", "Account connection state changed."),
        new("sync.stateChanged", "Sync engine state changed."),
        new("sync.progress", "Index/sync progress."),
        new("node.changed", "Nodes changed (batched)."),
        new("transfer.progress", "Transfer progress."),
        new("analysis.progress", "Analysis queue progress."),
        new("insight.updated", "An insight was (re)computed."),
        new("conflict.created", "A sync conflict needs resolution."),
        new("external.sessionChanged", "External edit sessions changed."),
        new("app.openPaths", "Second-instance launch paths forwarded."),
        new("settings.changed", "Settings changed."),
        new("error.global", "Unexpected host failure."),
    };

    public static IReadOnlyList<BridgeErrorCode> ErrorCodes { get; } = new List<BridgeErrorCode>
    {
        new("UNKNOWN", "Unclassified failure. Not retryable by default."),
        new("INTERNAL", "Host bug or unhandled exception."),
        new("INVALID_PARAMS", "Malformed or invalid request params."),
        new("NOT_FOUND", "Target node/repo/account does not exist."),
        new("ALREADY_EXISTS", "A same-named item already exists (naming policy block)."),
        new("CONFLICT_VERSION", "baseVersion mismatch; remote moved first."),
        new("PERMISSION_DENIED", "Provider refused the operation."),
        new("AUTH_REQUIRED", "No active account."),
        new("OAUTH_NOT_CONFIGURED", "No OAuth client configured; see docs/GOOGLE_SETUP.md."),
        new("REAUTH_REQUIRED", "Token revoked/expired; reconnect the account."),
        new("OFFLINE", "No connectivity; reads served from cache, writes queued."),
        new("RATE_LIMITED", "Provider throttled the request; retry with backoff."),
        new("QUOTA_EXCEEDED", "Drive storage quota exceeded."),
        new("TOO_LARGE_TO_EDIT", "File exceeds MAX_INLINE_EDIT_SIZE; use external edit."),
        new("UNSUPPORTED_OPERATION", "Provider cannot do this (e.g. revisions on LocalFolder)."),
        new("CANCELLED", "Request was cancelled."),
        new("CONFIRMATION_REQUIRED", "Destructive op needs a confirmation token first."),
        new("PROVIDER_ERROR", "Provider call failed after retries."),
        new("INTEGRITY_ERROR", "Hash/version integrity check failed."),
        new("PATH_REJECTED", "Path unsafe or outside allowed roots."),
        new("SENSITIVE_CONTENT_BLOCKED", "Sensitive node excluded from indexing/AI."),
        new("AI_NOT_CONFIGURED", "No AI key configured."),
        new("AI_CONSENT_REQUIRED", "Data-sharing consent not recorded."),
        new("AI_BUDGET_EXCEEDED", "Daily AI request budget exhausted."),
        new("UI_INCOMPATIBLE", "UI contract major mismatch."),
    };

    public static string MethodId(string ns, string method) => ns + "." + method;
}

internal sealed record BridgeNamespace(string Name, string Description, string[] Methods);
internal sealed record BridgeEvent(string Name, string Description);
internal sealed record BridgeErrorCode(string Code, string Description);
