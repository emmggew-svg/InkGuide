namespace InkGuide.Domain.Entities;

/// <summary>A queued, resumable upload or download with progress.</summary>
public sealed class TransferJob
{
    /// <summary>Opaque transfer id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Transfer direction.</summary>
    public TransferDirection Direction { get; set; } = TransferDirection.Upload;

    /// <summary>Current lifecycle state.</summary>
    public TransferState State { get; set; } = TransferState.Queued;

    /// <summary>Remote node id. Null for uploads of not-yet-created nodes.</summary>
    public string? NodeId { get; set; }

    /// <summary>Local file path. Null when not applicable.</summary>
    public string? LocalPath { get; set; }

    /// <summary>Remote parent id for uploads. Null for downloads.</summary>
    public string? RemoteParentId { get; set; }

    /// <summary>Total bytes. Null when unknown up front.</summary>
    public long? BytesTotal { get; set; }

    /// <summary>Bytes transferred so far.</summary>
    public long BytesTransferred { get; set; }

    /// <summary>When the job was created (UTC).</summary>
    public System.DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last failure in developer-oriented English. Null when never failed.</summary>
    public string? LastError { get; set; }
}
