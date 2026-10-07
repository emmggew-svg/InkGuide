using System;

namespace InkGuide.Analysis.Catalogs;

/// <summary>
/// Glob matching for catalog patterns. '*' matches within one path segment,
/// '**' spans segments, '?' matches one character. Matching is case-insensitive.
/// Patterns without '/' match against the file name; patterns with '/' match
/// the whole '/'-separated relative path.
/// </summary>
public static class GlobMatcher
{
    /// <summary>Normalises separators to '/' and strips a leading './'.</summary>
    public static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(2);
        }

        return normalized;
    }

    /// <summary>Returns the text after the last '/' or '\'.</summary>
    public static string GetFileName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string normalized = NormalizePath(path);
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized.Substring(slash + 1);
    }

    /// <summary>Matches a pattern against a whole '/'-separated relative path.</summary>
    public static bool MatchPath(string pattern, string path)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(path);
        string[] patternSegments = NormalizePath(pattern).Split('/');
        string[] pathSegments = NormalizePath(path).Split('/');
        return MatchSegments(patternSegments, 0, pathSegments, 0);
    }

    /// <summary>
    /// Matches a role-style pattern: without '/' it matches the file name at any
    /// depth, with '/' it matches the whole relative path.
    /// </summary>
    public static bool MatchFileOrPath(string pattern, string fileName, string? relativePath)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(fileName);
        if (pattern.Contains('/'))
        {
            string target = relativePath is null ? GetFileName(fileName) : NormalizePath(relativePath);
            return MatchPath(pattern, target);
        }

        return MatchSegment(pattern, GetFileName(fileName));
    }

    private static bool MatchSegments(string[] pattern, int patternIndex, string[] path, int pathIndex)
    {
        while (patternIndex < pattern.Length && pathIndex < path.Length)
        {
            if (string.Equals(pattern[patternIndex], "**", StringComparison.Ordinal))
            {
                for (int skip = pathIndex; skip <= path.Length; skip++)
                {
                    if (MatchSegments(pattern, patternIndex + 1, path, skip))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (!MatchSegment(pattern[patternIndex], path[pathIndex]))
            {
                return false;
            }

            patternIndex++;
            pathIndex++;
        }

        while (patternIndex < pattern.Length && string.Equals(pattern[patternIndex], "**", StringComparison.Ordinal))
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length && pathIndex == path.Length;
    }

    private static bool MatchSegment(string pattern, string text)
    {
        int patternIndex = 0;
        int textIndex = 0;
        int starIndex = -1;
        int resumeIndex = 0;
        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' ||
                 char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(text[textIndex])))
            {
                patternIndex++;
                textIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex;
                resumeIndex = textIndex;
                patternIndex++;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                resumeIndex++;
                textIndex = resumeIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
