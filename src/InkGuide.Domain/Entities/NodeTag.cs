namespace InkGuide.Domain.Entities;

/// <summary>Assignment of a tag to a node.</summary>
public sealed class NodeTag
{
    /// <summary>Assigned tag id.</summary>
    public string TagId { get; set; } = string.Empty;

    /// <summary>Node the tag is assigned to.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Origin of this assignment.</summary>
    public TagOrigin Origin { get; set; } = TagOrigin.User;

    /// <summary>When the tag was assigned (UTC).</summary>
    public System.DateTimeOffset AssignedAtUtc { get; set; }
}
