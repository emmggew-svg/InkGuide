namespace InkGuide.Domain.Entities;

/// <summary>One node waiting for (re-)analysis in the prioritised analysis queue.</summary>
public sealed class AnalysisJob
{
    /// <summary>Opaque job id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Node to analyse.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Queue priority. Higher runs first (user-initiated above visible-in-UI above background).</summary>
    public int Priority { get; set; }

    /// <summary>Current lifecycle state.</summary>
    public AnalysisJobState State { get; set; } = AnalysisJobState.Queued;

    /// <summary>When the job was created (UTC).</summary>
    public System.DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>How many analysis attempts have been made.</summary>
    public int Attempts { get; set; }
}
