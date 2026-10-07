using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using InkGuide.Analysis.Catalogs;

namespace InkGuide.Analysis.Inspection;

/// <summary>
/// Content inspection over a capped sample (at most MAX_ANALYSIS_READ_BYTES upstream).
/// Pure and tolerant: malformed input yields partial or empty facts, never throws.
/// </summary>
public static class ContentInspector
{
    /// <summary>Maximum characters kept in a doc excerpt.</summary>
    public const int MaxDocExcerptChars = 200;

    /// <summary>Maximum items kept in any peek list.</summary>
    public const int MaxPeekListItems = 20;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Analyses text-ness, encoding and line endings of a content sample.</summary>
    public static TextAnalysis AnalyzeText(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return new TextAnalysis(true, "utf-8", false, "none", 0);
        }

        string encoding = DetectBomEncoding(content, out bool hasBom);
        if (hasBom)
        {
            string text = DecodeForScan(content, encoding);
            if (IsControlHeavyText(text))
            {
                return new TextAnalysis(false, "binary", false, "none", 0);
            }

            (string eol, int lines) = ScanEol(text);
            return new TextAnalysis(true, encoding, true, eol, lines);
        }

        if (ContainsNullByte(content))
        {
            return new TextAnalysis(false, "binary", false, "none", 0);
        }

        encoding = IsValidUtf8(content) ? "utf-8" : "latin1";
        string decoded = DecodeForScan(content, encoding);
        if (IsControlHeavyBytes(content))
        {
            return new TextAnalysis(false, "binary", false, "none", 0);
        }

