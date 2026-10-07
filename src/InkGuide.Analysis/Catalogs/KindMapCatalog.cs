using System;
using System.Collections.Generic;
using System.Text.Json;
using InkGuide.Domain.Common;
using InkGuide.Domain.Kinds;

namespace InkGuide.Analysis.Catalogs;

/// <summary>
/// Extension/MIME to kind map loaded from config/kind-map.json.
/// Data, not code: the file is shipped with the app and user-overridable at runtime.
/// </summary>
public sealed class KindMapCatalog
{
    private readonly Dictionary<string, string> _extensions;
    private readonly Dictionary<string, string> _mimeFallback;

    /// <summary>Catalog schema version.</summary>
    public int Version { get; }

    /// <summary>Creates a catalog from validated dictionaries (keys compared case-insensitively).</summary>
    public KindMapCatalog(int version, IDictionary<string, string> extensionToKind, IDictionary<string, string> mimeFallback)
    {
        ArgumentNullException.ThrowIfNull(extensionToKind);
        ArgumentNullException.ThrowIfNull(mimeFallback);
        Version = version;
        _extensions = new Dictionary<string, string>(extensionToKind, StringComparer.OrdinalIgnoreCase);
        _mimeFallback = new Dictionary<string, string>(mimeFallback, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Parses and validates kind-map.json content. Unknown kind ids are rejected.</summary>
    public static Result<KindMapCatalog, Failure> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result.Failure<KindMapCatalog>(Errors.InvalidParams($"kind-map.json is not valid JSON: {ex.Message}"));
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure<KindMapCatalog>(Errors.InvalidParams("kind-map.json root must be an object"));
            }

            int version = CatalogJson.TryGetInt(root, "version", 1);
            Result<Dictionary<string, string>, Failure> extensions = ReadKindTable(root, "map", normalizeExtension: true);
            if (extensions.IsFailure)
            {
                ArgumentNullException.ThrowIfNull(extensions.Error);
                return Result.Failure<KindMapCatalog>(extensions.Error);
            }

            Result<Dictionary<string, string>, Failure> mime = ReadKindTable(root, "mimeFallback", normalizeExtension: false);
            if (mime.IsFailure)
            {
                ArgumentNullException.ThrowIfNull(mime.Error);
                return Result.Failure<KindMapCatalog>(mime.Error);
            }

            ArgumentNullException.ThrowIfNull(extensions.Value);
            ArgumentNullException.ThrowIfNull(mime.Value);
            return Result.Success(new KindMapCatalog(version, extensions.Value, mime.Value));
        }
    }

    /// <summary>Looks up a kind by file extension (with or without a leading dot). Null when unmapped.</summary>
    public string? GetKindByExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        string key = extension.Trim();
        if (!key.StartsWith('.'))
        {
            key = "." + key;
        }

        return _extensions.TryGetValue(key, out string? kind) ? kind : null;
    }

    /// <summary>Looks up a kind by MIME type (parameters after ';' are ignored). Null when unmapped.</summary>
    public string? GetKindByMime(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        string key = mimeType.Trim();
        int parameters = key.IndexOf(';');
        if (parameters >= 0)
        {
            key = key.Substring(0, parameters).Trim();
        }

        return _mimeFallback.TryGetValue(key, out string? kind) ? kind : null;
    }

    private static Result<Dictionary<string, string>, Failure> ReadKindTable(JsonElement root, string propertyName, bool normalizeExtension)
    {
        Dictionary<string, string> table = new(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(propertyName, out JsonElement section))
        {
            return Result.Success(table);
        }

        if (section.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<Dictionary<string, string>>(Errors.InvalidParams($"kind-map.json '{propertyName}' must be an object"));
        }

        foreach (JsonProperty entry in section.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                return Result.Failure<Dictionary<string, string>>(Errors.InvalidParams($"kind-map.json '{propertyName}.{entry.Name}' must be a string"));
            }

            string? kind = entry.Value.GetString();
            if (string.IsNullOrWhiteSpace(kind) || !NodeKinds.IsKnown(kind.Trim()))
            {
                return Result.Failure<Dictionary<string, string>>(Errors.InvalidParams($"kind-map.json '{propertyName}.{entry.Name}' has an unknown kind id '{kind}'"));
            }

            string key = entry.Name.Trim();
            if (normalizeExtension && !key.StartsWith('.'))
            {
                key = "." + key;
            }

            table[key] = kind.Trim();
        }

        return Result.Success(table);
    }
}
