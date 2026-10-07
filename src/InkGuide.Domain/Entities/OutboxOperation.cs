namespace InkGuide.Domain.Entities;

/// <summary>Durable queue entry for a local intent awaiting push to the provider.</summary>
public sealed class OutboxOperation
{
    /// <summary>Client-generated operation id; replays are idempotent on this key.</summary>
    public string OpId { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Operation type (for example <c>node.rename</c>, <c>content.upload</c>).</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>JSON payload with the parameters needed to replay the operation.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Current lifecycle state.</summary>
    public OutboxState State { get; set; } = OutboxState.Queued;

    /// <summary>How many replay attempts have been made.</summary>
    public int Attempts { get; set; }

    /// <summary>Earliest time the next attempt may run (UTC, backoff). Null means immediately.</summary>
    public System.DateTimeOffset? NextAttemptAtUtc { get; set; }

    /// <summary>Last replay error in developer-oriented English. Null when never failed.</summary>
    public string? LastError { get; set; }

    /// <summary>When the operation was enqueued (UTC).</summary>
    public System.DateTimeOffset CreatedAtUtc { get; set; }
}
