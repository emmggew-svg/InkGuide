using System;
using System.Collections.Generic;
using System.Text.Json;

namespace InkGuide.Analysis.Catalogs;

/// <summary>Shared tolerant JSON readers for the shipped data catalogs. Unknown fields are ignored.</summary>
internal static class CatalogJson
{
    internal static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    internal static int TryGetInt(JsonElement element, string propertyName, int defaultValue)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return defaultValue;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return defaultValue;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed) ? parsed : defaultValue;
    }

    internal static bool TryGetBool(JsonElement element, string propertyName, bool defaultValue)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return defaultValue;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return defaultValue;
        }

        return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;
    }

    internal static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        List<string> items = new();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(text.Trim());
                }
            }
        }

        return items;
    }

    internal static IReadOnlyList<string> ReadStringOrArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            return string.IsNullOrWhiteSpace(text)
                ? Array.Empty<string>()
                : new[] { text.Trim() };
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return ReadStringArray(element, propertyName);
        }

        return Array.Empty<string>();
    }
}
