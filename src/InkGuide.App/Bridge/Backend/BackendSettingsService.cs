using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging;

using InkGuide.App.Hosting;
namespace InkGuide.App.Bridge.Backend;

/// <summary>
/// Schema-versioned settings over the SettingsKv table (MASTER_PROMPT Sec 17).
/// Precedence defaults &lt; settings.json-equivalent rows &lt; env &lt; CLI is
/// honored at composition time; this service owns the persisted layer.
/// </summary>
public sealed class BackendSettingsService
{
    public const int SchemaVersion = 1;

    public static class Keys
    {
        public const string Language = "general.language";
        public const string ActiveAccountId = "accounts.activeId";
        public const string DriveScopeMode = "advanced.driveScopeMode";
        public const string SyncPollFocusedSeconds = "sync.pollFocusedSeconds";
        public const string SyncPollBackgroundSeconds = "sync.pollBackgroundSeconds";
        public const string SyncMaxParallel = "sync.maxParallel";
        public const string SyncPaused = "sync.paused";
        public const string CacheLimitBytes = "cache.limitBytes";
        public const string AnalysisAuto = "analysis.autoAnalyze";
        public const string AnalysisReadBytes = "analysis.readBytes";
        public const string AnalysisConfidenceThreshold = "analysis.confidenceThreshold";
        public const string NamingPolicy = "naming.policy";
        public const string ConfirmPermanentDelete = "safety.confirmPermanentDelete";
        public const string ConfirmExecutableOpen = "safety.confirmExecutableOpen";
        public const string OrganizerAuto = "organizer.autoOrganize";
        public const string OrganizerThreshold = "organizer.threshold";
        public const string EditorAutosave = "editor.autosave";
        public const string EditorMaxInlineBytes = "editor.maxInlineBytes";
        public const string SnapshotDefaultMode = "snapshots.defaultMode";
        public const string ActivityRetentionDays = "activity.retentionDays";
        public const string ActivityMaxEntries = "activity.maxEntries";
        public const string AiEnabled = "ai.enabled";
        public const string AiModel = "ai.model";
        public const string AiBudgetPerDay = "ai.budgetPerDay";
        public const string AiConsentAt = "ai.consentAt";
        public const string DevTools = "advanced.devtools";
        public const string DefaultContextMenu = "advanced.defaultContextMenu";
        public const string AcceleratorKeys = "advanced.acceleratorKeys";
        public const string LocalProviderEnabled = "advanced.localProvider";
        public const string LogLevel = "privacy.logLevel";
    }

    private static readonly IReadOnlyDictionary<string, string> Defaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Keys.Language] = "tr-TR",
            [Keys.ActiveAccountId] = string.Empty,
            [Keys.DriveScopeMode] = "AppFolderOnly",
            [Keys.SyncPollFocusedSeconds] = "30",
            [Keys.SyncPollBackgroundSeconds] = "300",
            [Keys.SyncMaxParallel] = "4",
            [Keys.SyncPaused] = "false",
            [Keys.CacheLimitBytes] = "2147483648",
            [Keys.AnalysisAuto] = "true",
            [Keys.AnalysisReadBytes] = "262144",
            [Keys.AnalysisConfidenceThreshold] = "0.6",
            [Keys.NamingPolicy] = "block",
            [Keys.ConfirmPermanentDelete] = "true",
            [Keys.ConfirmExecutableOpen] = "true",
            [Keys.OrganizerAuto] = "false",
            [Keys.OrganizerThreshold] = "0.8",
            [Keys.EditorAutosave] = "false",
            [Keys.EditorMaxInlineBytes] = "5242880",
            [Keys.SnapshotDefaultMode] = "pin",
            [Keys.ActivityRetentionDays] = "90",
            [Keys.ActivityMaxEntries] = "10000",
            [Keys.AiEnabled] = "false",
            [Keys.AiModel] = "claude-haiku-5-5",
            [Keys.AiBudgetPerDay] = "500",
            [Keys.AiConsentAt] = string.Empty,
            [Keys.DevTools] = "false",
            [Keys.DefaultContextMenu] = "false",
            [Keys.AcceleratorKeys] = "false",
            [Keys.LocalProviderEnabled] = "false",
            [Keys.LogLevel] = "Information",
        };

    private readonly IMiscStore _store;
    private readonly ILogger<BackendSettingsService> _logger;

    public BackendSettingsService(IMiscStore store, ILogger<BackendSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, string>(Defaults, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> kv in Defaults)
        {
            string? stored = await GetRawAsync(kv.Key, cancellationToken).ConfigureAwait(false);
            if (stored is not null)
            {
                merged[kv.Key] = stored;
            }
        }

        return merged;
    }

    public async Task<string> GetAsync(string key, CancellationToken cancellationToken)
    {
        string? stored = await GetRawAsync(key, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            return stored;
        }

        return Defaults.TryGetValue(key, out string? fallback) ? fallback : string.Empty;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        var row = await _store.SetSettingAsync("settings:" + key, JsonSerializer.Serialize(value), cancellationToken).ConfigureAwait(false);
        if (!row.IsSuccess)
        {
            AppLogMessages.BackendNote(_logger, "Could not persist setting " + key + ".");
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        foreach (string key in Defaults.Keys.ToArray())
        {
            await SetAsync(key, Defaults[key], cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string?> GetUiStateAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var row = await _store.GetSettingAsync("uistate:" + key, cancellationToken).ConfigureAwait(false);
        if (!row.IsSuccess || row.Value is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(row.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SetUiStateAsync(string key, string? valueJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = await _store.SetSettingAsync(
            "uistate:" + key,
            valueJson ?? "null",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> GetRawAsync(string key, CancellationToken cancellationToken)
    {
        var row = await _store.GetSettingAsync("settings:" + key, cancellationToken).ConfigureAwait(false);
        if (!row.IsSuccess || row.Value is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(row.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
