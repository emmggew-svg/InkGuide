using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Hosting;

/// <summary>
/// Validates a ui-override folder (MASTER_PROMPT Section 16.5). The override
/// wins only when index.html exists and ui-manifest.json declares a compatible
/// contract; otherwise the bundled UI is used and the reason is logged.
/// </summary>
internal sealed class UiOverride
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<UiOverride> _logger;
    private readonly AppPaths _paths;

    public UiOverride(ILogger<UiOverride> logger, AppPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    public UiResolution Resolve()
    {
        string overrideDir = _paths.UiOverrideDir;
        string overrideIndex = Path.Combine(overrideDir, "index.html");
        string manifestPath = Path.Combine(overrideDir, "ui-manifest.json");

        if (File.Exists(overrideIndex) && File.Exists(manifestPath))
        {
            UiOverrideValidation validation = ValidateManifest(manifestPath);
            if (validation.Ok)
            {
                AppLogMessages.UsingOverride(_logger, overrideDir);
                return new UiResolution(overrideDir, Source: "override", validation.Name, validation.Version, Status: "ok");
            }

            AppLogMessages.IgnoringOverride(_logger, validation.Reason ?? "unknown");
            return Fallback("override-invalid: " + validation.Reason);
        }

        return Fallback(File.Exists(overrideIndex) || File.Exists(manifestPath)
            ? "override-incomplete: ui-override needs both index.html and ui-manifest.json"
            : "bundled");
    }

    private static UiResolution Fallback(string status)
    {
        string bundledIndex = Path.Combine(AppPaths.UiBundledDir, "index.html");
        if (File.Exists(bundledIndex))
        {
            return new UiResolution(AppPaths.UiBundledDir, Source: "bundled", Name: null, Version: null, status);
        }

        return new UiResolution(Folder: null, Source: "missing", Name: null, Version: null, "ui-missing");
    }

    private UiOverrideValidation ValidateManifest(string manifestPath)
    {
        try
        {
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<UiManifestDto>(json, JsonOptions);
            if (manifest is null)
            {
                return UiOverrideValidation.Fail("ui-manifest.json is empty or invalid JSON.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Entry))
            {
                return UiOverrideValidation.Fail("ui-manifest.json misses the entry field.");
            }

            string entryPath = Path.Combine(_paths.UiOverrideDir, manifest.Entry);
            if (!File.Exists(entryPath))
            {
                return UiOverrideValidation.Fail("manifest entry file not found: " + manifest.Entry);
            }

            if (!IsContractCompatible(manifest.Contract))
            {
                return UiOverrideValidation.Fail(
                    "contract " + (manifest.Contract ?? "(none)") +
                    " is incompatible with host contract " +
                    AppIdentity.BridgeContractMajor + ".x.");
            }

            return UiOverrideValidation.OkResult(manifest.Name, manifest.Version);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            return UiOverrideValidation.Fail("cannot read ui-manifest.json: " + ex.Message);
        }
    }

    private static bool IsContractCompatible(string? contract)
    {
        if (string.IsNullOrWhiteSpace(contract))
        {
            return false;
        }

        string major = contract.Trim().TrimStart('v', 'V').Split('.')[0];
        return int.TryParse(major, out int majorNum) && majorNum == AppIdentity.BridgeContractMajor;
    }

    private sealed record UiOverrideValidation(bool Ok, string? Reason, string? Name, string? Version)
    {
        public static UiOverrideValidation Fail(string reason) => new(false, reason, null, null);
        public static UiOverrideValidation OkResult(string? name, string? version) => new(true, null, name, version);
    }

    private sealed class UiManifestDto
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Contract { get; set; }
        public string? Entry { get; set; }
        public string? MinHostVersion { get; set; }
    }
}

/// <summary>Outcome of UI folder resolution, exposed via app.getUiStatus.</summary>
internal sealed record UiResolution(string? Folder, string Source, string? Name, string? Version, string Status);
