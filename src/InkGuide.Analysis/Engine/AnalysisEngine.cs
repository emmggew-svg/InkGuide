using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Analysis.Catalogs;
using InkGuide.Analysis.Identification;
using InkGuide.Analysis.Inspection;
using InkGuide.Analysis.Roles;
using InkGuide.Analysis.Sensitivity;
using InkGuide.Domain;
using InkGuide.Domain.Common;
using InkGuide.Domain.Entities;
using InkGuide.Domain.Kinds;
using InkGuide.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace InkGuide.Analysis.Engine;

/// <summary>
/// Rule-based analysis pipeline: identify, inspect, infer role, summarize.
/// No AI is involved here; AI enrichment happens downstream via IDescriptionProvider
/// and never replaces user-authored text. Sensitive nodes get generic summaries.
/// </summary>
public sealed partial class AnalysisEngine
{
    /// <summary>Maximum summary length in characters.</summary>
    public const int MaxSummaryChars = 500;

    private readonly KindMapCatalog _kindMap;
    private readonly FileRoleCatalog _roles;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AnalysisEngine> _logger;

    /// <summary>Creates the engine over catalogs, a time provider and a logger.</summary>
    public AnalysisEngine(
        KindMapCatalog kindMap,
        FileRoleCatalog roles,
        TimeProvider timeProvider,
        ILogger<AnalysisEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(kindMap);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _kindMap = kindMap;
        _roles = roles;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Analyses one node into a persisted insight.</summary>
    public Task<Result<NodeInsight, Failure>> AnalyzeAsync(AnalysisInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(input.AccountId) ||
            string.IsNullOrWhiteSpace(input.NodeId) ||
            string.IsNullOrWhiteSpace(input.FileName))
        {
            return Task.FromResult(Result.Failure<NodeInsight>(Errors.InvalidParams("account, node and file name are required")));
        }

        NodeInsight insight = input.IsFolder ? AnalyzeFolder(input) : AnalyzeFile(input);
        LogAnalyzed(input.NodeId, insight.Kind);
        return Task.FromResult(Result.Success(insight));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Analyzed node {nodeId} as kind {kind}")]
    private partial void LogAnalyzed(string nodeId, string kind);

    private NodeInsight AnalyzeFolder(AnalysisInput input)
    {
        SensitivityFinding sensitivity = SensitivityDetector.Detect(input.FileName, null);
        string summary = input.ChildCount > 0
            ? $"Folder '{input.FileName}' with {input.ChildCount.ToString(CultureInfo.InvariantCulture)} items."
            : $"Folder '{input.FileName}'.";
        return new NodeInsight
        {
            NodeId = input.NodeId,
            AccountId = input.AccountId,
            Kind = NodeKinds.Folder,
            Role = null,
            Summary = summary,
            Confidence = 0.9,
            Source = InsightSource.Rule,
            Sensitivity = sensitivity.IsSensitive ? SensitivityLevel.Sensitive : SensitivityLevel.None,
            ContentHash = null,
            AnalyzedAtUtc = _timeProvider.GetUtcNow(),
            RulesetVersion = _roles.RulesetVersion,
        };
    }

    private NodeInsight AnalyzeFile(AnalysisInput input)
    {
        byte[]? content = input.ContentBytes;
        byte[]? header = TakeHeader(input.HeaderBytes ?? content);

        KindIdentification identification = new KindIdentifier(_kindMap).Identify(input.FileName, input.MimeType, header);

        bool nameSensitive = SensitivityDetector.Detect(input.FileName, null).IsSensitive;

        TextAnalysis? text = null;
        StructuredPeek? peek = null;
        string? preview = null;
        if (content is not null && content.Length > 0 && !nameSensitive)
        {
            text = ContentInspector.AnalyzeText(content);
            if (text.IsText)
            {
                preview = DecodePreview(content);
                peek = ContentInspector.PeekStructured(input.FileName, content, text, includeDocExcerpt: true);
            }
        }

        SensitivityFinding sensitivity = SensitivityDetector.Detect(input.FileName, preview);
        bool isSensitive = sensitivity.IsSensitive;
        if (isSensitive && !nameSensitive && content is not null && content.Length > 0 && text is not null && text.IsText)
        {
            peek = ContentInspector.PeekStructured(input.FileName, content, text, includeDocExcerpt: false);
        }

        RoleMatch role = new RoleInference(_roles).InferRole(
            input.FileName,
            input.RelativePath,
            text,
            peek,
            isSensitive ? null : preview);

        string kind = identification.Kind;
        double kindConfidence = identification.Confidence;
        if (SensitivityDetector.LooksLikeKeyMaterial(input.FileName))
        {
            kind = NodeKinds.CertificateOrKey;
            kindConfidence = Math.Max(kindConfidence, 0.85);
        }

        string summary = BuildSummary(input, kind, text, peek, role, isSensitive, content);
        double confidence = CombineConfidence(kindConfidence, role.Confidence, peek, input.ProjectName);
        string? contentHash = content is not null && content.Length > 0 ? ShortContentHash(content) : null;

        return new NodeInsight
        {
            NodeId = input.NodeId,
            AccountId = input.AccountId,
            Kind = kind,
            Role = role.RoleId,
            Summary = summary,
            Confidence = confidence,
            Source = InsightSource.Rule,
            Sensitivity = isSensitive ? SensitivityLevel.Sensitive : SensitivityLevel.None,
            ContentHash = contentHash,
            AnalyzedAtUtc = _timeProvider.GetUtcNow(),
            RulesetVersion = _roles.RulesetVersion,
        };
    }

    private static string BuildSummary(
        AnalysisInput input,
        string kind,
        TextAnalysis? text,
        StructuredPeek? peek,
        RoleMatch role,
        bool isSensitive,
        byte[]? content)
    {
        if (isSensitive)
        {
            if (!string.IsNullOrWhiteSpace(role.Summary))
            {
                return Truncate(role.Summary);
            }

            return Truncate($"Sensitive file '{input.FileName}'. Excluded from indexing and AI.");
        }

        if (!string.IsNullOrWhiteSpace(role.Summary))
        {
            string summary = role.Summary;
            if (input.ProjectName is not null &&
                !summary.Contains(input.ProjectName, StringComparison.OrdinalIgnoreCase))
            {
                summary += $" Part of the '{input.ProjectName}' project.";
            }

            return Truncate(summary);
        }

        StringBuilder builder = new();
        builder.Append(kind);
        builder.Append(" file '");
        builder.Append(input.FileName);
        builder.Append('\'');
        if (text is not null && text.IsText && text.LineCount > 0)
        {
            builder.Append(" with ~");
            builder.Append(text.LineCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(" lines");
        }

        if (string.Equals(kind, NodeKinds.ImageRaster, StringComparison.Ordinal) && content is not null)
        {
            ImageInfo? image = ContentInspector.ReadImageInfo(content);
            if (image is not null)
            {
                builder.Append(CultureInfo.InvariantCulture, $" ({image.Width.ToString(CultureInfo.InvariantCulture)}x{image.Height.ToString(CultureInfo.InvariantCulture)})");
            }
        }

        if (input.ProjectName is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $". Part of the '{input.ProjectName}' project");
        }

        builder.Append('.');
        return Truncate(builder.ToString());
    }

    private static double CombineConfidence(double kindConfidence, double roleConfidence, StructuredPeek? peek, string? projectName)
    {
        double combined = 1.0 - ((1.0 - kindConfidence) * (1.0 - roleConfidence));
        if (peek?.HasFacts == true)
        {
            combined += 0.03;
        }

        if (projectName is not null)
        {
            combined += 0.02;
        }

        return Confidence.Clamp(combined).Value;
    }

    private static byte[]? TakeHeader(byte[]? source)
    {
        if (source is null)
        {
            return null;
        }

        if (source.Length <= KindIdentifier.MaxHeaderBytes)
        {
            return source;
        }

        byte[] header = new byte[KindIdentifier.MaxHeaderBytes];
        Buffer.BlockCopy(source, 0, header, 0, KindIdentifier.MaxHeaderBytes);
        return header;
    }

    private static string DecodePreview(byte[] content)
    {
        int length = Math.Min(content.Length, SensitivityDetector.MaxPreviewChars);
        return Encoding.Latin1.GetString(content, 0, length);
    }

    private static string ShortContentHash(byte[] content)
    {
        byte[] hash = SHA256.HashData(content);
        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }

    private static string Truncate(string summary)
    {
        return summary.Length > MaxSummaryChars ? summary.Substring(0, MaxSummaryChars) : summary;
    }
}
