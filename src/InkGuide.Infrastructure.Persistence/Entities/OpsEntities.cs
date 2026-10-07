using System;

namespace InkGuide.Infrastructure.Persistence.Entities;

/// <summary>Durable outbox entry: a local intent awaiting push to the provider.</summary>
public sealed class OutboxOperationEntity
{
    public string OpId { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string OperationType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public string State { get; set; } = "Pending";

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Activity journal entry (also backs undo).</summary>
public sealed class OperationJournalEntity
{
    public long Id { get; set; }

    public string AccountId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? NodeId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public bool Reversible { get; set; }

    public bool Undone { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>Record of a content or structural conflict needing explicit resolution.</summary>
public sealed class ConflictRecordEntity
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string Kind { get; set; } = "Content";

    public string? ConflictCopyNodeId { get; set; }

    public string? ConflictCopyPath { get; set; }

    public string? BaseVersion { get; set; }

    public string? RemoteVersion { get; set; }

    public string? LocalText { get; set; }

    public string? RemoteText { get; set; }

    public string? Resolution { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }
}

/// <summary> resumable transfer job (upload/download), restartable after crash.</summary>
public sealed class TransferJobEntity
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string Direction { get; set; } = "Upload";

    public string? NodeId { get; set; }

    public string? ParentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? LocalPath { get; set; }

    public long TotalBytes { get; set; }

    public long TransferredBytes { get; set; }

    public string State { get; set; } = "Queued";

    public string? SessionUri { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Queued analysis work item.</summary>
public sealed class AnalysisJobEntity
{
    public long Id { get; set; }

    public string AccountId { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string Priority { get; set; } = "Background";

    public string State { get; set; } = "Queued";

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>Persisted change-feed token per account.</summary>
public sealed class ChangeTokenEntity
{
    public string AccountId { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
