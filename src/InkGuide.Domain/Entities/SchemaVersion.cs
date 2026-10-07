namespace InkGuide.Domain.Entities;

/// <summary>Local database schema version marker. On incompatible versions the cache is dropped and rebuilt from Drive.</summary>
public sealed class SchemaVersion
{
    /// <summary>Schema version number.</summary>
    public int Version { get; set; }

    /// <summary>When the schema was applied (UTC).</summary>
    public System.DateTimeOffset AppliedAtUtc { get; set; }
}
