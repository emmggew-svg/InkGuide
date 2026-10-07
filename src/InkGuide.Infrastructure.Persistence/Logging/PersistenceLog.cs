using System;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Persistence.Logging;

/// <summary>Source-generated high-performance log messages (CA1848).</summary>
internal static partial class PersistenceLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Schema version mismatch (db={DbVersion} expected={ExpectedVersion}); dropping disposable cache.")]
    public static partial void SchemaMismatch(ILogger logger, int dbVersion, int expectedVersion);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "FTS5 disabled by configuration; using LIKE fallback.")]
    public static partial void FtsDisabled(ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "FTS5 is unavailable; degrading to indexed LIKE search.")]
    public static partial void FtsUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "FTS index write failed; results will use LIKE fallback for this query.")]
    public static partial void FtsWriteFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "FTS query failed; falling back to LIKE.")]
    public static partial void FtsQueryFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = "Index failed for account {AccountId}.")]
    public static partial void IndexFailed(ILogger logger, Exception exception, string accountId);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Change token expired for account {AccountId}; triggering full re-index.")]
    public static partial void TokenExpired(ILogger logger, string accountId);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning, Message = "Sync call failed for account {AccountId} ({Failures} consecutive): {Detail}.")]
    public static partial void SyncCallFailed(ILogger logger, string accountId, int failures, string? detail);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Warning, Message = "Sync state hook failed.")]
    public static partial void SyncHookFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Outbox replay tick failed; will retry on the next tick.")]
    public static partial void OutboxTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning, Message = "Sync poll tick failed; will retry on the next tick.")]
    public static partial void PollTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Information, Message = "Rebuilding local mirror for account {AccountId} from provider.")]
    public static partial void RebuildStarted(ILogger logger, string accountId);
}