        (string eolStyle, int lineCount) = ScanEol(decoded);
        return new TextAnalysis(true, encoding, false, eolStyle, lineCount);
    }

    /// <summary>Extracts structured facts based on the file extension. Returns empty facts for non-text.</summary>
    /// <param name="fileName">File name (dispatches the peek).</param>
    /// <param name="content">Capped content bytes.</param>
    /// <param name="text">Text analysis of the same content.</param>
    /// <param name="includeDocExcerpt">False for Sensitive nodes so no secret-adjacent text is captured.</param>
    public static StructuredPeek PeekStructured(string fileName, byte[] content, TextAnalysis text, bool includeDocExcerpt = true)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(text);
        if (!text.IsText || content.Length == 0)
        {
            return new StructuredPeek();
        }

        string preview = DecodeForScan(content, text.EncodingName);
        string extension = GetLowerExtension(fileName);
        string baseName = GlobMatcher.GetFileName(fileName);

        IReadOnlyList<string>? jsonTopKeys = null;
        IReadOnlyList<string>? yamlTopKeys = null;
        IReadOnlyList<string>? tomlSections = null;
        string? xmlRoot = null;
        IReadOnlyList<string>? xmlChildNames = null;
        IReadOnlyList<string>? csvHeaders = null;
        string? csvDelimiter = null;
        PackageJsonInfo? packageJson = null;
        CsprojInfo? csproj = null;
        string? htmlTitle = null;
        string? htmlDescription = null;
        string? markdownH1 = null;

        switch (extension)
        {
            case ".json":
                jsonTopKeys = ReadJsonTopKeys(preview);
                if (string.Equals(baseName, "package.json", StringComparison.OrdinalIgnoreCase))
                {
                    packageJson = ReadPackageJson(preview);
                }

                break;
            case ".yaml":
            case ".yml":
                yamlTopKeys = ReadYamlTopKeys(preview);
                break;
            case ".toml":
                tomlSections = ReadTomlSections(preview);
                break;
            case ".xml":
            case ".csproj":
            case ".props":
            case ".targets":
            case ".resx":
            case ".config":
            case ".nuspec":
                (xmlRoot, xmlChildNames) = ReadXmlOutline(preview);
                if (string.Equals(extension, ".csproj", StringComparison.Ordinal))
                {
                    csproj = ReadCsproj(preview);
                }

                break;
            case ".csv":
            case ".tsv":
            case ".tab":
                (csvHeaders, csvDelimiter) = ReadCsvHeaders(preview, extension);
                break;
            case ".html":
            case ".htm":
                (htmlTitle, htmlDescription) = ReadHtmlHead(preview);
                break;
            case ".md":
            case ".markdown":
            case ".mdown":
                markdownH1 = ReadMarkdownH1(preview);
                break;
            default:
                break;
        }

        string? docExcerpt = includeDocExcerpt ? ExtractDocExcerpt(preview, extension) : null;

        return new StructuredPeek(
            jsonTopKeys,
            yamlTopKeys,
            tomlSections,
            xmlRoot,
            xmlChildNames,
            csvHeaders,
            csvDelimiter,
            packageJson,
            csproj,
            htmlTitle,
            htmlDescription,
            markdownH1,
            docExcerpt);
    }

    /// <summary>Reads image dimensions via hand-written header readers. Null when unparseable.</summary>
    public static ImageInfo? ReadImageInfo(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ReadPngInfo(content)
            ?? ReadGifInfo(content)
            ?? ReadBmpInfo(content)
            ?? ReadJpegInfo(content)
            ?? ReadWebpInfo(content);
    }

    /// <summary>Reads PDF title and page count tolerantly. Nulls when unparseable.</summary>
    public static PdfInfo ReadPdfInfo(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string text = Encoding.Latin1.GetString(content);
        return new PdfInfo(ReadPdfTitle(text), ReadPdfPageCount(text));
    }

    /// <summary>Lists zip entries from the central directory (tail-first, no extraction).</summary>
    public static ZipListing ReadZipListing(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length < 22)
        {
            return new ZipListing(0, Array.Empty<string>(), false);
        }

        int searchStart = Math.Max(0, content.Length - 22 - 65557);
        int eocd = -1;
        for (int i = content.Length - 22; i >= searchStart; i--)
        {
            if (content[i] == (byte)'P' && content[i + 1] == (byte)'K' && content[i + 2] == 0x05 && content[i + 3] == 0x06)
            {
                eocd = i;
                break;
            }
        }

        if (eocd < 0)
        {
            return new ZipListing(0, Array.Empty<string>(), false);
        }

        int entryCount = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(eocd + 10, 2));
        long centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(eocd + 16, 4));
        if (centralOffset < 0 || centralOffset >= content.Length)
        {
            return new ZipListing(entryCount, Array.Empty<string>(), false);
        }

        List<string> topLevel = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        long position = centralOffset;
        int parsed = 0;
        while (parsed < entryCount && parsed < 5000 && position + 46 <= content.Length)
        {
            if (content[position] != (byte)'P' || content[position + 1] != (byte)'K' ||
                content[position + 2] != 0x01 || content[position + 3] != 0x02)
            {
                break;
            }

            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan((int)position + 28, 2));
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan((int)position + 30, 2));
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan((int)position + 32, 2));
            long nameStart = position + 46;
            if (nameStart + nameLength > content.Length)
            {
                break;
            }

            string name = Encoding.UTF8.GetString(content, (int)nameStart, nameLength);
            if (!string.IsNullOrEmpty(name) && topLevel.Count < MaxPeekListItems)
            {
                int slash = name.IndexOf('/');
                string top = slash < 0 ? name : name.Substring(0, slash);
                if (!string.IsNullOrEmpty(top) && seen.Add(top))
                {
                    topLevel.Add(top);
                }
            }

            position = nameStart + nameLength + extraLength + commentLength;
            parsed++;
        }

        return new ZipListing(entryCount, topLevel, parsed >= entryCount);
    }

    private static string DetectBomEncoding(byte[] content, out bool hasBom)
    {
        hasBom = true;
        if (content.Length >= 4 && content[0] == 0xFF && content[1] == 0xFE && content[2] == 0x00 && content[3] == 0x00)
        {
            return "utf-32le";
        }

        if (content.Length >= 4 && content[0] == 0x00 && content[1] == 0x00 && content[2] == 0xFE && content[3] == 0xFF)
        {
            return "utf-32be";
        }

        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return "utf-8";
        }

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return "utf-16le";
        }

        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
        {
            return "utf-16be";
        }

        hasBom = false;
        return "utf-8";
    }

    private static bool ContainsNullByte(byte[] content)
    {
        int sample = Math.Min(content.Length, 8192);
        for (int i = 0; i < sample; i++)
        {
            if (content[i] == 0x00)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidUtf8(byte[] content)
    {
        try
        {
            StrictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string DecodeForScan(byte[] content, string encodingName)
    {
        return encodingName switch
        {
            "utf-16le" => Encoding.Unicode.GetString(content),
            "utf-16be" => Encoding.BigEndianUnicode.GetString(content),
            "utf-32le" => Encoding.UTF32.GetString(content),
            "utf-32be" => new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetString(content),
            "latin1" => Encoding.Latin1.GetString(content),
            _ => Encoding.UTF8.GetString(content),
        };
    }

    private static bool IsControlHeavyBytes(byte[] content)
    {
        int sample = Math.Min(content.Length, 8192);
        int suspicious = 0;
        for (int i = 0; i < sample; i++)
        {
            byte b = content[i];
            if (b == 0x09 || b == 0x0A || b == 0x0D)
            {
                continue;
            }

            if (b < 0x20 || b == 0x7F)
            {
                suspicious++;
            }
        }

        return sample > 0 && (double)suspicious / sample > 0.30;
    }

    private static bool IsControlHeavyText(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        int suspicious = 0;
        int sample = Math.Min(text.Length, 8192);
        for (int i = 0; i < sample; i++)
        {
            char c = text[i];
            if (c == '\t' || c == '\n' || c == '\r')
            {
                continue;
            }

            if (c < 0x20 || c == 0x7F)
            {
                suspicious++;
            }
        }

        return (double)suspicious / sample > 0.30;
    }

    private static (string EolStyle, int LineCount) ScanEol(string text)
    {
        int crlf = 0;
        int lf = 0;
        int cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        string style = kinds switch
        {
            0 => "none",
            _ when kinds > 1 => "mixed",
            _ when crlf > 0 => "crlf",
            _ when lf > 0 => "lf",
            _ => "cr",
        };
        int lines = crlf + lf + cr;
        if (text.Length > 0 && !text.EndsWith('\n') && !text.EndsWith('\r'))
        {
            lines++;
        }

        return (style, lines);
    }

    private static string GetLowerExtension(string fileName)
    {
        string name = GlobMatcher.GetFileName(fileName);
        int dot = name.LastIndexOf('.');
        return dot < 0 ? string.Empty : name.Substring(dot).ToLowerInvariant();
    }

    private static List<string>? ReadJsonTopKeys(string preview)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(preview);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            List<string> keys = new();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (keys.Count >= MaxPeekListItems)
                {
                    break;
                }

                keys.Add(property.Name);
            }

            return keys.Count == 0 ? null : keys;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PackageJsonInfo? ReadPackageJson(string preview)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(preview);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement root = document.RootElement;
            string? name = root.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            string? description = root.TryGetProperty("description", out JsonElement descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
                ? descriptionElement.GetString()
                : null;

            List<string> scripts = new();
            if (root.TryGetProperty("scripts", out JsonElement scriptsElement) && scriptsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty script in scriptsElement.EnumerateObject())
                {
                    if (scripts.Count >= MaxPeekListItems)
                    {
                        break;
                    }

                    scripts.Add(script.Name);
                }
            }

            List<string> dependencies = new();
            foreach (string section in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
            {
                if (root.TryGetProperty(section, out JsonElement sectionElement) && sectionElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty dependency in sectionElement.EnumerateObject())
                    {
                        if (dependencies.Count >= MaxPeekListItems * 2)
                        {
                            break;
                        }

                        if (!dependencies.Contains(dependency.Name))
                        {
                            dependencies.Add(dependency.Name);
                        }
                    }
                }
            }

            return new PackageJsonInfo(name, description, scripts, dependencies);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string>? ReadYamlTopKeys(string preview)
    {
        List<string> keys = new();
        foreach (string rawLine in preview.Split('\n'))
        {
            if (keys.Count >= MaxPeekListItems)
            {
                break;
            }

            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#' || line[0] == ' ' || line[0] == '\t' || line.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line.Substring(0, colon).Trim().Trim('\'', '"');
            if (key.Length > 0 && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys.Count == 0 ? null : keys;
    }

    private static List<string>? ReadTomlSections(string preview)
    {
        List<string> sections = new();
        foreach (string rawLine in preview.Split('\n'))
        {
            if (sections.Count >= MaxPeekListItems)
            {
                break;
            }

            string line = rawLine.Trim();
            if (!line.StartsWith('['))
            {
                continue;
            }

            int end = line.IndexOf(']');
            if (end <= 1)
            {
                continue;
            }

            string section = line.Substring(1, end - 1).Trim().Trim('[', ']', ' ', '\t', '"', '\'');
            if (section.Length > 0 && !sections.Contains(section))
            {
                sections.Add(section);
            }
        }

        return sections.Count == 0 ? null : sections;
    }

    private static (string? Root, IReadOnlyList<string>? Children) ReadXmlOutline(string preview)
    {
        try
        {
            XmlDocument document = new()
            {
                XmlResolver = null,
            };
            document.LoadXml(preview);
            XmlElement? root = document.DocumentElement;
            if (root is null)
            {
                return (null, null);
            }

            List<string> children = new();
            foreach (XmlNode child in root.ChildNodes)
            {
                if (children.Count >= MaxPeekListItems)
                {
                    break;
                }

                if (child.NodeType == XmlNodeType.Element && !children.Contains(child.Name))
                {
                    children.Add(child.Name);
                }
            }

            return (root.Name, children.Count == 0 ? null : children);
        }
        catch (XmlException)
        {
            return (null, null);
        }
    }

    private static CsprojInfo? ReadCsproj(string preview)
    {
        try
        {
            XmlDocument document = new()
            {
                XmlResolver = null,
            };
            document.LoadXml(preview);
            XmlElement? root = document.DocumentElement;
            if (root is null || !string.Equals(root.Name, "Project", StringComparison.Ordinal))
            {
                return null;
            }

            string? sdk = root.GetAttribute("Sdk");
            List<string> frameworks = new();
            foreach (XmlNode node in root.GetElementsByTagName("TargetFramework"))
            {
                AddDistinct(frameworks, node.InnerText.Trim(), MaxPeekListItems);
            }

            foreach (XmlNode node in root.GetElementsByTagName("TargetFrameworks"))
            {
                foreach (string framework in node.InnerText.Split(';'))
                {
                    AddDistinct(frameworks, framework.Trim(), MaxPeekListItems);
                }
            }

            bool usesWpf = IsMsBuildTrue(GetFirstElementText(root, "UseWPF"));
            bool usesForms = IsMsBuildTrue(GetFirstElementText(root, "UseWindowsForms"));

            List<string> packages = new();
            foreach (XmlNode node in root.GetElementsByTagName("PackageReference"))
            {
                if (node is XmlElement element)
                {
                    AddDistinct(packages, element.GetAttribute("Include").Trim(), MaxPeekListItems);
                }
            }

            return new CsprojInfo(string.IsNullOrWhiteSpace(sdk) ? null : sdk.Trim(), frameworks, usesWpf, usesForms, packages);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static (IReadOnlyList<string>? Headers, string? Delimiter) ReadCsvHeaders(string preview, string extension)
    {
        int newline = preview.IndexOf('\n');
        string firstLine = (newline < 0 ? preview : preview.Substring(0, newline)).TrimEnd('\r');
        if (firstLine.Length == 0)
        {
            return (null, null);
        }

        if (firstLine.Length > 4096)
        {
            firstLine = firstLine.Substring(0, 4096);
        }

        char delimiter = string.Equals(extension, ".tsv", StringComparison.Ordinal) ||
            string.Equals(extension, ".tab", StringComparison.Ordinal)
            ? '\t'
            : DetectCsvDelimiter(firstLine);
        List<string> headers = SplitCsvLine(firstLine, delimiter);
        if (headers.Count > MaxPeekListItems)
        {
            headers = headers.GetRange(0, MaxPeekListItems);
        }

        string delimiterName = delimiter switch
        {
            '\t' => "tab",
            ';' => "semicolon",
            '|' => "pipe",
            _ => "comma",
        };
        return (headers.Count == 0 ? null : headers, delimiterName);
    }

    private static char DetectCsvDelimiter(string firstLine)
    {
        int commas = CountChar(firstLine, ',');
        int semicolons = CountChar(firstLine, ';');
        int tabs = CountChar(firstLine, '\t');
        int pipes = CountChar(firstLine, '|');
        if (tabs >= commas && tabs >= semicolons && tabs >= pipes && tabs > 0)
        {
            return '\t';
        }

        if (semicolons >= commas && semicolons >= pipes && semicolons > 0)
        {
            return ';';
        }

        if (pipes >= commas && pipes > 0)
        {
            return '|';
        }

        return ',';
    }

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        List<string> fields = new();
        StringBuilder current = new();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    private static (string? Title, string? Description) ReadHtmlHead(string preview)
    {
        string? title = null;
        int titleStart = preview.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (titleStart >= 0)
        {
            int titleContent = preview.IndexOf('>', titleStart);
            if (titleContent >= 0)
            {
                int titleEnd = preview.IndexOf("</title", titleContent, StringComparison.OrdinalIgnoreCase);
                if (titleEnd > titleContent)
                {
                    title = preview.Substring(titleContent + 1, titleEnd - titleContent - 1).Trim();
                    if (title.Length > MaxDocExcerptChars)
                    {
                        title = title.Substring(0, MaxDocExcerptChars);
                    }

                    if (title.Length == 0)
                    {
                        title = null;
                    }
                }
            }
        }

        string? description = null;
        int search = 0;
        for (int guard = 0; guard < 40; guard++)
        {
            int meta = preview.IndexOf("<meta", search, StringComparison.OrdinalIgnoreCase);
            if (meta < 0)
            {
                break;
            }

            int tagEnd = preview.IndexOf('>', meta);
            if (tagEnd < 0)
            {
                break;
            }

            string tag = preview.Substring(meta, tagEnd - meta);
            if (tag.Contains("name=\"description\"", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("name='description'", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("property=\"og:description\"", StringComparison.OrdinalIgnoreCase))
            {
                description = ReadHtmlAttribute(tag, "content");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    description = description.Trim();
                    if (description.Length > 300)
                    {
                        description = description.Substring(0, 300);
                    }

                    break;
                }

                description = null;
            }

            search = tagEnd + 1;
        }

        return (title, description);
    }

    private static string? ReadHtmlAttribute(string tag, string attribute)
    {
        foreach (char quote in new[] { '"', '\'' })
        {
            string marker = attribute + "=" + quote;
            int start = tag.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                int valueStart = start + marker.Length;
                int valueEnd = tag.IndexOf(quote, valueStart);
                if (valueEnd > valueStart)
                {
                    return tag.Substring(valueStart, valueEnd - valueStart);
                }
            }
        }

        return null;
    }

    private static string? ReadMarkdownH1(string preview)
    {
        foreach (string rawLine in preview.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                string heading = line.Substring(2).Trim().Trim('#', ' ').Trim();
                if (heading.Length > MaxDocExcerptChars)
                {
                    heading = heading.Substring(0, MaxDocExcerptChars);
                }

                return heading.Length == 0 ? null : heading;
            }
        }

        return null;
    }

    private static string? ExtractDocExcerpt(string preview, string extension)
    {
        string prefix = CommentPrefixFor(extension);
        List<string> fragments = new();
        foreach (string rawLine in preview.Split('\n'))
        {
            if (fragments.Count >= 5)
            {
                break;
            }

            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#!", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(prefix, "<!--", StringComparison.Ordinal))
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string fragment = line.Substring(prefix.Length);
                    int end = fragment.IndexOf("-->", StringComparison.Ordinal);
                    fragments.Add((end < 0 ? fragment : fragment.Substring(0, end)).Trim());
                    if (end >= 0)
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            else if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                string fragment = line.Substring(prefix.Length).Trim().TrimStart('/', ' ', '\t');
                if (fragment.Length > 0)
                {
                    fragments.Add(fragment);
                }
            }
            else
            {
                break;
            }
        }

        string joined = string.Join(" ", fragments).Trim();
        if (joined.Length < 4)
        {
            return null;
        }

        return joined.Length > MaxDocExcerptChars ? joined.Substring(0, MaxDocExcerptChars) : joined;
    }

    private static string CommentPrefixFor(string extension)
    {
        return extension switch
        {
            ".py" or ".pyw" or ".rb" or ".sh" or ".bash" or ".yaml" or ".yml" or ".toml" or ".pl" or ".r" => "#",
            ".sql" or ".lua" => "--",
            ".html" or ".htm" or ".xml" or ".svg" => "<!--",
            _ => "//",
        };
    }

    private static ImageInfo? ReadPngInfo(byte[] content)
    {
        if (content.Length < 24)
        {
            return null;
        }

        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        for (int i = 0; i < signature.Length; i++)
        {
            if (content[i] != signature[i])
            {
                return null;
            }
        }

        if (content[12] != (byte)'I' || content[13] != (byte)'H' || content[14] != (byte)'D' || content[15] != (byte)'R')
        {
            return null;
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(20, 4));
        return ValidDimensions(width, height) ? new ImageInfo("png", width, height) : null;
    }

    private static ImageInfo? ReadGifInfo(byte[] content)
    {
        if (content.Length < 10)
        {
            return null;
        }

        bool gif87 = content[0] == (byte)'G' && content[1] == (byte)'I' && content[2] == (byte)'F' &&
            content[3] == (byte)'8' && content[4] == (byte)'7' && content[5] == (byte)'a';
        bool gif89 = content[0] == (byte)'G' && content[1] == (byte)'I' && content[2] == (byte)'F' &&
            content[3] == (byte)'8' && content[4] == (byte)'9' && content[5] == (byte)'a';
        if (!gif87 && !gif89)
        {
            return null;
        }

        int width = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(6, 2));
        int height = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(8, 2));
        return ValidDimensions(width, height) ? new ImageInfo("gif", width, height) : null;
    }

    private static ImageInfo? ReadBmpInfo(byte[] content)
    {
        if (content.Length < 26 || content[0] != (byte)'B' || content[1] != (byte)'M')
        {
            return null;
        }

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(14, 4));
        int width;
        int height;
        if (headerSize == 12)
        {
            if (content.Length < 22)
            {
                return null;
            }

            width = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(18, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(20, 2));
        }
        else
        {
            if (content.Length < 26)
            {
                return null;
            }

            width = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(18, 4));
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(22, 4)));
        }

        return ValidDimensions(width, height) ? new ImageInfo("bmp", width, height) : null;
    }

    private static ImageInfo? ReadJpegInfo(byte[] content)
    {
        if (content.Length < 4 || content[0] != 0xFF || content[1] != 0xD8)
        {
            return null;
        }

        int position = 2;
        for (int guard = 0; guard < 200; guard++)
        {
            if (position + 1 >= content.Length || content[position] != 0xFF)
            {
                return null;
            }

            byte marker = content[position + 1];
            position += 2;
            if (marker == 0xD9)
            {
                return null;
            }

            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            if (position + 1 >= content.Length)
            {
                return null;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(position, 2));
            if (segmentLength < 2)
            {
                return null;
            }

            bool isStartOfFrame = (marker >= 0xC0 && marker <= 0xCF) && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isStartOfFrame)
            {
                if (position + 7 >= content.Length)
                {
                    return null;
                }

                int height = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(position + 3, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(position + 5, 2));
                return ValidDimensions(width, height) ? new ImageInfo("jpeg", width, height) : null;
            }

            position += segmentLength;
        }

        return null;
    }

    private static ImageInfo? ReadWebpInfo(byte[] content)
    {
        if (content.Length < 12 || content[0] != (byte)'R' || content[1] != (byte)'I' ||
            content[2] != (byte)'F' || content[3] != (byte)'F' ||
            content[8] != (byte)'W' || content[9] != (byte)'E' || content[10] != (byte)'B' || content[11] != (byte)'P')
        {
            return null;
        }

        if (content.Length < 16)
        {
            return null;
        }

        if (content[12] == (byte)'V' && content[13] == (byte)'P' && content[14] == (byte)'8' && content[15] == (byte)'X')
        {
            if (content.Length < 30)
            {
                return null;
            }

            int width = content[24] | (content[25] << 8) | (content[26] << 16);
            int height = content[27] | (content[28] << 8) | (content[29] << 16);
            return ValidDimensions(width + 1, height + 1) ? new ImageInfo("webp", width + 1, height + 1) : null;
        }

        if (content[12] == (byte)'V' && content[13] == (byte)'P' && content[14] == (byte)'8' && content[15] == (byte)'L')
        {
            if (content.Length < 25)
            {
                return null;
            }

            int packed = content[21] | (content[22] << 8) | (content[23] << 16) | (content[24] << 24);
            int width = (packed & 0x3FFF) + 1;
            int height = ((packed >> 14) & 0x3FFF) + 1;
            return ValidDimensions(width, height) ? new ImageInfo("webp", width, height) : null;
        }

        if (content[12] == (byte)'V' && content[13] == (byte)'P' && content[14] == (byte)'8' && content[15] == (byte)' ')
        {
            if (content.Length < 30 || content[23] != 0x9D || content[24] != 0x01 || content[25] != 0x2A)
            {
                return null;
            }

            int width = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(26, 2)) & 0x3FFF;
            int height = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(28, 2)) & 0x3FFF;
            return ValidDimensions(width, height) ? new ImageInfo("webp", width, height) : null;
        }

        return null;
    }

    private static bool ValidDimensions(int width, int height)
    {
        return width > 0 && height > 0 && width <= 100000 && height <= 100000;
    }

    private static string? ReadPdfTitle(string text)
    {
        int search = 0;
        for (int guard = 0; guard < 5; guard++)
        {
            int marker = text.IndexOf("/Title", search, StringComparison.Ordinal);
            if (marker < 0)
            {
                return null;
            }

            int cursor = marker + "/Title".Length;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            if (cursor < text.Length && text[cursor] == '(')
            {
                string? title = ReadPdfLiteral(text, cursor);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    title = title.Trim();
                    return title.Length > MaxDocExcerptChars ? title.Substring(0, MaxDocExcerptChars) : title;
                }

                return null;
            }

            search = cursor;
        }

        return null;
    }

    private static string? ReadPdfLiteral(string text, int openParen)
    {
        StringBuilder builder = new();
        int depth = 0;
        for (int i = openParen; i < text.Length && builder.Length < 500; i++)
        {
            char c = text[i];
            if (c == '(' && (i == openParen || text[i - 1] != '\\'))
            {
                depth++;
                continue;
            }

            if (c == '\\' && i + 1 < text.Length)
            {
                char next = text[i + 1];
                switch (next)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case '(':
                    case ')':
                    case '\\':
                        builder.Append(next);
                        break;
                    default:
                        if (next >= '0' && next <= '7' && i + 3 < text.Length &&
                            text[i + 2] >= '0' && text[i + 2] <= '7' && text[i + 3] >= '0' && text[i + 3] <= '7')
                        {
                            int code = ((next - '0') * 64) + ((text[i + 2] - '0') * 8) + (text[i + 3] - '0');
                            builder.Append((char)code);
                            i += 3;
                            break;
                        }

                        builder.Append(next);
                        break;
                }

                i++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return builder.ToString();
                }

                continue;
            }

            builder.Append(c);
        }

        return null;
    }

    private static int? ReadPdfPageCount(string text)
    {
        int? best = null;
        int search = 0;
        for (int guard = 0; guard < 500; guard++)
        {
            int marker = text.IndexOf("/Count", search, StringComparison.Ordinal);
            if (marker < 0)
            {
                break;
            }

            int cursor = marker + "/Count".Length;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            int digits = cursor;
            while (digits < text.Length && char.IsDigit(text[digits]))
            {
                digits++;
            }

            if (digits > cursor && int.TryParse(text.AsSpan(cursor, digits - cursor), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                if (count > 0 && (best is null || count > best))
                {
                    best = count;
                }
            }

            search = digits;
        }

        return best;
    }

    private static int CountChar(string text, char value)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c == value)
            {
                count++;
            }
        }

        return count;
    }

    private static string? GetFirstElementText(XmlElement root, string localName)
    {
        XmlNodeList nodes = root.GetElementsByTagName(localName);
        return nodes.Count == 0 ? null : nodes[0]?.InnerText;
    }

    private static bool IsMsBuildTrue(string? value)
    {
        return string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddDistinct(List<string> items, string value, int cap)
    {
        if (items.Count < cap && !string.IsNullOrWhiteSpace(value) && !items.Contains(value))
        {
            items.Add(value);
        }
    }
}
