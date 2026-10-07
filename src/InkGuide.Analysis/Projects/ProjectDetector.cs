using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using InkGuide.Analysis.Catalogs;

namespace InkGuide.Analysis.Projects;

/// <summary>One detected project: type, location, tags and confidence.</summary>
/// <param name="DetectorId">Detector type id (for example <c>web-nextjs</c>).</param>
/// <param name="Name">Human-readable project type name.</param>
/// <param name="RelativePath">Project root relative to the scanned tree, '/'-separated. Empty for the tree root.</param>
/// <param name="Tags">Tags to apply (for example <c>type:web-site</c>).</param>
/// <param name="Confidence">Detection confidence in 0-1.</param>
/// <param name="Evidence">Matched marker evidence (marker@path entries).</param>
public sealed record DetectedProject(
    string DetectorId,
    string Name,
    string RelativePath,
    IReadOnlyList<string> Tags,
    double Confidence,
    IReadOnlyList<string> Evidence);

/// <summary>Project detection outcome for one scanned tree.</summary>
/// <param name="Projects">Detected projects (nearest root wins, except under mono roots).</param>
/// <param name="IsMonoRoot">True when the tree root matches a mono-root signature.</param>
/// <param name="MonoRootEvidence">Matched mono-root marker files.</param>
public sealed record ProjectDetectionResult(
    IReadOnlyList<DetectedProject> Projects,
    bool IsMonoRoot,
    IReadOnlyList<string> MonoRootEvidence);

/// <summary>
/// Scores directories from marker evidence (depth-limited). Nearest root wins,
/// except recognised mono-roots whose children are detected as projects.
/// Auto-apply decisions use the 0.6 confidence threshold (see Tags.TagDecider).
/// </summary>
public sealed class ProjectDetector
{
    /// <summary>Default evidence depth limit.</summary>
    public const int DefaultMaxDepth = 4;

    private readonly ProjectMarkerCatalog _catalog;
    private readonly int _maxDepth;

