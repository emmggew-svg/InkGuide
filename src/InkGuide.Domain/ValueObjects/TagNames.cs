using System;

namespace InkGuide.Domain.ValueObjects;

/// <summary>Normalisation and validation rules for tag names. Names are unique per workspace, case-insensitive.</summary>
public static class TagNames
{
    /// <summary>Maximum tag name length.</summary>
    public const int MaxLength = 128;

    /// <summary>Normalises a tag name for comparison: trimmed, upper-invariant.</summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToUpperInvariant();
    }

    /// <summary>Checks whether a tag name is usable.</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        if (trimmed.Length > MaxLength)
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Splits a namespaced tag (for example <c>type:web-site</c>) into namespace and local name.</summary>
    public static (string? Namespace, string LocalName) SplitNamespace(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string trimmed = name.Trim();
        int separator = trimmed.IndexOf(':');
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return (null, trimmed);
        }

        return (trimmed.Substring(0, separator), trimmed.Substring(separator + 1));
    }
}
