using System;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Platform.Logging;

/// <summary>Source-generated high-performance log messages (CA1848).</summary>
internal static partial class PlatformLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Content cache evicted {Count} entries ({Bytes} bytes) to stay under the cap.")]
    public static partial void CacheEvicted(ILogger logger, int count, long bytes);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "External-edit change handler failed for session {SessionId}.")]
    public static partial void ExternalHandlerFailed(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "Re-adopted external-edit session {SessionId} for {FileName}.")]
    public static partial void SessionReadopted(ILogger logger, string sessionId, string fileName);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "Dropped stale external-edit session {SessionId}.")]
    public static partial void SessionDropped(ILogger logger, string sessionId);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Warning, Message = "AI request failed (attempt {Attempt}): {Detail}.")]
    public static partial void AiRequestFailed(ILogger logger, int attempt, string detail);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Information, Message = "AI daily budget exhausted ({Used}/{Budget}).")]
    public static partial void AiBudgetExhausted(ILogger logger, int used, int budget);
}
