using System;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Storage.Logging;

/// <summary>Source-generated high-performance log messages (CA1848).</summary>
internal static partial class StorageLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "Drive request failed (attempt {Attempt}): {Method} {Path} -> {Status}.")]
    public static partial void DriveRequestFailed(ILogger logger, int attempt, string method, string path, int status);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Drive circuit opened after sustained failures; provider reports offline.")]
    public static partial void CircuitOpened(ILogger logger);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "Drive circuit closed; provider reachable again.")]
    public static partial void CircuitClosed(ILogger logger);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Resuming upload session for {Name} at byte {Offset}.")]
    public static partial void UploadResumed(ILogger logger, string name, long offset);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "OAuth token refresh failed; account {AccountId} needs re-authentication.")]
    public static partial void ReauthRequired(ILogger logger, string accountId);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information, Message = "OAuth client is not configured; returning OAUTH_NOT_CONFIGURED.")]
    public static partial void OAuthNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning, Message = "LocalFolder journal entry out of order; triggering full re-index semantics.")]
    public static partial void LocalJournalGap(ILogger logger);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Information, Message = "Manifest {File} for repo {RepoId} merged with last-writer-wins ({Entries} entries).")]
    public static partial void ManifestMerged(ILogger logger, string file, string repoId, int entries);
}
