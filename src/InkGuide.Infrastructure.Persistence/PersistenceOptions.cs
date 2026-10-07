namespace InkGuide.Infrastructure.Persistence;

/// <summary>Options for the SQLite persistence layer.</summary>
public sealed class PersistenceOptions
{
    /// <summary>Full path to the SQLite database file.</summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>Current schema version. On mismatch the cache is dropped and rebuilt from Drive.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Forces the LIKE search fallback even when FTS5 is available (tests).</summary>
    public bool ForceLikeFallback { get; set; }

    /// <summary>Max indexed text bytes per file for FTS content.</summary>
    public int MaxIndexedContentBytes { get; set; } = 65536;

    /// <summary>Write batch size for bulk index operations.</summary>
    public int WriteBatchSize { get; set; } = 200;
}
