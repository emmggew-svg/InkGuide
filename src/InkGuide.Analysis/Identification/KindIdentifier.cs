using System;
using System.Collections.Generic;
using InkGuide.Analysis.Catalogs;
using InkGuide.Domain.Kinds;

namespace InkGuide.Analysis.Identification;

/// <summary>
/// Identifies a file kind from extension, MIME type and magic-byte sniffing
/// (first 8 KB). Magic wins over extension; zip-family extensions refine
/// generic zip magic (docx, epub, apk, ...). Google-native MIME types map
/// to google-doc/sheet/slides/other.
/// </summary>
public sealed class KindIdentifier
{
    /// <summary>Maximum header bytes sniffed for magic signatures.</summary>
    public const int MaxHeaderBytes = 8192;

    /// <summary>Drive folder MIME type.</summary>
    public const string FolderMimeType = "application/vnd.google-apps.folder";

    /// <summary>Google-native MIME prefix.</summary>
    public const string GoogleAppsPrefix = "application/vnd.google-apps.";

    private readonly KindMapCatalog _kindMap;

    private static readonly (int Offset, byte[] Pattern, string Kind)[] MagicTable =
    [
        (0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], NodeKinds.ImageRaster),
        (0, [0xFF, 0xD8, 0xFF], NodeKinds.ImageRaster),
        (0, "GIF87a"u8.ToArray(), NodeKinds.ImageRaster),
        (0, "GIF89a"u8.ToArray(), NodeKinds.ImageRaster),
        (0, [0x42, 0x4D], NodeKinds.ImageRaster),
        (0, [0x49, 0x49, 0x2A, 0x00], NodeKinds.ImageRaster),
        (0, [0x4D, 0x4D, 0x00, 0x2A], NodeKinds.ImageRaster),
        (0, [0x00, 0x00, 0x01, 0x00], NodeKinds.ImageRaster),
        (0, "%PDF"u8.ToArray(), NodeKinds.Pdf),
        (0, "PK\x03\x04"u8.ToArray(), NodeKinds.Archive),
        (0, "PK\x05\x06"u8.ToArray(), NodeKinds.Archive),
        (0, "PK\x07\x08"u8.ToArray(), NodeKinds.Archive),
        (0, [0x1F, 0x8B], NodeKinds.Archive),
        (0, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], NodeKinds.Archive),
        (0, "Rar!"u8.ToArray(), NodeKinds.Archive),
        (0, "SQLite format 3\0"u8.ToArray(), NodeKinds.Database),
        (0, "ID3"u8.ToArray(), NodeKinds.Audio),
        (0, "OggS"u8.ToArray(), NodeKinds.Audio),
        (0, "fLaC"u8.ToArray(), NodeKinds.Audio),
        (4, "ftyp"u8.ToArray(), NodeKinds.Video),
        (0, [0x1A, 0x45, 0xDF, 0xA3], NodeKinds.Video),
        (0, "MZ"u8.ToArray(), NodeKinds.Executable),
        (0, [0x7F, 0x45, 0x4C, 0x46], NodeKinds.Executable),
        (0, [0xFE, 0xED, 0xFA], NodeKinds.Executable),
        (0, [0xCA, 0xFE, 0xBA, 0xBE], NodeKinds.Executable),
        (0, [0x00, 0x61, 0x73, 0x6D], NodeKinds.Executable),
        (0, "8BPS"u8.ToArray(), NodeKinds.DesignSource),
        (0, [0x00, 0x01, 0x00, 0x00], NodeKinds.Font),
        (0, "OTTO"u8.ToArray(), NodeKinds.Font),
        (0, "wOFF"u8.ToArray(), NodeKinds.Font),
        (0, "wOF2"u8.ToArray(), NodeKinds.Font),
        (0, "MSCF"u8.ToArray(), NodeKinds.Installer),
    ];

    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();

    private static readonly byte[] WebpSignature = "WEBP"u8.ToArray();

    private static readonly byte[] WaveSignature = "WAVE"u8.ToArray();

    private static readonly byte[] AviSignature = "AVI "u8.ToArray();

    private static readonly Dictionary<string, string> ZipFamilyKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["docx"] = NodeKinds.DocumentRich,
        ["docm"] = NodeKinds.DocumentRich,
        ["xlsx"] = NodeKinds.Spreadsheet,
        ["xlsm"] = NodeKinds.Spreadsheet,
        ["pptx"] = NodeKinds.Presentation,
        ["pptm"] = NodeKinds.Presentation,
        ["odt"] = NodeKinds.DocumentRich,
        ["ods"] = NodeKinds.Spreadsheet,
        ["odp"] = NodeKinds.Presentation,
        ["odg"] = NodeKinds.DesignSource,
        ["epub"] = NodeKinds.Ebook,
        ["cbz"] = NodeKinds.Ebook,
        ["apk"] = NodeKinds.Installer,
        ["aab"] = NodeKinds.Installer,
        ["ipa"] = NodeKinds.Installer,
        ["jar"] = NodeKinds.LibraryBinary,
    };

    private static readonly Dictionary<string, string> MimePrefixKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = NodeKinds.Pdf,
        ["application/json"] = NodeKinds.DataStructured,
        ["application/xml"] = NodeKinds.Markup,
        ["application/javascript"] = NodeKinds.ClientScript,
        ["application/zip"] = NodeKinds.Archive,
        ["application/x-zip-compressed"] = NodeKinds.Archive,
        ["application/gzip"] = NodeKinds.Archive,
        ["application/x-tar"] = NodeKinds.Archive,
        ["application/x-7z-compressed"] = NodeKinds.Archive,
        ["application/x-rar-compressed"] = NodeKinds.Archive,
        ["application/x-sqlite3"] = NodeKinds.Database,
        ["application/font-woff"] = NodeKinds.Font,
        ["application/font-sfnt"] = NodeKinds.Font,
        ["font/"] = NodeKinds.Font,
        ["text/csv"] = NodeKinds.DataTabular,
        ["text/tab-separated-values"] = NodeKinds.DataTabular,
        ["text/markdown"] = NodeKinds.DocumentText,
        ["text/html"] = NodeKinds.WebPage,
        ["text/css"] = NodeKinds.Stylesheet,
        ["text/xml"] = NodeKinds.Markup,
        ["text/"] = NodeKinds.DocumentText,
        ["image/svg+xml"] = NodeKinds.ImageVector,
        ["image/"] = NodeKinds.ImageRaster,
        ["audio/"] = NodeKinds.Audio,
        ["video/"] = NodeKinds.Video,
    };

    /// <summary>Creates an identifier over a kind map.</summary>
    public KindIdentifier(KindMapCatalog kindMap)
    {
        ArgumentNullException.ThrowIfNull(kindMap);
        _kindMap = kindMap;
    }

    /// <summary>Identifies the kind of one file. Never throws for malformed input.</summary>
    /// <param name="fileName">File name (extension is read from here).</param>
    /// <param name="mimeType">Provider MIME type. Null when unknown.</param>
    /// <param name="headerBytes">Content header (only the first 8 KB are used). Null when unavailable.</param>
    public KindIdentification Identify(string fileName, string? mimeType, byte[]? headerBytes)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (string.Equals(mimeType, FolderMimeType, StringComparison.OrdinalIgnoreCase))
        {
            return new KindIdentification(NodeKinds.Folder, 1.0, KindIdentificationSource.Folder);
        }

        if (!string.IsNullOrWhiteSpace(mimeType) &&
            mimeType.StartsWith(GoogleAppsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string? googleKind = _kindMap.GetKindByMime(mimeType);
            return new KindIdentification(googleKind ?? NodeKinds.GoogleOther, 0.95, KindIdentificationSource.GoogleNative);
        }

        byte[] header = headerBytes ?? Array.Empty<byte>();
        int headerLength = Math.Min(header.Length, MaxHeaderBytes);
        string? magicKind = IdentifyByMagic(header, headerLength);
        if (string.Equals(magicKind, NodeKinds.Archive, StringComparison.Ordinal))
        {
            string? zipKind = IdentifyZipFamily(fileName);
            if (zipKind is not null)
            {
                return new KindIdentification(zipKind, 0.85, KindIdentificationSource.MagicBytes);
            }
        }

        if (magicKind is not null)
        {
            return new KindIdentification(magicKind, 0.9, KindIdentificationSource.MagicBytes);
        }

        string? extension = GetExtension(fileName);
        string? extensionKind = _kindMap.GetKindByExtension(extension);
        if (extensionKind is not null)
        {
            return new KindIdentification(extensionKind, 0.7, KindIdentificationSource.Extension);
        }

        string? mimeKind = IdentifyByMime(mimeType);
        if (mimeKind is not null)
        {
            return new KindIdentification(mimeKind, 0.5, KindIdentificationSource.Mime);
        }

        return new KindIdentification(NodeKinds.Unknown, 0.1, KindIdentificationSource.Fallback);
    }

    private static string? IdentifyByMagic(byte[] header, int length)
    {
        if (length >= 12 && MatchesAt(header, 0, RiffSignature))
        {
            if (MatchesAt(header, 8, WebpSignature))
            {
                return NodeKinds.ImageRaster;
            }

            if (MatchesAt(header, 8, WaveSignature))
            {
                return NodeKinds.Audio;
            }

            if (MatchesAt(header, 8, AviSignature))
            {
                return NodeKinds.Video;
            }

            return null;
        }

        foreach ((int offset, byte[] pattern, string kind) in MagicTable)
        {
            if (length < offset + pattern.Length)
            {
                continue;
            }

            if (MatchesAt(header, offset, pattern))
            {
                return kind;
            }
        }

        if (length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            return NodeKinds.Audio;
        }

        return null;
    }

    private static bool MatchesAt(byte[] header, int offset, byte[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (header[offset + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string? IdentifyZipFamily(string fileName)
    {
        string? extension = GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        string bare = extension.StartsWith('.') ? extension.Substring(1) : extension;
        return ZipFamilyKinds.TryGetValue(bare, out string? kind) ? kind : null;
    }

    private static string? IdentifyByMime(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        string mime = mimeType.Trim();
        int parameters = mime.IndexOf(';');
        if (parameters >= 0)
        {
            mime = mime.Substring(0, parameters).Trim();
        }

        if (string.Equals(mime, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (MimePrefixKinds.TryGetValue(mime, out string? exact))
        {
            return exact;
        }

        int slash = mime.IndexOf('/');
        if (slash > 0)
        {
            string prefix = mime.Substring(0, slash + 1);
            if (MimePrefixKinds.TryGetValue(prefix, out string? prefixed))
            {
                return prefixed;
            }
        }

        return null;
    }

    private static string? GetExtension(string fileName)
    {
        string name = GlobMatcher.GetFileName(fileName);
        int dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1)
        {
            return null;
        }

        return name.Substring(dot);
    }
}
