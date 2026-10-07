using System;

namespace InkGuide.Infrastructure.Persistence.Entities;

/// <summary>Tag definition (unique name per workspace/account, case-insensitive).</summary>
public sealed class TagEntity
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Namespace { get; set; }

    public string? Description { get; set; }

    public string Origin { get; set; } = "user";

    public string? ColorToken { get; set; }

    public bool IsSystem { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Tag assignment to a node.</summary>
public sealed class NodeTagEntity
{
    public string TagId { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public DateTimeOffset AssignedAtUtc { get; set; }
}

/// <summary>Suppression of an auto tag the owner removed (never re-applied).</summary>
public sealed class TagSuppressionEntity
{
    public int Id { get; set; }

    public string AccountId { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public string? NodeId { get; set; }

    public DateTimeOffset SuppressedAtUtc { get; set; }
}
