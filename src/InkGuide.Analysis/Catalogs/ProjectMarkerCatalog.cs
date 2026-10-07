using System;
using System.Collections.Generic;
using System.Text.Json;
using InkGuide.Domain.Common;

namespace InkGuide.Analysis.Catalogs;

/// <summary>One marker: a path glob plus optional content evidence inside the matched file.</summary>
/// <param name="PathGlob">Glob matched against the candidate project-relative path.</param>
/// <param name="Contains">Required substring of the file text. Null for path-only markers.</param>
/// <param name="JsonPath">Dotted JSON path (for example <c>dependencies.next</c>) that must exist. Null when unused.</param>
public sealed record MarkerRule(string PathGlob, string? Contains, string? JsonPath);

/// <summary>One project type: weighted markers, applied tags and exclusion globs.</summary>
/// <param name="Id">Stable detector id (for example <c>web-nextjs</c>).</param>
/// <param name="Name">Human-readable project type name.</param>
/// <param name="Tags">Tags applied on confident detection.</param>
/// <param name="Markers">Evidence markers.</param>
/// <param name="Weight">Score contributed by a full marker match.</param>
/// <param name="Excludes">Globs hiding vendored or generated trees from evidence.</param>
/// <param name="ParentSensitive">Whether matches usually contain secrets.</param>
public sealed record ProjectMarker(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    IReadOnlyList<MarkerRule> Markers,
    int Weight,
    IReadOnlyList<string> Excludes,
    bool ParentSensitive);

/// <summary>One mono-repo root signature: marker files expected at the workspace root.</summary>
/// <param name="Id">Stable mono-root id.</param>
/// <param name="Markers">Root-level marker file names.</param>
public sealed record MonoRoot(string Id, IReadOnlyList<string> Markers);

/// <summary>Project-marker catalog loaded from config/project-markers.json. Data, not code.</summary>
public sealed class ProjectMarkerCatalog
{
    /// <summary>Catalog schema version.</summary>
    public int Version { get; }

    /// <summary>Project detectors in catalog order.</summary>
    public IReadOnlyList<ProjectMarker> Projects { get; }

    /// <summary>Mono-root signatures.</summary>
    public IReadOnlyList<MonoRoot> MonoRoots { get; }

