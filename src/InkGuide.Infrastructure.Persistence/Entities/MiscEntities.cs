using System;

namespace InkGuide.Infrastructure.Persistence.Entities;

/// <summary>Pinned/favorite node.</summary>
public sealed class FavoriteEntity
{
    public string NodeId { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public DateTimeOffset PinnedAtUtc { get; set; }
}

/// <summary>Commit-like snapshot record.</summary>
public sealed class SnapshotEntity
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string RepositoryId { get; set; } = string.Empty;

    public string? ProjectId { get; set; }

    public string Message { get; set; } = string.Empty;

    public string Mode { get; set; } = "Pin";

    public string ManifestJson { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>Single file entry inside a snapshot.</summary>
public sealed class SnapshotEntryEntity
{
    public long Id { get; set; }

    public string SnapshotId { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    public string? Md5 { get; set; }

    public string? HeadRevisionId { get; set; }

    public string? CopyNodeId { get; set; }
}

/// <summary>Simple key/value settings row.</summary>
public sealed class SettingsKvEntity
{
    public string Key { get; set; } = string.Empty;

    public string ValueJson { get; set; } = "null";

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Schema version marker row.</summary>
public sealed class SchemaVersionEntity
{
    public int Id { get; set; }

    public int Version { get; set; }

    public DateTimeOffset AppliedAtUtc { get; set; }
}
