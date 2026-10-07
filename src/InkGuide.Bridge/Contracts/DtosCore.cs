using System.Collections.Generic;

namespace InkGuide.Bridge.Contracts;

/// <summary>Contract version (handshake compatibility: same major, host minor ≥ UI minor).</summary>
public static class BridgeContractVersion
{
    public const int Major = 1;
    public const int Minor = 0;
}

/// <summary>One page of results with an opaque cursor.</summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Pagination input (cursor + limit, default 200).</summary>
public record PagedQuery(string? Cursor, int Limit)
{
    public int SafeLimit => Limit <= 0 ? 200 : System.Math.Min(Limit, 1000);
}

/// <summary>Mutation options accepted by mutating calls.</summary>
public record MutationOptions(string? IdempotencyKey, string? ConfirmationToken);

/// <summary>app.hello request (UI declares the contract it needs).</summary>
public sealed record HelloRequest(int Major, int Minor);

/// <summary>app.hello response.</summary>
public sealed record HelloResponse(
    ContractInfo Contract,
    string HostVersion,
    IReadOnlyList<string> Capabilities,
    IReadOnlyDictionary<string, bool> Features,
    string SettingsSnapshot,
    string UiStatus);

/// <summary>Contract compatibility info.</summary>
public sealed record ContractInfo(int Major, int Minor);

/// <summary>app.getInfo response.</summary>
public sealed record AppInfo(string Name, string Version, string AppId, string DataDir);

/// <summary>app.getUiStatus response.</summary>
public sealed record UiStatus(string Mode, string? OverrideName, string? FallbackReason);

/// <summary>window.setBackground request.</summary>
public sealed record SetBackgroundRequest(string Color);

/// <summary>window.setTitle request.</summary>
public sealed record SetTitleRequest(string Title);

/// <summary>settings.get / settings.update payloads.</summary>
public sealed record SettingsDto(IReadOnlyDictionary<string, string> Values, int SchemaVersion);

/// <summary>settings.update request.</summary>
public sealed record UpdateSettingsRequest(IReadOnlyDictionary<string, string> Values, string? IdempotencyKey);

/// <summary>settings.getUiState / setUiState payloads.</summary>
public sealed record UiStateRequest(string Key, string? ValueJson);

/// <summary>storage.getUsage response.</summary>
public sealed record StorageUsageDto(long? LimitBytes, long UsageBytes);

/// <summary>storage.getCacheStats response.</summary>
public sealed record CacheStatsDto(int FileCount, long TotalBytes, long MaxBytes);

/// <summary>rules.getCatalogInfo response.</summary>
public sealed record CatalogInfoDto(string RulesetVersion, int DetectorCount, int RoleCount, int KindCount);

/// <summary>rules.validate response.</summary>
public sealed record RulesValidationDto(bool Valid, IReadOnlyList<string> Errors);

/// <summary>diagnostics.getHealth response.</summary>
public sealed record HealthDto(string State, IReadOnlyDictionary<string, string> Checks);

/// <summary>diagnostics.exportBundle response (path to the redacted bundle, host-side).</summary>
public sealed record ExportBundleResponse(string BundlePath);

/// <summary>diagnostics.log request (UI→host log relay).</summary>
public sealed record LogRequest(string Level, string Message);

/// <summary>app.openExternalUrl request.</summary>
public sealed record OpenUrlRequest(string Url);

/// <summary>Empty result marker.</summary>
public sealed record EmptyResult();
