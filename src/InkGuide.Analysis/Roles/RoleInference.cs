using System;
using System.Collections.Generic;
using System.Globalization;
using InkGuide.Analysis.Catalogs;
using InkGuide.Analysis.Inspection;

namespace InkGuide.Analysis.Roles;

/// <summary>One role inference outcome.</summary>
/// <param name="RoleId">Matched role id. Null when no rule matched.</param>
/// <param name="Confidence">Match confidence in 0-1.</param>
/// <param name="IsSensitive">True when the matched rule marks matches Sensitive.</param>
/// <param name="IsGenerated">True when the matched rule marks matches generated.</param>
/// <param name="Summary">Summary template filled from inspected facts. Empty when no rule matched.</param>
public sealed record RoleMatch(string? RoleId, double Confidence, bool IsSensitive, bool IsGenerated, string Summary);

/// <summary>
/// Data-driven role inference over the file-role catalog: glob patterns plus
/// optional content predicates, highest priority wins. Summaries come from the
/// catalog templates with {slots} filled from inspected facts.
/// </summary>
public sealed class RoleInference
{
    private readonly FileRoleCatalog _catalog;

    /// <summary>Creates role inference over a catalog.</summary>
    public RoleInference(FileRoleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Infers the role of one file. Never throws for malformed input.</summary>
    /// <param name="fileName">File name.</param>
    /// <param name="relativePath">Repo-relative path. Null when unknown.</param>
    /// <param name="text">Text analysis. Null when the content was not read.</param>
    /// <param name="peek">Structured peek. Null when the content was not read.</param>
    /// <param name="textPreview">Decoded text preview for content predicates. Null when unavailable or Sensitive.</param>
    public RoleMatch InferRole(
        string fileName,
        string? relativePath,
        TextAnalysis? text,
        StructuredPeek? peek,
        string? textPreview)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        RoleRule? best = null;
        foreach (RoleRule rule in _catalog.Rules)
        {
            if (!MatchesPatterns(rule, fileName, relativePath))
            {
                continue;
            }

            if (!MatchesContent(rule, textPreview))
            {
                continue;
            }

            if (best is null || rule.Priority > best.Priority)
            {
                best = rule;
            }
        }

        if (best is null)
        {
            return new RoleMatch(null, 0.2, false, false, string.Empty);
        }

        double confidence = best.Priority >= 90 ? 0.95 : best.Priority >= 50 ? 0.8 : 0.65;
        if (best.ContentContains.Count > 0)
        {
            confidence = Math.Min(0.98, confidence + 0.05);
        }

        Dictionary<string, string> slots = BuildSlots(fileName, text, peek);
        string summary = FillTemplate(best.SummaryTemplate, slots);
        return new RoleMatch(best.Id, confidence, best.IsSensitive, best.IsGenerated, summary);
    }

    /// <summary>Fills {slots} in a catalog template. Unknown slots become empty strings.</summary>
    public static string FillTemplate(string template, IReadOnlyDictionary<string, string> slots)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(slots);
        if (template.Length == 0 || slots.Count == 0)
        {
            return template;
        }

        string result = template;
        foreach (KeyValuePair<string, string> slot in slots)
        {
            result = result.Replace("{" + slot.Key + "}", slot.Value, StringComparison.Ordinal);
        }

        return result;
    }

    private static bool MatchesPatterns(RoleRule rule, string fileName, string? relativePath)
    {
        foreach (string pattern in rule.Patterns)
        {
            if (GlobMatcher.MatchFileOrPath(pattern, fileName, relativePath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesContent(RoleRule rule, string? textPreview)
    {
        if (rule.ContentContains.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrEmpty(textPreview))
        {
            return false;
        }

        foreach (string required in rule.ContentContains)
        {
            if (!textPreview.Contains(required, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, string> BuildSlots(string fileName, TextAnalysis? text, StructuredPeek? peek)
    {
        string title = peek?.MarkdownH1 ?? peek?.HtmlTitle ?? peek?.PackageJson?.Name ?? string.Empty;
        string lines = text is null ? string.Empty : text.LineCount.ToString(CultureInfo.InvariantCulture);
        Dictionary<string, string> slots = new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = fileName,
            ["title"] = title,
            ["lines"] = lines,
            ["project"] = string.Empty,
            ["language"] = string.Empty,
        };

        if (peek?.PackageJson is not null)
        {
            if (!string.IsNullOrWhiteSpace(peek.PackageJson.Name))
            {
                slots["project"] = peek.PackageJson.Name.Trim();
            }
        }

        return slots;
    }
}
