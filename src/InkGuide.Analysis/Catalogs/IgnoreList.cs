using System;
using System.Collections.Generic;
using System.Text.Json;
using InkGuide.Domain.Common;

namespace InkGuide.Analysis.Catalogs;

/// <summary>Default ignore list loaded from config/ignore-patterns.json. Data, not code.</summary>
public static class IgnoreList
{
    /// <summary>Parses ignore patterns from either {"patterns":[...]} or a bare [...] document.</summary>
    public static Result<IReadOnlyList<string>, Failure> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result.Failure<IReadOnlyList<string>>(Errors.InvalidParams($"ignore-patterns.json is not valid JSON: {ex.Message}"));
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return Result.Success<IReadOnlyList<string>>(ReadItems(root));
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure<IReadOnlyList<string>>(Errors.InvalidParams("ignore-patterns.json root must be an object or an array"));
            }

            return Result.Success(CatalogJson.ReadStringArray(root, "patterns"));
        }
    }

    private static List<string> ReadItems(JsonElement array)
    {
        List<string> items = new();
        foreach (JsonElement item in array.EnumerateArray())
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
}
