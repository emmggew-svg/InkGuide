using System;

namespace InkGuide.Infrastructure.Persistence.Entities;

/// <summary>Cached view of a provider (Drive) item. Id is the provider id.</summary>
public sealed class NodeEntity
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    public string? MimeType { get; set; }

    public long Size { get; set; }

    public string? Md5 { get; set; }

    public string? Version { get; set; }

    public string? HeadRevisionId { get; set; }

    public DateTimeOffset ModifiedTimeUtc { get; set; }

    public bool Trashed { get; set; }

    public string? RepoId { get; set; }

    public string? ProjectId { get; set; }

    public string PathText { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? WebViewLink { get; set; }

    public DateTimeOffset SyncedAtUtc { get; set; }
}

/// <summary>Auto-generated understanding of a node.</summary>
public sealed class NodeInsightEntity
{
    public string NodeId { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string Kind { get; set; } = "unknown";

    public string? Role { get; set; }

    public string? Summary { get; set; }

    public double Confidence { get; set; }

    public string Source { get; set; } = "rule";

    public bool Sensitive { get; set; }

    public string? ContentHash { get; set; }

    public DateTimeOffset AnalyzedAtUtc { get; set; }

    public string RulesetVersion { get; set; } = string.Empty;
}
