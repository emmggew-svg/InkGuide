using System;
using System.Text;
using System.Text.RegularExpressions;

namespace InkGuide.Infrastructure.Platform.Ai;

/// <summary>Strips obvious secrets from excerpts before any AI call.</summary>
public static class SecretScrubber
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex[] Patterns = new[]
    {
        new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled, MatchTimeout),
        new Regex(@"(?i)(api[_-]?key|secret|password|passwd|pwd|token)\s*[:=]\s*['""]?[^'""\s,}]+", RegexOptions.Compiled, MatchTimeout),
        new Regex(@"ya29\.[A-Za-z0-9_\-]+", RegexOptions.Compiled, MatchTimeout),
        new Regex(@"sk-ant-[A-Za-z0-9_\-]+", RegexOptions.Compiled, MatchTimeout),
        new Regex(@"1//[A-Za-z0-9_\-]+", RegexOptions.Compiled, MatchTimeout),
    };

    /// <summary>Returns the excerpt with secret-looking values replaced.</summary>
    public static string Scrub(string excerpt)
    {
        if (string.IsNullOrEmpty(excerpt))
        {
            return string.Empty;
        }

        string scrubbed = excerpt;
        foreach (Regex pattern in Patterns)
        {
            scrubbed = pattern.Replace(scrubbed, "[SCRUBBED]");
        }

        return scrubbed;
    }
}
