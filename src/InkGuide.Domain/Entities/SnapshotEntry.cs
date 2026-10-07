namespace InkGuide.Domain.Entities;

/// <summary>One file or folder captured inside a snapshot.</summary>
public sealed class SnapshotEntry
{
    /// <summary>Owning snapshot id.</summary>
    public string SnapshotId { get; set; } = string.Empty;

    /// <summary>Provider id of the captured node.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Repo-relative path at capture time.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Content size in bytes. Null for folders.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>MD5 checksum at capture time. Null when unavailable.</summary>
    public string? Md5Checksum { get; set; }

    /// <summary>Head revision id at capture time (pin mode). Null when unavailable.</summary>
    public string? HeadRevisionId { get; set; }

    /// <summary>True for folder entries.</summary>
    public bool IsFolder { get; set; }
}
