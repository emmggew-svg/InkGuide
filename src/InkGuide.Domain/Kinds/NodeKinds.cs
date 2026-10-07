using System;

namespace InkGuide.Domain.Kinds;

/// <summary>
/// Stable kind taxonomy (MASTER_PROMPT Appendix A). Ids are extended, never repurposed.
/// Canonical home of the taxonomy; identification logic lives in InkGuide.Analysis.
/// </summary>
public static class NodeKinds
{
    /// <summary>Source code file.</summary>
    public const string SourceCode = "source-code";

    /// <summary>HTML web page.</summary>
    public const string WebPage = "web-page";

    /// <summary>Stylesheet.</summary>
    public const string Stylesheet = "stylesheet";

    /// <summary>Browser-executed script.</summary>
    public const string ClientScript = "client-script";

    /// <summary>Other markup (XML, SVG-as-text handled separately).</summary>
    public const string Markup = "markup";

    /// <summary>Configuration file.</summary>
    public const string Config = "config";

    /// <summary>Structured data (JSON, YAML, TOML).</summary>
    public const string DataStructured = "data-structured";

    /// <summary>Tabular data (CSV, TSV).</summary>
    public const string DataTabular = "data-tabular";

    /// <summary>Database file.</summary>
    public const string Database = "database";

    /// <summary>Notebook (for example Jupyter).</summary>
    public const string Notebook = "notebook";

    /// <summary>Plain-text note or document.</summary>
    public const string DocumentText = "document-text";

    /// <summary>Rich document (Word, ODF).</summary>
    public const string DocumentRich = "document-rich";

    /// <summary>Spreadsheet.</summary>
    public const string Spreadsheet = "spreadsheet";

    /// <summary>Presentation.</summary>
    public const string Presentation = "presentation";

    /// <summary>E-book.</summary>
    public const string Ebook = "ebook";

    /// <summary>PDF file.</summary>
    public const string Pdf = "pdf";

    /// <summary>Raster image.</summary>
    public const string ImageRaster = "image-raster";

    /// <summary>Vector image.</summary>
    public const string ImageVector = "image-vector";

    /// <summary>Editable design source (PSD, Figma export, AI).</summary>
    public const string DesignSource = "design-source";

    /// <summary>Audio file.</summary>
    public const string Audio = "audio";

    /// <summary>Video file.</summary>
    public const string Video = "video";

    /// <summary>Font file.</summary>
    public const string Font = "font";

    /// <summary>Archive file.</summary>
    public const string Archive = "archive";

    /// <summary>Executable program or script.</summary>
    public const string Executable = "executable";

    /// <summary>Binary library.</summary>
    public const string LibraryBinary = "library-binary";

    /// <summary>Installer package.</summary>
    public const string Installer = "installer";

    /// <summary>Disk image.</summary>
    public const string DiskImage = "disk-image";

    /// <summary>3D model.</summary>
    public const string Model3d = "model-3d";

    /// <summary>Generic game asset.</summary>
    public const string GameAsset = "game-asset";

    /// <summary>Minecraft data or asset file.</summary>
    public const string MinecraftAsset = "minecraft-asset";

    /// <summary>Log file.</summary>
    public const string Log = "log";

    /// <summary>Certificate or private key material (always Sensitive).</summary>
    public const string CertificateOrKey = "certificate-or-key";

    /// <summary>Shortcut or link node; never followed implicitly.</summary>
    public const string Shortcut = "shortcut";

    /// <summary>Google Docs file (exportable).</summary>
    public const string GoogleDoc = "google-doc";

    /// <summary>Google Sheets file (exportable).</summary>
    public const string GoogleSheet = "google-sheet";

    /// <summary>Google Slides file (exportable).</summary>
    public const string GoogleSlides = "google-slides";

    /// <summary>Other Google-native file.</summary>
    public const string GoogleOther = "google-other";

    /// <summary>Folder.</summary>
    public const string Folder = "folder";

    /// <summary>Fallback when nothing identified the file.</summary>
    public const string Unknown = "unknown";

    /// <summary>All stable kind ids.</summary>
    public static readonly string[] All =
    [
        SourceCode, WebPage, Stylesheet, ClientScript, Markup, Config,
        DataStructured, DataTabular, Database, Notebook, DocumentText,
        DocumentRich, Spreadsheet, Presentation, Ebook, Pdf,
        ImageRaster, ImageVector, DesignSource, Audio, Video, Font,
        Archive, Executable, LibraryBinary, Installer, DiskImage,
        Model3d, GameAsset, MinecraftAsset, Log, CertificateOrKey,
        Shortcut, GoogleDoc, GoogleSheet, GoogleSlides, GoogleOther,
        Folder, Unknown,
    ];

    /// <summary>Checks whether a kind id belongs to the taxonomy.</summary>
    public static bool IsKnown(string? kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return false;
        }

        return Array.IndexOf(All, kind) >= 0;
    }
}
