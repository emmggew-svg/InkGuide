using System;
using System.Collections.Generic;
using InkGuide.Domain.Entities;
using InkGuide.Domain.ValueObjects;

namespace InkGuide.Application.Tags;

/// <summary>One tag assignment at a given tree level for effective-tag resolution.</summary>
/// <param name="Tag">Assigned tag.</param>
/// <param name="Inherited">True when inherited from an ancestor folder or project rather than assigned directly.</param>
public sealed record TagAssignment(Tag Tag, bool Inherited);

/// <summary>
/// Pure tag policy: inheritance plus suppression of unwanted auto-tags.
/// A tag on a folder or project is queryable on descendants; a suppression
/// recorded when the owner removes an auto tag stops it from being re-applied.
/// </summary>
public static class TagPolicy
{
    /// <summary>
    /// Resolves the effective tags for a node from assignments ordered leaf-first.
    /// Suppressed tags are dropped; duplicates (case-insensitive) keep the first occurrence.
    /// </summary>
    /// <param name="assignmentsInLeafFirstOrder">Direct assignment first, then ancestors outward.</param>
    /// <param name="suppressions">Workspace suppressions.</param>
    /// <param name="nodeId">Node being resolved. Null resolves workspace-level tags only.</param>
    public static IReadOnlyList<Tag> ResolveEffectiveTags(
        IReadOnlyList<TagAssignment> assignmentsInLeafFirstOrder,
        IReadOnlyList<TagSuppression> suppressions,
        string? nodeId)
    {
        ArgumentNullException.ThrowIfNull(assignmentsInLeafFirstOrder);
        ArgumentNullException.ThrowIfNull(suppressions);

        List<Tag> effective = new(assignmentsInLeafFirstOrder.Count);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (TagAssignment assignment in assignmentsInLeafFirstOrder)
        {
            ArgumentNullException.ThrowIfNull(assignment);
            string normalized = TagNames.Normalize(assignment.Tag.Name);
            if (seen.Contains(normalized))
            {
                continue;
            }

            if (IsSuppressed(assignment.Tag.Name, nodeId, suppressions))
            {
                seen.Add(normalized);
                continue;
            }

            seen.Add(normalized);
            effective.Add(assignment.Tag);
        }

        return effective;
    }

    /// <summary>
    /// Checks whether a tag is suppressed for a node: either workspace-wide
    /// (no scope) or scoped to that exact node id. Comparison is case-insensitive.
    /// </summary>
    public static bool IsSuppressed(string tagName, string? nodeId, IReadOnlyList<TagSuppression> suppressions)
    {
        ArgumentNullException.ThrowIfNull(tagName);
        ArgumentNullException.ThrowIfNull(suppressions);

        string normalized = TagNames.Normalize(tagName);
        foreach (TagSuppression suppression in suppressions)
        {
            ArgumentNullException.ThrowIfNull(suppression);
            if (!string.Equals(suppression.NormalizedTagName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (suppression.NodeId is null || string.Equals(suppression.NodeId, nodeId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
