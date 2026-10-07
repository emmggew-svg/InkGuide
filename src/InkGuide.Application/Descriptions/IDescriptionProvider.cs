using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;

namespace InkGuide.Application.Descriptions;

/// <summary>Input for the optional AI describer. Sensitive nodes, binaries and images never reach this port.</summary>
public sealed class DescribeRequest
{
    /// <summary>Node file name.</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>Identified kind id.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Rule-inferred role id. Null when unknown.</summary>
    public string? Role { get; set; }

    /// <summary>Bounded, secret-stripped text excerpt (default cap 6 KB upstream).</summary>
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>Sibling file names for context.</summary>
    public IReadOnlyList<string> SiblingNames { get; set; } = System.Array.Empty<string>();

    /// <summary>Detected project context (type id and name). Null outside projects.</summary>
    public string? ProjectContext { get; set; }

    /// <summary>MIME type. Null when unknown.</summary>
    public string? MimeType { get; set; }
}

/// <summary>Validated AI describer reply. Written only to description/insight fields, never executed.</summary>
/// <param name="Summary">Improved plain-language summary.</param>
/// <param name="Role">Suggested role id. Null to keep the rule-inferred role.</param>
/// <param name="KindHint">Suggested kind id. Null to keep the identified kind.</param>
/// <param name="TagSuggestions">Suggested tag names.</param>
/// <param name="Confidence">Model-reported confidence in 0-1.</param>
public sealed record DescribeResult(
    string Summary,
    string? Role,
    string? KindHint,
    IReadOnlyList<string> TagSuggestions,
    double Confidence);

/// <summary>Optional AI file-description provider (Anthropic). Always opt-in, privacy-guarded upstream.</summary>
public interface IDescriptionProvider
{
    /// <summary>Describes a node from bounded metadata and a secret-stripped excerpt.</summary>
    Task<Result<DescribeResult, Failure>> DescribeAsync(DescribeRequest request, CancellationToken ct);
}
