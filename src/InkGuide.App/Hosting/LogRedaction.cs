using System;
using System.Text.RegularExpressions;

namespace InkGuide.App.Hosting;

/// <summary>
/// Redacts credential-looking material from log text (Section 18).
/// Applied to exception strings and the UI-to-host log relay.
/// </summary>
internal static partial class LogRedaction
{
    private const string Mask = "***REDACTED***";

    [GeneratedRegex(@"ya29\.[A-Za-z0-9\-_]+")]
    private static partial Regex GoogleAccessToken();

    [GeneratedRegex(@"1//[A-Za-z0-9\-_]+")]
    private static partial Regex GoogleRefreshToken();

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9\-_]+")]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"(?i)(bearer\s+)[A-Za-z0-9\-_\.=]+")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?i)(x-api-key""?\s*[:=]\s*""?)[^""'\s,}]+")]
    private static partial Regex ApiKeyHeader();

    [GeneratedRegex(@"(?i)(client_secret""?\s*[:=]\s*""?)[^""'\s,}]+")]
    private static partial Regex ClientSecret();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        text = GoogleAccessToken().Replace(text, Mask);
        text = GoogleRefreshToken().Replace(text, Mask);
        text = AnthropicKey().Replace(text, Mask);
        text = BearerToken().Replace(text, "$1" + Mask);
        text = ApiKeyHeader().Replace(text, "$1" + Mask);
        text = ClientSecret().Replace(text, "$1" + Mask);
        return text;
    }
}