    /// <summary>Creates a catalog.</summary>
    public ProjectMarkerCatalog(int version, IReadOnlyList<ProjectMarker> projects, IReadOnlyList<MonoRoot> monoRoots)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(monoRoots);
        Version = version;
        Projects = projects;
        MonoRoots = monoRoots;
    }

    /// <summary>Parses and validates project-markers.json content.</summary>
    public static Result<ProjectMarkerCatalog, Failure> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result.Failure<ProjectMarkerCatalog>(Errors.InvalidParams($"project-markers.json is not valid JSON: {ex.Message}"));
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure<ProjectMarkerCatalog>(Errors.InvalidParams("project-markers.json root must be an object"));
            }

            int version = CatalogJson.TryGetInt(root, "version", 1);

            List<ProjectMarker> projects = new();
            if (root.TryGetProperty("projects", out JsonElement projectsElement) && projectsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in projectsElement.EnumerateArray())
                {
                    Result<ProjectMarker, Failure> parsed = ParseProject(item);
                    if (parsed.IsFailure)
                    {
                        ArgumentNullException.ThrowIfNull(parsed.Error);
                        return Result.Failure<ProjectMarkerCatalog>(parsed.Error);
                    }

                    ArgumentNullException.ThrowIfNull(parsed.Value);
                    projects.Add(parsed.Value);
                }
            }

            List<MonoRoot> monoRoots = new();
            if (root.TryGetProperty("monoRoots", out JsonElement rootsElement) && rootsElement.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement item in rootsElement.EnumerateArray())
                {
                    Result<MonoRoot, Failure> parsed = ParseMonoRoot(item, index);
                    if (parsed.IsFailure)
                    {
                        ArgumentNullException.ThrowIfNull(parsed.Error);
                        return Result.Failure<ProjectMarkerCatalog>(parsed.Error);
                    }

                    ArgumentNullException.ThrowIfNull(parsed.Value);
                    monoRoots.Add(parsed.Value);
                    index++;
                }
            }

            return Result.Success(new ProjectMarkerCatalog(version, projects, monoRoots));
        }
    }

    private static Result<ProjectMarker, Failure> ParseProject(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<ProjectMarker>(Errors.InvalidParams("project-markers.json project entries must be objects"));
        }

        string? id = CatalogJson.TryGetString(item, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return Result.Failure<ProjectMarker>(Errors.InvalidParams("project-markers.json project entry needs a non-empty 'id'"));
        }

        string name = CatalogJson.TryGetString(item, "name") ?? id.Trim();
        IReadOnlyList<string> tags = CatalogJson.ReadStringOrArray(item, "tags");
        int weight = CatalogJson.TryGetInt(item, "weight", 10);
        if (weight < 0)
        {
            return Result.Failure<ProjectMarker>(Errors.InvalidParams($"project-markers.json project '{id}' has a negative weight"));
        }

        IReadOnlyList<string> excludes = CatalogJson.ReadStringOrArray(item, "excludes");
        bool parentSensitive = CatalogJson.TryGetBool(item, "parentSensitive", defaultValue: false);

        List<MarkerRule> markers = new();
        if (item.TryGetProperty("markers", out JsonElement markersElement) && markersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement marker in markersElement.EnumerateArray())
            {
                Result<MarkerRule, Failure> parsed = ParseMarker(marker, id);
                if (parsed.IsFailure)
                {
                    ArgumentNullException.ThrowIfNull(parsed.Error);
                    return Result.Failure<ProjectMarker>(parsed.Error);
                }

                ArgumentNullException.ThrowIfNull(parsed.Value);
                markers.Add(parsed.Value);
            }
        }

        if (markers.Count == 0)
        {
            return Result.Failure<ProjectMarker>(Errors.InvalidParams($"project-markers.json project '{id}' needs at least one marker"));
        }

        return Result.Success(new ProjectMarker(id.Trim(), name.Trim(), tags, markers, weight, excludes, parentSensitive));
    }

    private static Result<MarkerRule, Failure> ParseMarker(JsonElement item, string? projectId)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<MarkerRule>(Errors.InvalidParams($"project-markers.json project '{projectId}' marker entries must be objects"));
        }

        string? glob = CatalogJson.TryGetString(item, "path-glob")
            ?? CatalogJson.TryGetString(item, "pathGlob")
            ?? CatalogJson.TryGetString(item, "glob");
        if (string.IsNullOrWhiteSpace(glob))
        {
            return Result.Failure<MarkerRule>(Errors.InvalidParams($"project-markers.json project '{projectId}' marker needs a 'path-glob'"));
        }

        string? contains = CatalogJson.TryGetString(item, "contains");
        string? jsonPath = CatalogJson.TryGetString(item, "jsonPath") ?? CatalogJson.TryGetString(item, "json_path");
        return Result.Success(new MarkerRule(glob.Trim(), contains, jsonPath));
    }

    private static Result<MonoRoot, Failure> ParseMonoRoot(JsonElement item, int index)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<MonoRoot>(Errors.InvalidParams("project-markers.json monoRoots entries must be objects"));
        }

        string id = CatalogJson.TryGetString(item, "id") ?? $"mono-{index}";
        IReadOnlyList<string> markers = CatalogJson.ReadStringOrArray(item, "markers");
        if (markers.Count == 0)
        {
            return Result.Failure<MonoRoot>(Errors.InvalidParams($"project-markers.json mono root '{id}' needs at least one marker"));
        }

        return Result.Success(new MonoRoot(id.Trim(), markers));
    }
}
