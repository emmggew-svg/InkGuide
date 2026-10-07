namespace InkGuide.Domain.Entities;

/// <summary>A commit-like, named, point-in-time record of a repository or project scope.</summary>
public sealed class Snapshot
{
    /// <summary>Opaque snapshot id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Repository the snapshot was taken from.</summary>
    public string RepositoryId { get; set; } = string.Empty;

    /// <summary>User-supplied message describing the snapshot.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>How content was preserved: revision pins or server-side copies.</summary>
    public SnapshotMode Mode { get; set; } = SnapshotMode.Pin;

    /// <summary>When the snapshot was created (UTC).</summary>
    public System.DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Number of entries recorded in the snapshot.</summary>
    public int EntryCount { get; set; }
}
