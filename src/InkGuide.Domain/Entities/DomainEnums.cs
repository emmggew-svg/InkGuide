namespace InkGuide.Domain.Entities;

/// <summary>Status of a connected Google account.</summary>
public enum AccountStatus
{
    /// <summary>Connected and usable.</summary>
    Active = 0,

    /// <summary>Stored grant was revoked or expired; the user must reconnect.</summary>
    ReauthRequired = 1,

    /// <summary>Disconnected by the user; cached data stays readable.</summary>
    Disconnected = 2,
}

/// <summary>Google Drive OAuth scope mode of an account.</summary>
public enum DriveScopeMode
{
    /// <summary>Least-privilege <c>drive.file</c> scope (publishable).</summary>
    AppFolderOnly = 0,

    /// <summary>Full <c>drive</c> scope (restricted; enables adopting existing folders).</summary>
    FullDrive = 1,
}

/// <summary>Structural kind of a repository.</summary>
public enum RepositoryKind
{
    /// <summary>A single project: the repo itself is the one project.</summary>
    SingleProject = 0,

    /// <summary>Multiple detected or declared projects inside one repo folder.</summary>
    MonoRepo = 1,
}

/// <summary>Provenance of a node insight summary.</summary>
public enum InsightSource
{
    /// <summary>Produced by the built-in rule engine.</summary>
    Rule = 0,

    /// <summary>Enriched by the optional AI describer.</summary>
    Ai = 1,

    /// <summary>Authored by the user; automatic writers must never overwrite it.</summary>
    User = 2,
}

/// <summary>Sensitivity classification of a node.</summary>
public enum SensitivityLevel
{
    /// <summary>Ordinary content; eligible for indexing, previews and AI.</summary>
    None = 0,

    /// <summary>Looks like a secret or credential; excluded from indexing, logs and AI.</summary>
    Sensitive = 1,
}

/// <summary>Origin of a tag definition or assignment.</summary>
public enum TagOrigin
{
    /// <summary>Applied automatically by detectors; may be suppressed by the user.</summary>
    Auto = 0,

    /// <summary>Created or assigned explicitly by the user.</summary>
    User = 1,
}

/// <summary>Lifecycle state of a durable outbox operation.</summary>
public enum OutboxState
{
    /// <summary>Waiting to be replayed against the provider.</summary>
    Queued = 0,

    /// <summary>Currently being replayed by the sync worker.</summary>
    InProgress = 1,

    /// <summary>Replay failed; scheduled for retry with backoff.</summary>
    Failed = 2,

    /// <summary>Replayed successfully and acknowledged.</summary>
    Done = 3,
}

/// <summary>How a snapshot preserves file content.</summary>
public enum SnapshotMode
{
    /// <summary>Record revisions and pin head revisions where supported (default).</summary>
    Pin = 0,

    /// <summary>Server-side copy of every file into the snapshot data folder.</summary>
    Copy = 1,
}

/// <summary>Direction of a transfer job.</summary>
public enum TransferDirection
{
    /// <summary>Local bytes moving to the provider.</summary>
    Upload = 0,

    /// <summary>Provider bytes moving to local disk.</summary>
    Download = 1,
}

/// <summary>Lifecycle state of a transfer job.</summary>
public enum TransferState
{
    /// <summary>Queued, not started.</summary>
    Queued = 0,

    /// <summary>Actively transferring.</summary>
    Running = 1,

    /// <summary>Paused by the user; resumable.</summary>
    Paused = 2,

    /// <summary>Completed successfully.</summary>
    Done = 3,

    /// <summary>Failed; retryable.</summary>
    Failed = 4,

    /// <summary>Cancelled by the user.</summary>
    Cancelled = 5,
}

/// <summary>Lifecycle state of an analysis job.</summary>
public enum AnalysisJobState
{
    /// <summary>Waiting in the analysis queue.</summary>
    Queued = 0,

    /// <summary>Currently being analysed.</summary>
    Running = 1,

    /// <summary>Analysed and persisted.</summary>
    Done = 2,

    /// <summary>Failed; retryable.</summary>
    Failed = 3,
}

/// <summary>Kind of a sync conflict that needs explicit resolution.</summary>
public enum ConflictType
{
    /// <summary>Remote content version moved while a local edit was pending.</summary>
    Content = 0,

    /// <summary>Node was moved, renamed or deleted remotely while edited locally.</summary>
    Structural = 1,
}
