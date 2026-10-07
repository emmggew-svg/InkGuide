using System;
using System.Collections.Generic;

namespace InkGuide.Analysis.Tags;

/// <summary>Auto-apply versus suggestion split for candidate tags.</summary>
/// <param name="AutoApply">Tags at or above the threshold and not suppressed.</param>
/// <param name="Suggest">Tags below the threshold and not suppressed.</param>
public sealed record TagDecision(IReadOnlyList<string> AutoApply, IReadOnlyList<string> Suggest);

/// <summary>
/// Splits detector-proposed tags into auto-apply and suggestions.
/// Suppressed tags (removed by the owner) are dropped and never re-applied here.
/// </summary>
public static class TagDecider
{
    /// <summary>Default confidence at or above which tags are auto-applied.</summary>
    public const double DefaultAutoApplyThreshold = 0.6;

    /// <summary>Decides auto-apply versus suggest for candidate tags with confidences.</summary>
    /// <param name="tagConfidences">Candidate tag name to confidence (0-1).</param>
    /// <param name="suppressedNames">Suppressed tag names (case-insensitive).</param>
    /// <param name="autoApplyThreshold">Confidence at or above which tags are auto-applied.</param>
    public static TagDecision Decide(
        IReadOnlyDictionary<string, double> tagConfidences,
        IReadOnlyList<string> suppressedNames,
        double autoApplyThreshold = DefaultAutoApplyThreshold)
    {
        ArgumentNullException.ThrowIfNull(tagConfidences);
        ArgumentNullException.ThrowIfNull(suppressedNames);
        ArgumentOutOfRangeException.ThrowIfLessThan(autoApplyThreshold, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(autoApplyThreshold, 1.0);

        HashSet<string> suppressed = new(suppressedNames, StringComparer.OrdinalIgnoreCase);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> autoApply = new();
        List<string> suggest = new();
        foreach (KeyValuePair<string, double> candidate in tagConfidences)
        {
            if (string.IsNullOrWhiteSpace(candidate.Key) || !seen.Add(candidate.Key))
            {
                continue;
            }

            if (suppressed.Contains(candidate.Key))
            {
                continue;
            }

            if (candidate.Value >= autoApplyThreshold)
            {
                autoApply.Add(candidate.Key);
            }
            else
            {
                suggest.Add(candidate.Key);
            }
        }

        autoApply.Sort(StringComparer.Ordinal);
        suggest.Sort(StringComparer.Ordinal);
        return new TagDecision(autoApply, suggest);
    }
}
