namespace InkGuide.Domain.Entities;

/// <summary>
/// Records that the owner removed an auto-applied tag so detectors do not re-apply it.
/// A null <see cref="NodeId"/> means workspace-wide suppression.
/// </summary>
public sealed class TagSuppression
{
    /// <summary>Owning workspace id.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Suppressed tag name in normalised (upper-invariant, trimmed) form.</summary>
    public string NormalizedTagName { get; set; } = string.Empty;

    /// <summary>Suppression scope. Null suppresses the tag everywhere in the workspace.</summary>
    public string? NodeId { get; set; }

    /// <summary>When the suppression was recorded (UTC).</summary>
    public System.DateTimeOffset SuppressedAtUtc { get; set; }
}
