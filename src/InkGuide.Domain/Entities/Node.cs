namespace InkGuide.Domain.Entities;

/// <summary>Cached view of a Drive item: a file or a folder.</summary>
public sealed class Node
{
    /// <summary>Provider id of the item.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Provider id of the parent. Null for the workspace root.</summary>
    public string? ParentId { get; set; }

    /// <summary>Item name (last path segment).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>True for folders.</summary>
    public bool IsFolder { get; set; }

    /// <summary>Provider MIME type. Null when unknown.</summary>
    public string? MimeType { get; set; }

    /// <summary>Content size in bytes. Zero for folders.</summary>
    public long SizeBytes { get; set; }

    /// <summary>MD5 checksum from the provider. Null for folders and Google-native files.</summary>
    public string? Md5Checksum { get; set; }

    /// <summary>Provider version token used for conflict-safe saves.</summary>
    public string? Version { get; set; }

    /// <summary>Head revision id, when the provider exposes revisions.</summary>
    public string? HeadRevisionId { get; set; }

    /// <summary>Last modification time (UTC).</summary>
    public System.DateTimeOffset ModifiedTimeUtc { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public System.DateTimeOffset CreatedTimeUtc { get; set; }

    /// <summary>True when trashed (but not permanently deleted).</summary>
    public bool Trashed { get; set; }

    /// <summary>Containing repository id. Null for the inbox and workspace roots.</summary>
    public string? RepositoryId { get; set; }

    /// <summary>Containing project id. Null when not inside a project.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Human-readable '/'-separated path used for display and search.</summary>
    public string PathText { get; set; } = string.Empty;

    /// <summary>Durable human summary mirrored to the Drive description field. Null when not yet analysed.</summary>
    public string? Description { get; set; }

    /// <summary>Provider web link for opening Google-native files in the system browser.</summary>
    public string? WebViewLink { get; set; }
}
