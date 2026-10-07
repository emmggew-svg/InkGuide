namespace InkGuide.Domain.Entities;

/// <summary>The root Drive folder (<c>InkGuide Workspace</c>) of one account, holding repositories and the inbox.</summary>
public sealed class Workspace
{
    /// <summary>Opaque workspace id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Provider id of the workspace root folder.</summary>
    public string DriveFolderId { get; set; } = string.Empty;

    /// <summary>Display name of the workspace root folder.</summary>
    public string Name { get; set; } = string.Empty;
}
