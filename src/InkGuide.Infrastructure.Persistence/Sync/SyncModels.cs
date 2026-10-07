using System;
using System.Collections.Generic;

namespace InkGuide.Infrastructure.Persistence.Sync;

/// <summary>Well-known outbox operation types.</summary>
public static class OutboxTypes
{
    public const string CreateFolder = "createFolder";
    public const string Upload = "upload";
    public const string UpdateContent = "updateContent";
    public const string Rename = "rename";
    public const string Move = "move";
    public const string Copy = "copy";
    public const string Trash = "trash";
    public const string Restore = "restore";
    public const string DeletePermanent = "deletePermanent";
    public const string SetMetadata = "setMetadata";
    public const string WriteManifest = "writeManifest";
}

/// <summary>Sync states exposed to the UI.</summary>
public enum SyncState
{
    Idle,
    Indexing,
    Syncing,
    Offline,
    Paused,
    Error,
    ReauthRequired,
}

/// <summary>Remote item view used by the sync engine (provider-agnostic subset).</summary>
public sealed record RemoteItem(
    string Id,
    string? ParentId,
    string Name,
    bool IsFolder,
    string? MimeType,
    long Size,
    string? Md5,
    string? Version,
    string? HeadRevisionId,
    DateTimeOffset ModifiedTimeUtc,
    bool Trashed,
    string? Description,
    IReadOnlyDictionary<string, string> AppProperties,
    string? WebViewLink);

/// <summary>One page of remote children.</summary>
public sealed record RemotePage(IReadOnlyList<RemoteItem> Items, string? NextCursor);

/// <summary>One remote change entry.</summary>
public sealed record RemoteChange(RemoteItem? Item, string ItemId, bool Removed);

/// <summary>Result of polling the remote change feed.</summary>
public sealed record RemoteChangeSet(IReadOnlyList<RemoteChange> Changes, string NextToken, bool MustReindex);

/// <summary>Indexing progress event payload.</summary>
public sealed record SyncProgress(string AccountId, int Indexed, int Changed, string? CurrentPath);
