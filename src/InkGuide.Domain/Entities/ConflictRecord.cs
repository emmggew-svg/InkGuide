namespace InkGuide.Domain.Entities;

/// <summary>A content or structural conflict awaiting explicit resolution. Never resolved silently.</summary>
public sealed class ConflictRecord
{
    /// <summary>Opaque conflict id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Conflicting node id.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Node name at detection time.</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>Whether the conflict is about content or structure.</summary>
    public ConflictType ConflictType { get; set; } = ConflictType.Content;

    /// <summary>JSON snapshot of the local side (for example the conflict-copy node id). Null when none.</summary>
    public string? LocalSnapshotJson { get; set; }

    /// <summary>Remote version token observed at detection time. Null when unknown.</summary>
    public string? RemoteVersion { get; set; }

    /// <summary>When the conflict was recorded (UTC).</summary>
    public System.DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>When the conflict was resolved (UTC). Null while open.</summary>
    public System.DateTimeOffset? ResolvedAtUtc { get; set; }

    /// <summary>Resolution taken (<c>keepLocal</c>, <c>keepRemote</c>, <c>keepBoth</c>). Null while open.</summary>
    public string? Resolution { get; set; }
}
