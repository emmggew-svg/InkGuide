using System;
using System.Collections.Generic;
using InkGuide.Analysis.Catalogs;

namespace InkGuide.Analysis.Sensitivity;

/// <summary>Sensitivity detection outcome.</summary>
/// <param name="IsSensitive">True when the node looks like secret or credential material.</param>
/// <param name="Reasons">Short machine-readable reason codes (for example <c>name:.env*</c>).</param>
public sealed record SensitivityFinding(bool IsSensitive, IReadOnlyList<string> Reasons);

/// <summary>
/// Secret and credential heuristics over names and bounded text previews.
/// Sensitive nodes are excluded from content indexing, log excerpts and AI.
/// </summary>
public static class SensitivityDetector
{
    /// <summary>Maximum reasons reported per node.</summary>
    public const int MaxReasons = 4;

    /// <summary>Maximum preview characters scanned for content signals.</summary>
    public const int MaxPreviewChars = 32768;

    private static readonly string[] SensitiveNamePatterns =
    [
        ".env*",
        "*.pem",
        "*.key",
        "*.pfx",
        "*.p12",
        "*.keystore",
        "id_rsa*",
        "id_ed25519*",
        "id_ecdsa*",
        "credentials*.json",
        "client_secret*.json",
        "*token*.json",
        "secret*",
        "*.kdbx",
        ".npmrc",
        ".pypirc",
    ];

    private static readonly string[] KeyMaterialNamePatterns =
    [
        "*.pem",
        "*.key",
        "*.pfx",
        "*.p12",
        "*.keystore",
        "id_rsa*",
        "id_ed25519*",
        "id_ecdsa*",
    ];

    private static readonly string[] SecretContentMarkers =
    [
        "PRIVATE KEY-----",
        "sk-ant-",
        "ya29.",
        "ghp_",
        "gho_",
        "ghu_",
        "github_pat_",
        "xoxb-",
        "xoxp-",
        "AKIA",
        "AIza",
        "1//0",
    ];

    /// <summary>Detects sensitivity from the file name and an optional bounded text preview.</summary>
    public static SensitivityFinding Detect(string fileName, string? textPreview)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        List<string> reasons = new();
        string baseName = GlobMatcher.GetFileName(fileName);
        foreach (string pattern in SensitiveNamePatterns)
        {
            if (GlobMatcher.MatchFileOrPath(pattern, baseName, null))
            {
                reasons.Add("name:" + pattern);
                break;
            }
        }

        if (!string.IsNullOrEmpty(textPreview))
        {
            string preview = textPreview.Length > MaxPreviewChars
                ? textPreview.Substring(0, MaxPreviewChars)
                : textPreview;
            ScanContent(preview, reasons);
        }

        return new SensitivityFinding(reasons.Count > 0, reasons);
    }

    /// <summary>Checks whether a file name looks like key material (drives the certificate-or-key kind).</summary>
    public static bool LooksLikeKeyMaterial(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string baseName = GlobMatcher.GetFileName(fileName);
        foreach (string pattern in KeyMaterialNamePatterns)
        {
            if (GlobMatcher.MatchFileOrPath(pattern, baseName, null))
            {
                return true;
            }
        }

        return false;
    }

    private static void ScanContent(string preview, List<string> reasons)
    {
        foreach (string marker in SecretContentMarkers)
        {
            if (reasons.Count >= MaxReasons)
            {
                return;
            }

            if (preview.Contains(marker, StringComparison.Ordinal))
            {
                reasons.Add("content:" + marker);
            }
        }

        if (reasons.Count < MaxReasons && ContainsHighEntropyToken(preview))
        {
            reasons.Add("content:high-entropy-token");
        }
    }

    private static bool ContainsHighEntropyToken(string preview)
    {
        int runStart = -1;
        for (int i = 0; i <= preview.Length; i++)
        {
            bool tokenChar = i < preview.Length && IsTokenChar(preview[i]);
            if (tokenChar && runStart < 0)
            {
                runStart = i;
            }

            if (!tokenChar && runStart >= 0)
            {
                int length = i - runStart;
                if (length >= 24 && ShannonEntropy(preview, runStart, length) >= 4.5)
                {
                    return true;
                }

                runStart = -1;
            }
        }

        return false;
    }

    private static bool IsTokenChar(char c)
    {
        return (c >= 'a' && c <= 'z') ||
            (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') ||
            c == '_' || c == '-' || c == '+' || c == '/' || c == '=';
    }

    private static double ShannonEntropy(string text, int start, int length)
    {
        int[] counts = new int[128];
        int ascii = 0;
        for (int i = start; i < start + length; i++)
        {
            char c = text[i];
            if (c < 128)
            {
                counts[c]++;
                ascii++;
            }
        }

        if (ascii == 0)
        {
            return 0.0;
        }

        double entropy = 0.0;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            double probability = (double)counts[i] / ascii;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }
}
