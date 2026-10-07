using System.Collections.Generic;

namespace InkGuide.Analysis.Engine;

/// <summary>Input for one node analysis. Content is capped upstream (MAX_ANALYSIS_READ_BYTES).</summary>
/// <param name="AccountId">Owning account id.</param>
/// <param name="NodeId">Node being analysed.</param>
/// <param name="FileName">File or folder name.</param>
/// <param name="RelativePath">Repo-relative path. Null when unknown.</param>
/// <param name="MimeType">Provider MIME type. Null when unknown.</param>
/// <param name="IsFolder">True for folder insights.</param>
/// <param name="ChildCount">Direct child count for folders.</param>
/// <param name="ProjectName">Detected project name for context. Null outside projects.</param>
/// <param name="SiblingNames">Sibling names for AI context downstream. Unused by the rule engine.</param>
/// <param name="HeaderBytes">Content header for magic sniffing (first 8 KB used). Null when unavailable.</param>
/// <param name="ContentBytes">Capped content bytes for inspection. Null when unavailable.</param>
public sealed record AnalysisInput(
    string AccountId,
    string NodeId,
    string FileName,
    string? RelativePath,
    string? MimeType,
    bool IsFolder,
    int ChildCount,
    string? ProjectName,
    IReadOnlyList<string>? SiblingNames,
    byte[]? HeaderBytes,
    byte[]? ContentBytes);
