namespace InkGuide.Domain.Entities;

/// <summary>A user-pinned favorite node.</summary>
public sealed class Favorite
{
    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Pinned node id.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>When the node was pinned (UTC).</summary>
    public System.DateTimeOffset AddedAtUtc { get; set; }
}
