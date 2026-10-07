using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using InkGuide.Domain.Common;

namespace InkGuide.Analysis.Catalogs;

/// <summary>One file-role rule: glob patterns plus optional content predicates and a summary template.</summary>
/// <param name="Id">Stable role id (for example <c>readme</c>).</param>
/// <param name="Patterns">Glob patterns matched against the file name or relative path.</param>
/// <param name="Priority">Higher wins when several rules match. Default 50.</param>
/// <param name="SummaryTemplate">Summary with {slots} filled from inspected facts.</param>
/// <param name="IsSensitive">Matches are always classified Sensitive.</param>
/// <param name="IsGenerated">Matches are generated or vendored content.</param>
/// <param name="ContentContains">All entries must occur in the text preview for the rule to match.</param>
public sealed record RoleRule(
    string Id,
    IReadOnlyList<string> Patterns,
    int Priority,
    string SummaryTemplate,
    bool IsSensitive,
    bool IsGenerated,
    IReadOnlyList<string> ContentContains);

/// <summary>File-role catalog loaded from config/file-roles.json. Data, not code.</summary>
public sealed class FileRoleCatalog
{
    /// <summary>Catalog schema version.</summary>
    public int Version { get; }

    /// <summary>Ruleset version stamped on insights produced with this catalog.</summary>
    public string RulesetVersion { get; }

    /// <summary>Role rules in catalog order (tie-break for equal priority).</summary>
    public IReadOnlyList<RoleRule> Rules { get; }

    /// <summary>Creates a catalog. Rules must not be null.</summary>
    public FileRoleCatalog(int version, string rulesetVersion, IReadOnlyList<RoleRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rulesetVersion);
        ArgumentNullException.ThrowIfNull(rules);
        Version = version;
        RulesetVersion = rulesetVersion;
        Rules = rules;
    }

    /// <summary>Parses and validates file-roles.json content.</summary>
    public static Result<FileRoleCatalog, Failure> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result.Failure<FileRoleCatalog>(Errors.InvalidParams($"file-roles.json is not valid JSON: {ex.Message}"));
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure<FileRoleCatalog>(Errors.InvalidParams("file-roles.json root must be an object"));
            }

            int version = CatalogJson.TryGetInt(root, "version", 1);
            string rulesetVersion = CatalogJson.TryGetString(root, "rulesetVersion")
                ?? CatalogJson.TryGetString(root, "ruleset_version")
                ?? version.ToString(CultureInfo.InvariantCulture);

            JsonElement rulesElement;
            if (root.TryGetProperty("roles", out JsonElement roles) && roles.ValueKind == JsonValueKind.Array)
            {
                rulesElement = roles;
            }
            else if (root.TryGetProperty("rules", out JsonElement rulesProperty) && rulesProperty.ValueKind == JsonValueKind.Array)
            {
                rulesElement = rulesProperty;
            }
            else
            {
                return Result.Failure<FileRoleCatalog>(Errors.InvalidParams("file-roles.json needs a 'roles' array"));
            }

            List<RoleRule> rules = new();
            foreach (JsonElement item in rulesElement.EnumerateArray())
            {
                Result<RoleRule, Failure> parsed = ParseRule(item);
                if (parsed.IsFailure)
                {
                    ArgumentNullException.ThrowIfNull(parsed.Error);
                    return Result.Failure<FileRoleCatalog>(parsed.Error);
                }

                ArgumentNullException.ThrowIfNull(parsed.Value);
                rules.Add(parsed.Value);
            }

            return Result.Success(new FileRoleCatalog(version, rulesetVersion, rules));
        }
    }

    private static Result<RoleRule, Failure> ParseRule(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<RoleRule>(Errors.InvalidParams("file-roles.json role entries must be objects"));
        }

        string? id = CatalogJson.TryGetString(item, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return Result.Failure<RoleRule>(Errors.InvalidParams("file-roles.json role entry needs a non-empty 'id'"));
        }

        IReadOnlyList<string> patterns = CatalogJson.ReadStringOrArray(item, "patterns");
        if (patterns.Count == 0)
        {
            return Result.Failure<RoleRule>(Errors.InvalidParams($"file-roles.json role '{id}' needs at least one pattern"));
        }

        int priority = CatalogJson.TryGetInt(item, "priority", 50);
        string template = CatalogJson.TryGetString(item, "summaryTemplate")
            ?? CatalogJson.TryGetString(item, "summary_template")
            ?? string.Empty;
        bool sensitive = CatalogJson.TryGetBool(item, "sensitive", defaultValue: false);
        bool generated = CatalogJson.TryGetBool(item, "generated", defaultValue: false);
        IReadOnlyList<string> contains = CatalogJson.ReadStringOrArray(item, "contentContains");

        return Result.Success(new RoleRule(
            id.Trim(),
            patterns,
            priority,
            template,
            sensitive,
            generated,
            contains));
    }
}
