using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace InkGuide.Infrastructure.Storage.GoogleDrive;

/// <summary>Compact machine facts stored in appProperties (tiny, private, survive moves).</summary>
internal static class DriveAppProperties
{
    /// <summary>Builds the appProperties map for write-back.</summary>
    public static Dictionary<string, string> Build(string? kind, string? source, string? description, string? contentHash, string? operationId)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(kind))
        {
            map[DriveConstants.AppKeys.Kind] = kind!;
        }

        if (!string.IsNullOrEmpty(source))
        {
            map[DriveConstants.AppKeys.Source] = source!;
        }

        if (description is not null)
        {
            map[DriveConstants.AppKeys.DescriptionHash] = ComputeShortHash(description);
        }

        if (!string.IsNullOrEmpty(contentHash))
        {
            map[DriveConstants.AppKeys.ContentHash] = contentHash!;
        }

        if (!string.IsNullOrEmpty(operationId))
        {
            map[DriveConstants.AppKeys.OperationId] = operationId!;
        }

        return map;
    }

    /// <summary>Short hash (16 hex chars of SHA-256) for description/content identity.</summary>
    public static string ComputeShortHash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLower(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// User-edit detection: when the remote description hash differs from the stored
    /// inkguide.dh, the description is user-authored and must never be auto-overwritten.
    /// </summary>
    public static bool IsUserEdited(string? remoteDescription, IReadOnlyDictionary<string, string>? appProperties)
    {
        if (remoteDescription is null || appProperties is null)
        {
            return false;
        }

        if (!appProperties.TryGetValue(DriveConstants.AppKeys.DescriptionHash, out string? stored))
        {
            return true;
        }

        return !string.Equals(ComputeShortHash(remoteDescription), stored, StringComparison.Ordinal);
    }
}
