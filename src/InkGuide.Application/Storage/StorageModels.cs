using System;
using System.Collections.Generic;

namespace InkGuide.Application.Storage;

/// <summary>What a storage provider implementation can do. Drives UI affordances and sync planning.</summary>
/// <param name="Name">Provider display name (for example <c>GoogleDrive</c>, <c>LocalFolder</c>).</param>
/// <param name="SupportsRevisions">Whether revision listing, pinning and restore are available.</param>
/// <param name="SupportsExport">Whether Google-native style export to another MIME type is available.</param>
/// <param name="SupportsChangeFeed">Whether incremental change tokens are available.</param>
/// <param name="SupportsServerSideCopy">Whether copy/snapshot can run without re-uploading bytes.</param>
/// <param name="MaxChunkBytes">Maximum resumable-upload chunk size in bytes.</param>
public sealed record ProviderCapabilities(
    string Name,
    bool SupportsRevisions,
    bool SupportsExport,
    bool SupportsChangeFeed,
    bool SupportsServerSideCopy,
    long MaxChunkBytes);

/// <summary>Per-item capability flags reported by the provider.</summary>
/// <param name="CanEdit">Content can be edited.</param>
/// <param name="CanTrash">Item can be moved to trash.</param>
/// <param name="CanDelete">Item can be permanently deleted.</param>
/// <param name="CanRename">Item can be renamed.</param>
/// <param name="CanMove">Item can be moved within the drive.</param>
public sealed record ItemCapabilities(bool CanEdit, bool CanTrash, bool CanDelete, bool CanRename, bool CanMove);

/// <summary>Provider-side view of one file or folder.</summary>
public sealed class RemoteNode
{
    /// <summary>Provider id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Item name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>True for folders.</summary>
    public bool IsFolder { get; set; }

    /// <summary>MIME type. Null when unknown.</summary>
    public string? MimeType { get; set; }

    /// <summary>Content size in bytes. Null for folders.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>MD5 checksum. Null for folders and Google-native files.</summary>
    public string? Md5Checksum { get; set; }

    /// <summary>Version token for conflict-safe saves.</summary>
    public string? Version { get; set; }

    /// <summary>Head revision id. Null when revisions are unsupported.</summary>
    public string? HeadRevisionId { get; set; }

    /// <summary>Last modification time (UTC).</summary>
    public DateTimeOffset ModifiedTimeUtc { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTimeOffset CreatedTimeUtc { get; set; }

    /// <summary>True when trashed.</summary>
    public bool Trashed { get; set; }

    /// <summary>Parent ids reported by the provider.</summary>
    public IReadOnlyList<string> ParentIds { get; set; } = Array.Empty<string>();

    /// <summary>Human summary mirrored from the description field. Null when none.</summary>
    public string? Description { get; set; }

    /// <summary>Compact machine facts (inkguide.* keys).</summary>
    public IReadOnlyDictionary<string, string> AppProperties { get; set; } = new Dictionary<string, string>();

    /// <summary>Browser link for Google-native files. Null otherwise.</summary>
    public string? WebViewLink { get; set; }

    /// <summary>Allowed operations on this item.</summary>
    public ItemCapabilities Capabilities { get; set; } = new ItemCapabilities(false, false, false, false, false);
}

/// <summary>One page of children plus the cursor for the next page.</summary>
/// <param name="Items">Child nodes on this page.</param>
/// <param name="NextCursor">Cursor for the next page. Null when complete.</param>
public sealed record ChildPage(IReadOnlyList<RemoteNode> Items, string? NextCursor);

/// <summary>One entry of an incremental change feed.</summary>
public sealed class RemoteChange
{
    /// <summary>Changed node id.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>True when the node was removed (deleted or trashed beyond recovery).</summary>
    public bool IsRemoved { get; set; }

    /// <summary>Current state of the node. Null when removed.</summary>
    public RemoteNode? Node { get; set; }
}

/// <summary>One page of a change feed plus the token for the next poll.</summary>
/// <param name="Items">Changes on this page.</param>
/// <param name="NextToken">Token to resume from on the next poll.</param>
public sealed record ChangePage(IReadOnlyList<RemoteChange> Items, string NextToken);

/// <summary>One preserved revision of a file.</summary>
public sealed class RevisionInfo
{
    /// <summary>Revision id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Size in bytes. Null when unknown.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>MD5 checksum. Null when unknown.</summary>
    public string? Md5Checksum { get; set; }

    /// <summary>When the revision was created (UTC).</summary>
    public DateTimeOffset ModifiedTimeUtc { get; set; }

    /// <summary>True when pinned against provider-side purging.</summary>
    public bool Pinned { get; set; }
}

/// <summary>Storage quota snapshot.</summary>
/// <param name="TotalBytes">Total quota in bytes. Null when unlimited or unknown.</param>
/// <param name="UsedBytes">Used bytes.</param>
public sealed record QuotaInfo(long? TotalBytes, long UsedBytes);

/// <summary>Provider account identity.</summary>
/// <param name="Id">Provider account id.</param>
/// <param name="Email">Account e-mail. Null when the scope forbids reading it.</param>
/// <param name="DisplayName">Display name. Null when unavailable.</param>
public sealed record ProviderAccountInfo(string Id, string? Email, string? DisplayName);
