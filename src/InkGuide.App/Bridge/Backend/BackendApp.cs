using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Analysis.Catalogs;
using Microsoft.Extensions.Logging;

using InkGuide.App.Hosting;
namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    private static readonly string[] OwnedCapabilities = new[]
    {
        "virtualHosts", "dragDropPaths", "uiOverride", "devUrl", "localProvider",
    };

    private static readonly string[] RuleCatalogNames = new[]
    {
        "project-markers", "file-roles", "kind-map", "ignore-patterns",
    };

    private static readonly HashSet<string> KnownSettingKeys = new(StringComparer.Ordinal)
    {
        BackendSettingsService.Keys.Language,
        BackendSettingsService.Keys.ActiveAccountId,
        BackendSettingsService.Keys.DriveScopeMode,
        BackendSettingsService.Keys.SyncPollFocusedSeconds,
        BackendSettingsService.Keys.SyncPollBackgroundSeconds,
        BackendSettingsService.Keys.SyncMaxParallel,
        BackendSettingsService.Keys.SyncPaused,
        BackendSettingsService.Keys.CacheLimitBytes,
        BackendSettingsService.Keys.AnalysisAuto,
        BackendSettingsService.Keys.AnalysisReadBytes,
        BackendSettingsService.Keys.AnalysisConfidenceThreshold,
        BackendSettingsService.Keys.NamingPolicy,
        BackendSettingsService.Keys.ConfirmPermanentDelete,
        BackendSettingsService.Keys.ConfirmExecutableOpen,
        BackendSettingsService.Keys.OrganizerAuto,
        BackendSettingsService.Keys.OrganizerThreshold,
        BackendSettingsService.Keys.EditorAutosave,
        BackendSettingsService.Keys.EditorMaxInlineBytes,
        BackendSettingsService.Keys.SnapshotDefaultMode,
        BackendSettingsService.Keys.ActivityRetentionDays,
        BackendSettingsService.Keys.ActivityMaxEntries,
        BackendSettingsService.Keys.AiEnabled,
        BackendSettingsService.Keys.AiModel,
        BackendSettingsService.Keys.AiBudgetPerDay,
        BackendSettingsService.Keys.AiConsentAt,
        BackendSettingsService.Keys.DevTools,
        BackendSettingsService.Keys.DefaultContextMenu,
        BackendSettingsService.Keys.AcceleratorKeys,
        BackendSettingsService.Keys.LocalProviderEnabled,
        BackendSettingsService.Keys.LogLevel,
    };

    public async Task<Result<HelloResponse>> HelloAsync(HelloRequest request, CancellationToken cancellationToken)
    {
        if (request.Major != AppIdentity.BridgeContractMajor)
        {
            return Result.Fail<HelloResponse>(new OperationError(
                "UI_INCOMPATIBLE",
                "Contract major mismatch: UI needs " + request.Major + ", host serves " + AppIdentity.BridgeContractMajor + ".",
                false,
                null));
        }

        string localFlag = await _settings.GetAsync(BackendSettingsService.Keys.LocalProviderEnabled, cancellationToken).ConfigureAwait(false);
        bool localOn = _options.LocalProviderEnabled || string.Equals(localFlag, "true", StringComparison.OrdinalIgnoreCase);
        var response = new HelloResponse(
            new ContractInfo(AppIdentity.BridgeContractMajor, AppIdentity.BridgeContractMinor),
            _host.HostVersion,
            OwnedCapabilities,
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["localProvider"] = localOn,
                ["ai"] = false,
            },
            "{}",
            _host.UiStatus);
        AppLogMessages.HandshakeServed(_logger, AppIdentity.BridgeContractMajor, AppIdentity.BridgeContractMinor);
        return Result.Ok(response);
    }

    public Task<Result<AppInfo>> GetInfoAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(Result.Ok(new AppInfo(AppIdentity.AppName, _host.HostVersion, AppIdentity.AppId, _options.DataDirectory)));
    }

    public Task<Result<UiStatus>> GetUiStatusAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(Result.Ok(new UiStatus(_host.UiSource, _host.UiName, _host.UiStatus)));
    }

    public Task<Result<EmptyResult>> QuitAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _host.Quit();
        return Task.FromResult(Result.Ok(new EmptyResult()));
    }

    public Task<Result<EmptyResult>> RestartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _host.Restart();
        return Task.FromResult(Result.Ok(new EmptyResult()));
    }

    public Task<Result<EmptyResult>> OpenLogsFolderAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.LogsDirectory,
                UseShellExecute = true,
            };
            _ = Process.Start(startInfo);
            return Task.FromResult(Result.Ok(new EmptyResult()));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(Result.Fail<EmptyResult>(BackendResults.Internal("Could not open the logs folder.")));
        }
    }

    public Task<Result<EmptyResult>> OpenExternalUrlAsync(string url, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && !string.Equals(uri.Scheme, "mailto", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Result.Fail<EmptyResult>(BackendResults.InvalidParams("Only http, https and mailto URLs can be opened externally.")));
        }

        _host.OpenExternalUrl(url);
        return Task.FromResult(Result.Ok(new EmptyResult()));
    }

    public async Task<Result<EmptyResult>> SetBackgroundAsync(SetBackgroundRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!HexColor().IsMatch(request.Color))
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("color must be a hex value like #RRGGBB."));
        }

        await _host.SetBackgroundAsync(request.Color).ConfigureAwait(false);
        _ = cancellationToken;
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> SetTitleAsync(SetTitleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Title) || request.Title.Length > 200)
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("title must be 1-200 characters."));
        }

        await _host.SetTitleAsync(request.Title).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<SettingsDto>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> values = await _settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new SettingsDto(values, BackendSettingsService.SchemaVersion));
    }

    public async Task<Result<SettingsDto>> UpdateSettingsAsync(UpdateSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<string> unknown = request.Values.Keys.Where(k => !KnownSettingKeys.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            return Result.Fail<SettingsDto>(BackendResults.InvalidParams("Unknown settings keys: " + string.Join(", ", unknown) + "."));
        }

        foreach (KeyValuePair<string, string> kv in request.Values)
        {
            await _settings.SetAsync(kv.Key, kv.Value ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        // Local-provider toggle takes effect on next provider resolution (no restart).
        IReadOnlyDictionary<string, string> values = await _settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new SettingsDto(values, BackendSettingsService.SchemaVersion));
    }

    public async Task<Result<SettingsDto>> ResetSettingsAsync(CancellationToken cancellationToken)
    {
        await _settings.ResetAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> values = await _settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new SettingsDto(values, BackendSettingsService.SchemaVersion));
    }

    public async Task<Result<string?>> GetUiStateAsync(string key, CancellationToken cancellationToken)
    {
        string? value = await _settings.GetUiStateAsync(key, cancellationToken).ConfigureAwait(false);
        return Result.Ok<string?>(value);
    }

    public async Task<Result<EmptyResult>> SetUiStateAsync(UiStateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _settings.SetUiStateAsync(request.Key, request.ValueJson, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<ExportBundleResponse>> ExportBundleAsync(CancellationToken cancellationToken)
    {
        string bundleDir = Path.Combine(Path.GetTempPath(), "inkguide-bundle-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(bundleDir);
        try
        {
            if (Directory.Exists(_options.LogsDirectory))
            {
                foreach (string log in Directory.GetFiles(_options.LogsDirectory, "*.log"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string[] lines = await File.ReadAllLinesAsync(log, cancellationToken).ConfigureAwait(false);
                    var redacted = new List<string>(lines.Length);
                    foreach (string line in lines)
                    {
                        redacted.Add(Infrastructure.Platform.Secrets.LogRedactor.Redact(line));
                    }

                    await File.WriteAllLinesAsync(Path.Combine(bundleDir, Path.GetFileName(log)), redacted, cancellationToken).ConfigureAwait(false);
                }
            }

            IReadOnlyDictionary<string, string> settings = await _settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
            string settingsJson = Infrastructure.Platform.Secrets.LogRedactor.Redact(JsonSerializer.Serialize(settings, JsonOptions));
            await File.WriteAllTextAsync(Path.Combine(bundleDir, "settings.json"), settingsJson, cancellationToken).ConfigureAwait(false);
            string sysinfo = "app=" + AppIdentity.AppName + " version=" + _host.HostVersion
                + " os=" + Environment.OSVersion + " clr=" + Environment.Version
                + " utc=" + UtcNow();
            await File.WriteAllTextAsync(Path.Combine(bundleDir, "system.txt"), sysinfo, cancellationToken).ConfigureAwait(false);

            string zipPath = Path.Combine(Path.GetTempPath(), "inkguide-bundle-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture) + ".zip");
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            System.IO.Compression.ZipFile.CreateFromDirectory(bundleDir, zipPath);
            return Result.Ok(new ExportBundleResponse(zipPath));
        }
        finally
        {
            try
            {
                Directory.Delete(bundleDir, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public async Task<Result<HealthDto>> GetHealthAsync(CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            _ = await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            checks["database"] = "ok";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            checks["database"] = "error: " + ex.GetType().Name;
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (resolved.IsSuccess && resolved.Value is not null)
        {
            var caps = await resolved.Value.Provider.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            checks["provider"] = caps.IsSuccess ? "ok:" + resolved.Value.Provider.ProviderId : "error";
        }
        else
        {
            checks["provider"] = "unavailable";
        }

        var stats = await _cache.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        checks["cache"] = stats.IsSuccess ? "ok" : "error";
        checks["rules"] = _holder.Current.DetectorCount + " detectors";

        string state = checks.Values.Any(v => v.StartsWith("error", StringComparison.Ordinal)) ? "degraded" : "healthy";
        return Result.Ok(new HealthDto(state, checks));
    }

    public Task<Result<EmptyResult>> RelayLogAsync(LogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = cancellationToken;
        string message = "UI: " + (request.Message ?? string.Empty);
        switch ((request.Level ?? "info").ToLowerInvariant())
        {
            case "warn":
            case "warning":
                AppLogMessages.UiLogWarn(_logger, message);
                break;
            case "error":
                AppLogMessages.UiLogError(_logger, message);
                break;
            case "debug":
                AppLogMessages.UiLogDebug(_logger, message);
                break;
            default:
                AppLogMessages.UiLogInfo(_logger, message);
                break;
        }

        return Task.FromResult(Result.Ok(new EmptyResult()));
    }

    public async Task<Result<StorageUsageDto>> GetStorageUsageAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<StorageUsageDto>(resolved.Failure!);
        }

        var quota = await resolved.Value.Provider.GetQuotaAsync(cancellationToken).ConfigureAwait(false);
        if (!quota.IsSuccess || quota.Value is null)
        {
            return Result.Fail<StorageUsageDto>(BackendResults.ToBridge(quota.Failure!));
        }

        return Result.Ok(new StorageUsageDto(quota.Value.LimitBytes, quota.Value.UsageBytes));
    }

    public async Task<Result<CacheStatsDto>> GetCacheStatsAsync(CancellationToken cancellationToken)
    {
        var stats = await _cache.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        if (!stats.IsSuccess || stats.Value is null)
        {
            return Result.Fail<CacheStatsDto>(BackendResults.ToBridge(stats.Failure!));
        }

        return Result.Ok(new CacheStatsDto(stats.Value.FileCount, stats.Value.TotalBytes, stats.Value.MaxBytes));
    }

    public async Task<Result<EmptyResult>> ClearCacheAsync(CancellationToken cancellationToken)
    {
        var cleared = await _cache.ClearAsync(cancellationToken).ConfigureAwait(false);
        if (!cleared.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(cleared.Failure!));
        }

        return Result.Ok(new EmptyResult());
    }

    public Task<Result<CatalogInfoDto>> GetCatalogInfoAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        AnalysisCatalogs current = _holder.Current;
        return Task.FromResult(Result.Ok(new CatalogInfoDto(current.RulesetVersion, current.DetectorCount, current.RoleCount, current.KindCount)));
    }

    public Task<Result<EmptyResult>> ReloadRulesAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        try
        {
            _ = _holder.Reload();
            return Task.FromResult(Result.Ok(new EmptyResult()));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            return Task.FromResult(Result.Fail<EmptyResult>(BackendResults.Internal("Rule reload failed: " + ex.Message)));
        }
    }

    public Task<Result<RulesValidationDto>> ValidateRulesAsync(ValidateRulesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(request.CatalogJson))
        {
            return Task.FromResult(Result.Ok(new RulesValidationDto(false, new List<string> { "CatalogJson is empty." })));
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(request.CatalogJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("catalog", out JsonElement kind) && kind.ValueKind == JsonValueKind.String &&
                root.TryGetProperty("document", out JsonElement document))
            {
                return Task.FromResult(ValidateOne((kind.GetString() ?? string.Empty).ToLowerInvariant(), document.GetRawText()));
            }

            // Bare document: try each catalog, report the first clean parse.
            var errors = new List<string>();
            foreach (string name in RuleCatalogNames)
            {
                Result<RulesValidationDto> attempt = ValidateOne(name, request.CatalogJson);
                if (attempt.IsSuccess && attempt.Value is not null && attempt.Value.Valid)
                {
                    return Task.FromResult(attempt);
                }

                if (attempt.Value is not null)
                {
                    errors.AddRange(attempt.Value.Errors);
                }
            }

            return Task.FromResult(Result.Ok(new RulesValidationDto(false, errors)));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(Result.Ok(new RulesValidationDto(false, new List<string> { "Not valid JSON: " + ex.Message })));
        }
    }

    private static Result<RulesValidationDto> ValidateOne(string name, string json)
    {
        try
        {
            switch (name)
            {
                case "project-markers":
                    var markers = ProjectMarkerCatalog.Parse(json);
                    return markers.IsSuccess
                        ? Result.Ok(new RulesValidationDto(true, Array.Empty<string>()))
                        : Result.Ok(new RulesValidationDto(false, new List<string> { markers.Error?.Message ?? "invalid" }));
                case "file-roles":
                    var roles = FileRoleCatalog.Parse(json);
                    return roles.IsSuccess
                        ? Result.Ok(new RulesValidationDto(true, Array.Empty<string>()))
                        : Result.Ok(new RulesValidationDto(false, new List<string> { roles.Error?.Message ?? "invalid" }));
                case "kind-map":
                    var kinds = KindMapCatalog.Parse(json);
                    return kinds.IsSuccess
                        ? Result.Ok(new RulesValidationDto(true, Array.Empty<string>()))
                        : Result.Ok(new RulesValidationDto(false, new List<string> { kinds.Error?.Message ?? "invalid" }));
                case "ignore-patterns":
                case "ignore":
                    var ignore = IgnoreList.Parse(json);
                    return ignore.IsSuccess
                        ? Result.Ok(new RulesValidationDto(true, Array.Empty<string>()))
                        : Result.Ok(new RulesValidationDto(false, new List<string> { ignore.Error?.Message ?? "invalid" }));
                default:
                    return Result.Ok(new RulesValidationDto(false, new List<string> { "Unknown catalog '" + name + "'. Use project-markers, file-roles, kind-map or ignore-patterns." }));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return Result.Ok(new RulesValidationDto(false, new List<string> { ex.Message }));
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();
}
