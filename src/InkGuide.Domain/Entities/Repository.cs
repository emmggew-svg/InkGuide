namespace InkGuide.Domain.Entities;

/// <summary>A top-level folder in the workspace with a manifest. Kind is Single or Mono.</summary>
public sealed class Repository
{
    /// <summary>Opaque repository id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Containing workspace id.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Repository display name (Drive folder name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Structural kind: single-project or mono-repo.</summary>
    public RepositoryKind Kind { get; set; } = RepositoryKind.SingleProject;

    /// <summary>Provider id of the repository root folder.</summary>
    public string DriveFolderId { get; set; } = string.Empty;

    /// <summary>When the repository record was created (UTC).</summary>
    public System.DateTimeOffset CreatedAtUtc { get; set; }
}
