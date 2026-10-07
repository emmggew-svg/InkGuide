namespace InkGuide.Domain.Entities;

/// <summary>The auto-generated understanding of a node: kind, role, summary, confidence and sensitivity.</summary>
public sealed class NodeInsight
{
    /// <summary>Analysed node id.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Stable kind id from the Appendix A taxonomy.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Role id from the file-role catalog (for example <c>readme</c>). Null when unknown.</summary>
    public string? Role { get; set; }

    /// <summary>Plain-language summary, 1-3 sentences. Generic wording for Sensitive nodes.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Confidence score in the range 0-1, derived from the strength and number of signals.</summary>
    public double Confidence { get; set; }

    /// <summary>Provenance of the summary. <c>User</c> wins over <c>Ai</c> wins over <c>Rule</c>.</summary>
    public InsightSource Source { get; set; } = InsightSource.Rule;

    /// <summary>Sensitivity classification.</summary>
    public SensitivityLevel Sensitivity { get; set; } = SensitivityLevel.None;

    /// <summary>Short hash of the analysed content bytes. Null for folders.</summary>
    public string? ContentHash { get; set; }

    /// <summary>When the node was analysed (UTC).</summary>
    public System.DateTimeOffset AnalyzedAtUtc { get; set; }

    /// <summary>Ruleset version that produced this insight; drives lazy re-analysis on upgrades.</summary>
    public string RulesetVersion { get; set; } = string.Empty;
}
