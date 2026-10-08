using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using InkGuide.Analysis.Catalogs;
using InkGuide.Domain.Common;

namespace InkGuide.App.Bridge.Backend;

/// <summary>Mutable holder for the effective catalogs; rebuilt by rules.reload.</summary>
public sealed class CatalogHolder
{
    private readonly object _lock = new();
    private readonly string _shippedDirectory;
    private readonly string _userDirectory;

    public CatalogHolder(AnalysisCatalogs current, string shippedDirectory, string userDirectory)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(shippedDirectory);
        ArgumentNullException.ThrowIfNull(userDirectory);
        Current = current;
        _shippedDirectory = shippedDirectory;
        _userDirectory = userDirectory;
    }

    public AnalysisCatalogs Current { get; private set; }

    public AnalysisCatalogs Reload()
    {
        lock (_lock)
        {
            Current = AnalysisCatalogs.Load(_shippedDirectory, _userDirectory);
            return Current;
        }
    }
}

/// <summary>
/// Loaded analysis catalogs with user-override merging (MASTER_PROMPT R-05,
/// Section 10.2): user files in the rules directory merge OVER the shipped
/// catalogs, winning on equal ids. Shipped files live in config/.
/// </summary>
public sealed record AnalysisCatalogs(
    KindMapCatalog KindMap,
    FileRoleCatalog FileRoles,
    ProjectMarkerCatalog ProjectMarkers,
    IReadOnlyList<string> IgnorePatterns,
    string RulesetVersion,
    string SourceSummary,
    int KindCount)
{
    public int DetectorCount => ProjectMarkers.Projects.Count;
    public int RoleCount => FileRoles.Rules.Count;

    private static readonly string[] KindMapKeys = new[] { "map", "mimeFallback" };

    public static AnalysisCatalogs Load(string shippedDirectory, string userDirectory)
    {
        ArgumentNullException.ThrowIfNull(shippedDirectory);
        ArgumentNullException.ThrowIfNull(userDirectory);

        string kindJson = MergeObjects(
            ReadFile(shippedDirectory, "kind-map.json"),
            ReadFile(userDirectory, "kind-map.json"),
            KindMapKeys);
        string roleJson = MergeArraysById(
            ReadFile(shippedDirectory, "file-roles.json"),
            ReadFile(userDirectory, "file-roles.json"),
            "roles",
            "rules");
        string markerJson = MergeMarkers(
            ReadFile(shippedDirectory, "project-markers.json"),
            ReadFile(userDirectory, "project-markers.json"));
        string ignoreJson = MergeStringArrays(
            ReadFile(shippedDirectory, "ignore-patterns.json"),
            ReadFile(userDirectory, "ignore-patterns.json"),
            "patterns");

        Result<KindMapCatalog, Failure> kinds = KindMapCatalog.Parse(kindJson);
        if (kinds.IsFailure || kinds.Value is null)
        {
            throw new InvalidOperationException("Shipped kind-map.json is invalid: " + kinds.Error?.Message);
        }

        Result<FileRoleCatalog, Failure> roles = FileRoleCatalog.Parse(roleJson);
        if (roles.IsFailure || roles.Value is null)
        {
            throw new InvalidOperationException("Shipped file-roles.json is invalid: " + roles.Error?.Message);
        }

        Result<ProjectMarkerCatalog, Failure> markers = ProjectMarkerCatalog.Parse(markerJson);
        if (markers.IsFailure || markers.Value is null)
        {
            throw new InvalidOperationException("Shipped project-markers.json is invalid: " + markers.Error?.Message);
        }

        Result<IReadOnlyList<string>, Failure> ignore = IgnoreList.Parse(ignoreJson);
        IReadOnlyList<string> patterns = ignore.IsSuccess && ignore.Value is not null
            ? ignore.Value
            : Array.Empty<string>();

        return new AnalysisCatalogs(
            kinds.Value,
            roles.Value,
            markers.Value,
            patterns,
            roles.Value.RulesetVersion,
            "shipped=" + shippedDirectory + "; user=" + userDirectory,
            CountKinds(kindJson));
    }

    private static int CountKinds(string kindJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(kindJson);
            int count = 0;
            foreach (string key in new[] { "map", "mimeFallback" })
            {
                if (doc.RootElement.TryGetProperty(key, out JsonElement element) && element.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty _ in element.EnumerateObject())
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string ReadFile(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string MergeObjects(string shipped, string user, IReadOnlyList<string> mapKeys)
    {
        using JsonDocument shippedDoc = ParseOrEmpty(shipped);
        using JsonDocument userDoc = ParseOrEmpty(user);
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (string key in AllKeys(shippedDoc, userDoc))
        {
            if (mapKeys.Contains(key))
            {
                merged[key] = MergeStringMap(GetMap(shippedDoc, key), GetMap(userDoc, key));
            }
            else
            {
                merged[key] = GetRaw(userDoc, key) ?? GetRaw(shippedDoc, key);
            }
        }

        return JsonSerializer.Serialize(merged);
    }

    private static string MergeArraysById(string shipped, string user, params string[] arrayKeys)
    {
        using JsonDocument shippedDoc = ParseOrEmpty(shipped);
        using JsonDocument userDoc = ParseOrEmpty(user);
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (string key in AllKeys(shippedDoc, userDoc))
        {
            if (arrayKeys.Contains(key))
            {
                merged[key] = MergeArrayById(GetArray(shippedDoc, key), GetArray(userDoc, key));
            }
            else
            {
                merged[key] = GetRaw(userDoc, key) ?? GetRaw(shippedDoc, key);
            }
        }

        return JsonSerializer.Serialize(merged);
    }

    private static string MergeMarkers(string shipped, string user)
    {
        using JsonDocument shippedDoc = ParseOrEmpty(shipped);
        using JsonDocument userDoc = ParseOrEmpty(user);
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (string key in AllKeys(shippedDoc, userDoc))
        {
            if (string.Equals(key, "projects", StringComparison.Ordinal))
            {
                merged[key] = MergeArrayById(GetArray(shippedDoc, key), GetArray(userDoc, key));
            }
            else if (string.Equals(key, "monoRoots", StringComparison.Ordinal))
            {
                var all = new List<object?>(GetArray(shippedDoc, key));
                all.AddRange(GetArray(userDoc, key));
                merged[key] = all;
            }
            else
            {
                merged[key] = GetRaw(userDoc, key) ?? GetRaw(shippedDoc, key);
            }
        }

        return JsonSerializer.Serialize(merged);
    }

    private static string MergeStringArrays(string shipped, string user, string key)
    {
        using JsonDocument shippedDoc = ParseOrEmpty(shipped);
        using JsonDocument userDoc = ParseOrEmpty(user);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = new List<string>();
        foreach (object? element in GetArray(shippedDoc, key).Concat(GetArray(userDoc, key)))
        {
            if (element is string value && !string.IsNullOrEmpty(value) && seen.Add(value))
            {
                all.Add(value);
            }
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?> { [key] = all });
    }

    private static JsonDocument ParseOrEmpty(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static HashSet<string> AllKeys(JsonDocument a, JsonDocument b)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty p in a.RootElement.EnumerateObject())
        {
            _ = keys.Add(p.Name);
        }

        foreach (JsonProperty p in b.RootElement.EnumerateObject())
        {
            _ = keys.Add(p.Name);
        }

        return keys;
    }

    private static Dictionary<string, string> GetMap(JsonDocument doc, string key)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty(key, out JsonElement element) && element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in element.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    map[p.Name] = p.Value.GetString() ?? string.Empty;
                }
            }
        }

        return map;
    }

    private static Dictionary<string, string> MergeStringMap(Dictionary<string, string> shipped, Dictionary<string, string> user)
    {
        var merged = new Dictionary<string, string>(shipped, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> kv in user)
        {
            merged[kv.Key] = kv.Value;
        }

        return merged;
    }

    private static List<object?> GetArray(JsonDocument doc, string key)
    {
        var list = new List<object?>();
        if (doc.RootElement.TryGetProperty(key, out JsonElement element) && element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                list.Add(JsonToObject(item));
            }
        }

        return list;
    }

    private static List<object?> MergeArrayById(List<object?> shipped, List<object?> user)
    {
        var merged = new List<object?>();
        var userIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (object? item in user)
        {
            merged.Add(item);
            string? id = ReadId(item);
            if (id is not null)
            {
                _ = userIds.Add(id);
            }
        }

        foreach (object? item in shipped)
        {
            string? id = ReadId(item);
            if (id is null || !userIds.Contains(id))
            {
                merged.Add(item);
            }
        }

        return merged;
    }

    private static string? ReadId(object? item)
    {
        if (item is Dictionary<string, object?> dict &&
            dict.TryGetValue("id", out object? id) && id is string text)
        {
            return text;
        }

        return null;
    }

    private static object? GetRaw(JsonDocument doc, string key)
    {
        if (doc.RootElement.TryGetProperty(key, out JsonElement element))
        {
            return JsonToObject(element);
        }

        return null;
    }

    private static object? JsonToObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (JsonProperty p in element.EnumerateObject())
                {
                    dict[p.Name] = JsonToObject(p.Value);
                }

                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    list.Add(JsonToObject(item));
                }

                return list;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long l))
                {
                    return l;
                }

                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
}