    /// <summary>Creates a detector over a catalog with an evidence depth limit.</summary>
    public ProjectDetector(ProjectMarkerCatalog catalog, int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);
        _catalog = catalog;
        _maxDepth = maxDepth;
    }

    /// <summary>Detects projects in a tree described by repo-relative file paths.</summary>
    /// <param name="relativePaths">Repo-relative file paths ('/' separators).</param>
    /// <param name="fileContents">Optional path to text-excerpt map for contains/jsonPath markers. Missing content means the marker does not match.</param>
    public ProjectDetectionResult Detect(
        IReadOnlyList<string> relativePaths,
        IReadOnlyDictionary<string, string>? fileContents = null)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);

        HashSet<string> files = new(StringComparer.Ordinal);
        foreach (string raw in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            files.Add(GlobMatcher.NormalizePath(raw));
        }

        Dictionary<string, string> contents = new(StringComparer.Ordinal);
        if (fileContents is not null)
        {
            foreach (KeyValuePair<string, string> entry in fileContents)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && entry.Value is not null)
                {
                    contents[GlobMatcher.NormalizePath(entry.Key)] = entry.Value;
                }
            }
        }

        List<string> monoEvidence = DetectMonoRoot(files);
        bool isMonoRoot = monoEvidence.Count > 0;

        List<string> candidates = CollectCandidateDirectories(files);
        List<DetectedProject> detections = new();
        foreach (string directory in candidates)
        {
            foreach (ProjectMarker project in _catalog.Projects)
            {
                DetectedProject? detection = ScoreDirectory(project, directory, files, contents);
                if (detection is not null)
                {
                    detections.Add(detection);
                }
            }
        }

        List<DetectedProject> winners = ApplyNearestRootWins(detections, isMonoRoot);
        return new ProjectDetectionResult(winners, isMonoRoot, monoEvidence);
    }

    private List<string> DetectMonoRoot(HashSet<string> files)
    {
        List<string> evidence = new();
        foreach (string file in files)
        {
            if (DirectoryDepth(file) != 1)
            {
                continue;
            }

            foreach (MonoRoot mono in _catalog.MonoRoots)
            {
                foreach (string marker in mono.Markers)
                {
                    if (GlobMatcher.MatchPath(marker, file) && !evidence.Contains(file))
                    {
                        evidence.Add(file);
                    }
                }
            }
        }

        if (HasSolutionWithMultipleProjects(files) && !evidence.Contains("multi-project-sln"))
        {
            evidence.Add("multi-project-sln");
        }

        return evidence;
    }

    private bool HasSolutionWithMultipleProjects(HashSet<string> files)
    {
        int solutions = 0;
        int csprojs = 0;
        foreach (string file in files)
        {
            if (DirectoryDepth(file) > _maxDepth)
            {
                continue;
            }

            string name = GlobMatcher.GetFileName(file);
            if (name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                solutions++;
            }
            else if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                csprojs++;
            }
        }

        return solutions >= 1 && csprojs >= 2;
    }

    private List<string> CollectCandidateDirectories(HashSet<string> files)
    {
        HashSet<string> directories = new(StringComparer.Ordinal) { string.Empty };
        foreach (string file in files)
        {
            string[] segments = file.Split('/');
            for (int depth = 1; depth < segments.Length && depth <= _maxDepth; depth++)
            {
                directories.Add(string.Join("/", segments, 0, depth));
            }
        }

        List<string> ordered = new(directories);
        ordered.Sort((left, right) =>
        {
            int comparison = DirectoryDepth(left).CompareTo(DirectoryDepth(right));
            return comparison != 0 ? comparison : string.Compare(left, right, StringComparison.Ordinal);
        });
        return ordered;
    }

    private DetectedProject? ScoreDirectory(
        ProjectMarker project,
        string directory,
        HashSet<string> files,
        IReadOnlyDictionary<string, string> contents)
    {
        List<string> evidence = new();
        int matched = 0;
        foreach (MarkerRule marker in project.Markers)
        {
            string? hit = FindMarkerHit(marker, project, directory, files, contents);
            if (hit is not null)
            {
                matched++;
                if (evidence.Count < 8)
                {
                    evidence.Add(marker.PathGlob + "@" + hit);
                }
            }
        }

        if (matched == 0)
        {
            return null;
        }

        double score = (double)project.Weight * matched / project.Markers.Count;
        double confidence = Math.Round(score / (score + 6.0), 3, MidpointRounding.ToEven);
        return new DetectedProject(project.Id, project.Name, directory, project.Tags, confidence, evidence);
    }

    private string? FindMarkerHit(
        MarkerRule marker,
        ProjectMarker project,
        string directory,
        HashSet<string> files,
        IReadOnlyDictionary<string, string> contents)
    {
        foreach (string file in files)
        {
            if (!IsUnderDirectory(file, directory))
            {
                continue;
            }

            string relative = directory.Length == 0 ? file : file.Substring(directory.Length + 1);
            if (DirectoryDepth(relative) > _maxDepth)
            {
                continue;
            }

            if (!GlobMatcher.MatchPath(marker.PathGlob, relative))
            {
                continue;
            }

            if (IsExcluded(project, relative, file))
            {
                continue;
            }

            if (MarkerContentMatches(marker, file, contents))
            {
                return file;
            }
        }

        return null;
    }

    private static bool IsUnderDirectory(string file, string directory)
    {
        return directory.Length == 0 || file.StartsWith(directory + "/", StringComparison.Ordinal);
    }

    private static bool IsExcluded(ProjectMarker project, string directoryRelative, string fullPath)
    {
        foreach (string exclude in project.Excludes)
        {
            if (GlobMatcher.MatchPath(exclude, directoryRelative) || GlobMatcher.MatchPath(exclude, fullPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MarkerContentMatches(
        MarkerRule marker,
        string file,
        IReadOnlyDictionary<string, string> contents)
    {
        if (marker.Contains is null && marker.JsonPath is null)
        {
            return true;
        }

        if (!contents.TryGetValue(file, out string? text) || text is null)
        {
            return false;
        }

        if (marker.JsonPath is not null)
        {
            return JsonPathMatches(text, marker.JsonPath, marker.Contains);
        }

        if (marker.Contains is null)
        {
            return true;
        }

        if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("\"" + marker.Contains + "\"", StringComparison.OrdinalIgnoreCase);
        }

        return text.Contains(marker.Contains, StringComparison.OrdinalIgnoreCase);
    }

    private static bool JsonPathMatches(string text, string jsonPath, string? contains)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement current = document.RootElement;
            foreach (string segment in jsonPath.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out JsonElement next))
                {
                    return false;
                }

                current = next;
            }

            return contains is null || current.GetRawText().Contains(contains, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<DetectedProject> ApplyNearestRootWins(List<DetectedProject> detections, bool isMonoRoot)
    {
        List<DetectedProject> ordered = new(detections);
        ordered.Sort((left, right) =>
        {
            int comparison = DirectoryDepth(left.RelativePath).CompareTo(DirectoryDepth(right.RelativePath));
            return comparison != 0 ? comparison : string.Compare(left.RelativePath, right.RelativePath, StringComparison.Ordinal);
        });

        List<DetectedProject> winners = new();
        foreach (DetectedProject detection in ordered)
        {
            if (isMonoRoot && detection.RelativePath.Length == 0)
            {
                continue;
            }

            bool shadowed = false;
            foreach (DetectedProject accepted in winners)
            {
                if (IsSameOrAncestor(accepted.RelativePath, detection.RelativePath))
                {
                    shadowed = true;
                    break;
                }
            }

            if (!shadowed)
            {
                winners.Add(detection);
            }
        }

        return winners;
    }

    private static bool IsSameOrAncestor(string ancestor, string path)
    {
        return ancestor.Length == 0 || string.Equals(ancestor, path, StringComparison.Ordinal) ||
            path.StartsWith(ancestor + "/", StringComparison.Ordinal);
    }

    private static int DirectoryDepth(string path)
    {
        if (path.Length == 0)
        {
            return 0;
        }

        int depth = 1;
        foreach (char c in path)
        {
            if (c == '/')
            {
                depth++;
            }
        }

        return depth;
    }
}
