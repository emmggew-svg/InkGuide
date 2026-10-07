namespace InkGuide.Domain.Entities;

/// <summary>A label attachable to nodes, projects and repositories.</summary>
public sealed class Tag
{
    /// <summary>Opaque tag id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Owning workspace id. Tag names are unique per workspace (case-insensitive).</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Tag name, optionally namespaced (for example <c>type:web-site</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Namespace prefix (for example <c>type</c>). Null for plain tags.</summary>
    public string? Namespace { get; set; }

    /// <summary>Human-readable description of what the tag means.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the tag is system-applied or user-created.</summary>
    public TagOrigin Origin { get; set; } = TagOrigin.User;

    /// <summary>Nullable design-token key for future styling. The back-end never chooses colors.</summary>
    public string? ColorToken { get; set; }

    /// <summary>True for built-in tags that cannot be deleted.</summary>
    public bool IsSystem { get; set; }
}
