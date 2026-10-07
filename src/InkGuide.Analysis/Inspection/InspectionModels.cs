using System.Collections.Generic;

namespace InkGuide.Analysis.Inspection;

/// <summary>Text-ness, encoding and line-ending analysis of a content sample.</summary>
/// <param name="IsText">True when the sample looks like text.</param>
/// <param name="EncodingName">Detected encoding: utf-8, utf-16le, utf-16be, utf-32le, utf-32be or latin1. "binary" when not text.</param>
/// <param name="HasBom">True when a byte-order mark was found.</param>
/// <param name="EolStyle">lf, crlf, cr, mixed or none.</param>
/// <param name="LineCount">Number of lines.</param>
public sealed record TextAnalysis(bool IsText, string EncodingName, bool HasBom, string EolStyle, int LineCount);

/// <summary>package.json facts.</summary>
/// <param name="Name">Package name. Null when absent.</param>
/// <param name="Description">Package description. Null when absent.</param>
/// <param name="ScriptNames">npm script names.</param>
/// <param name="DependencyNames">Production, dev, peer and optional dependency names.</param>
public sealed record PackageJsonInfo(
    string? Name,
    string? Description,
    IReadOnlyList<string> ScriptNames,
    IReadOnlyList<string> DependencyNames);

/// <summary>.csproj facts.</summary>
/// <param name="Sdk">Sdk attribute. Null when absent.</param>
/// <param name="TargetFrameworks">Target framework monikers.</param>
/// <param name="UsesWpf">True when UseWPF is set.</param>
/// <param name="UsesWindowsForms">True when UseWindowsForms is set.</param>
/// <param name="PackageReferences">Package reference ids.</param>
public sealed record CsprojInfo(
    string? Sdk,
    IReadOnlyList<string> TargetFrameworks,
    bool UsesWpf,
    bool UsesWindowsForms,
    IReadOnlyList<string> PackageReferences);

/// <summary>Structured peek into text content. Only populated fields carry facts.</summary>
public sealed record StructuredPeek(
    IReadOnlyList<string>? JsonTopKeys = null,
    IReadOnlyList<string>? YamlTopKeys = null,
    IReadOnlyList<string>? TomlSections = null,
    string? XmlRoot = null,
    IReadOnlyList<string>? XmlChildNames = null,
    IReadOnlyList<string>? CsvHeaders = null,
    string? CsvDelimiter = null,
    PackageJsonInfo? PackageJson = null,
    CsprojInfo? Csproj = null,
    string? HtmlTitle = null,
    string? HtmlDescription = null,
    string? MarkdownH1 = null,
    string? DocExcerpt = null)
{
    /// <summary>True when at least one fact was extracted.</summary>
    public bool HasFacts =>
        (JsonTopKeys is not null && JsonTopKeys.Count > 0) ||
        (YamlTopKeys is not null && YamlTopKeys.Count > 0) ||
        (TomlSections is not null && TomlSections.Count > 0) ||
        XmlRoot is not null ||
        (XmlChildNames is not null && XmlChildNames.Count > 0) ||
        (CsvHeaders is not null && CsvHeaders.Count > 0) ||
        PackageJson is not null ||
        Csproj is not null ||
        HtmlTitle is not null ||
        HtmlDescription is not null ||
        MarkdownH1 is not null ||
        DocExcerpt is not null;
}

/// <summary>Image dimensions from a header reader (no imaging dependency).</summary>
/// <param name="Format">png, jpeg, gif, webp or bmp.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public sealed record ImageInfo(string Format, int Width, int Height);

/// <summary>Tolerant PDF metadata (title and page count); nulls when unparseable.</summary>
/// <param name="Title">Document title. Null when absent or unparseable.</param>
/// <param name="PageCount">Page count. Null when absent or unparseable.</param>
public sealed record PdfInfo(string? Title, int? PageCount);

/// <summary>Zip central-directory listing (no extraction).</summary>
/// <param name="EntryCount">Entry count from the end-of-central-directory record.</param>
/// <param name="TopLevelNames">Distinct top-level entry names.</param>
/// <param name="IsComplete">False when the directory was truncated or unreadable.</param>
public sealed record ZipListing(int EntryCount, IReadOnlyList<string> TopLevelNames, bool IsComplete);
