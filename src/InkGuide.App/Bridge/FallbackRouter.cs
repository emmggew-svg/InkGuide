using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.App.Hosting;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge;

/// <summary>
/// Serves the host-owned bridge methods: app.hello/getInfo/getUiStatus/
/// openLogsFolder/openExternalUrl/quit/restart, window.setBackground/setTitle,
/// diagnostics.log. Every other method returns a typed error until the Bridge
/// backend module is loaded (explicit "not served" state, never a stub throw).
/// </summary>
internal sealed partial class FallbackRouter : IBridgeRequestRouter
{
    private static readonly string[] OwnedCapabilities = new[]
    {
        "virtualHosts", "dragDropPaths", "uiOverride", "devUrl",
    };

    private static readonly HashSet<string> OwnedMethods = new(StringComparer.Ordinal)
    {
        "app.hello", "app.getInfo", "app.getUiStatus",
        "app.openLogsFolder", "app.openExternalUrl", "app.quit", "app.restart",
        "window.setBackground", "window.setTitle",
        "diagnostics.log",
    };

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();

    private readonly ILogger<FallbackRouter> _logger;

    public FallbackRouter(ILogger<FallbackRouter> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string method) => OwnedMethods.Contains(method);

    public Task<BridgeOutcome> RouteAsync(string method, string paramsJson, HostServices host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(host);

        try
        {
            return method switch
            {
                "app.hello" => Task.FromResult(Ok(Handshake(new
                {
                    contract = new { major = AppIdentity.BridgeContractMajor, minor = AppIdentity.BridgeContractMinor },
                    hostVersion = host.HostVersion,
                    capabilities = OwnedCapabilities,
                    features = new { localProvider = host.CliOptions.LocalProviderDir is not null },
                    settingsSnapshot = new { },
                    uiStatus = UiStatus(host),
                }))),
                "app.getInfo" => Task.FromResult(Ok(new
                {
                    name = AppIdentity.AppName,
                    version = host.HostVersion,
                    appId = AppIdentity.AppId,
                    portable = host.Paths.IsPortable,
                    dataDir = host.Paths.BaseDir,
                })),
                "app.getUiStatus" => Task.FromResult(Ok(UiStatus(host))),
                "app.openLogsFolder" => OpenLogsFolder(host),
                "app.openExternalUrl" => OpenExternalUrl(paramsJson, host),
                "app.quit" => Quit(host, restart: false),
                "app.restart" => Quit(host, restart: true),
                "window.setBackground" => SetBackground(paramsJson, host),
                "window.setTitle" => SetTitle(paramsJson, host),
                "diagnostics.log" => LogFromUi(paramsJson),
                _ => Task.FromResult(BridgeOutcome.Fail(BridgeError.BackendMissing(method))),
            };
        }
        catch (JsonException ex)
        {
            return Task.FromResult(BridgeOutcome.Fail(BridgeError.InvalidParams("Malformed params JSON: " + ex.Message)));
        }
    }

    private static object UiStatus(HostServices host)
    {
        return new
        {
            source = host.UiResolution.Source,
            status = host.UiResolution.Status,
            name = host.UiResolution.Name,
            version = host.UiResolution.Version,
        };
    }

    private static BridgeOutcome Ok(object value)
    {
        return BridgeOutcome.Ok(JsonSerializer.Serialize(value, BridgeEnvelope.Options));
    }

    private BridgeOutcome Handshake(object value)
    {
        AppLogMessages.HandshakeServed(_logger, AppIdentity.BridgeContractMajor, AppIdentity.BridgeContractMinor);
        return Ok(value);
    }

    private Task<BridgeOutcome> OpenLogsFolder(HostServices host)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = host.Paths.LogsDir,
                UseShellExecute = true,
            };
            Process.Start(startInfo);
            return Task.FromResult(Ok(new { opened = true }));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            AppLogMessages.OpenLogsFailed(_logger, ex);
            return Task.FromResult(BridgeOutcome.Fail(BridgeError.Internal("Could not open the logs folder.")));
        }
    }

    private static Task<BridgeOutcome> OpenExternalUrl(string paramsJson, HostServices host)
    {
        using JsonDocument doc = JsonDocument.Parse(paramsJson);
        string url = doc.RootElement.TryGetProperty("url", out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != "mailto"))
        {
            return Task.FromResult(BridgeOutcome.Fail(BridgeError.InvalidParams("Only http, https and mailto URLs can be opened externally.")));
        }

        host.OpenExternalUrl(url);
        return Task.FromResult(Ok(new { opened = true }));
    }

    private static Task<BridgeOutcome> Quit(HostServices host, bool restart)
    {
        if (restart)
        {
            host.Restart();
        }
        else
        {
            host.Quit();
        }

        return Task.FromResult(BridgeOutcome.Ok("{}"));
    }

    private static async Task<BridgeOutcome> SetBackground(string paramsJson, HostServices host)
    {
        using JsonDocument doc = JsonDocument.Parse(paramsJson);
        string color = doc.RootElement.TryGetProperty("color", out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

        if (!HexColor().IsMatch(color))
        {
            return BridgeOutcome.Fail(BridgeError.InvalidParams("color must be a hex value like #RRGGBB."));
        }

        await host.SetBackgroundAsync(color).ConfigureAwait(false);
        return BridgeOutcome.Ok("{}");
    }

    private static async Task<BridgeOutcome> SetTitle(string paramsJson, HostServices host)
    {
        using JsonDocument doc = JsonDocument.Parse(paramsJson);
        string title = doc.RootElement.TryGetProperty("title", out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

        if (title.Length == 0 || title.Length > 200)
        {
            return BridgeOutcome.Fail(BridgeError.InvalidParams("title must be 1-200 characters."));
        }

        await host.SetTitleAsync(title).ConfigureAwait(false);
        return BridgeOutcome.Ok("{}");
    }

    private Task<BridgeOutcome> LogFromUi(string paramsJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(paramsJson);
            JsonElement root = doc.RootElement;
            string level = root.TryGetProperty("level", out JsonElement l) && l.ValueKind == JsonValueKind.String
                ? l.GetString() ?? "info"
                : "info";
            string message = root.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String
                ? Hosting.LogRedaction.Redact(m.GetString())
                : string.Empty;

            switch (level.ToLowerInvariant())
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
        }
        catch (JsonException ex)
        {
            return Task.FromResult(BridgeOutcome.Fail(BridgeError.InvalidParams("Malformed params JSON: " + ex.Message)));
        }

        return Task.FromResult(BridgeOutcome.Ok("{}"));
    }
}
