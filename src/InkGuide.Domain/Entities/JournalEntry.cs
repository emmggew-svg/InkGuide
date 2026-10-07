namespace InkGuide.Domain.Entities;

/// <summary>One app-initiated mutation in the activity journal. Feeds the activity view and undo.</summary>
public sealed class JournalEntry
{
    /// <summary>Client-generated operation id (also used for idempotent replay).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>When the mutation happened (UTC).</summary>
    public System.DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Mutation category (for example <c>rename</c>, <c>move</c>, <c>tag.assign</c>).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Developer-oriented English description of what changed.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>True when the entry carries enough before/after state to be undone.</summary>
    public bool IsUndoable { get; set; }

    /// <summary>JSON payload needed to undo. Null for irreversible operations.</summary>
    public string? UndoPayloadJson { get; set; }
}
