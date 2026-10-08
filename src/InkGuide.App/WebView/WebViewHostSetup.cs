using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using InkGuide.App.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace InkGuide.App.WebView;

/// <summary>
/// Initializes WebView2: runtime detection, user-data folder, virtual hosts,
/// and the full hardening surface (MASTER_PROMPT Section 14.1).
/// </summary>
internal sealed class WebViewHostSetup
{
    private readonly ILogger<WebViewHostSetup> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly AppPaths _paths;
    private readonly Bridge.BridgeTransport _transport;
    private readonly Queue<DateTimeOffset> _reloadAttempts = new();

    public WebViewHostSetup(
        ILogger<WebViewHostSetup> logger,
        TimeProvider timeProvider,
        AppPaths paths,
        Bridge.BridgeTransport transport)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _paths = paths;
        _transport = transport;
    }

    public async Task<CoreWebView2?> InitializeAsync(WebView2 webView, CliOptions cli, Hosting.UiResolution ui)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(ui);

        if (!IsRuntimeAvailable())
        {
            Hosting.NativeDialog.ShowMissingRuntime(AppIdentity.WebView2EvergreenLink);
            return null;
        }

        CoreWebView2Environment env;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(null, _paths.WebView2Dir).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLogMessages.EnvFailed(_logger, ex);
            Hosting.NativeDialog.ShowFatal("Could not initialize the browser engine. See the log file for details.");
            return null;
        }

        try
        {
            await webView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLogMessages.ControlFailed(_logger, ex);
            Hosting.NativeDialog.ShowFatal("Could not initialize the browser engine. See the log file for details.");
            return null;
        }

        CoreWebView2 core = webView.CoreWebView2;
        ApplySettings(core, cli);
        MapVirtualHosts(core, ui);
        WireEvents(core, cli, ui);
        _transport.Attach(core);

        string? devUrl = EffectiveDevUrl(cli);
        core.Navigate(devUrl ?? (AppIdentity.UiOrigin + "/index.html"));
        return core;
    }

    private bool IsRuntimeAvailable()
    {
        try
        {
            string version = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
            AppLogMessages.RuntimeVersion(_logger, version);
            return true;
        }
        catch (Exception ex)
        {
            AppLogMessages.RuntimeMissing(_logger, ex);
            return false;
        }
    }

    private void ApplySettings(CoreWebView2 core, CliOptions cli)
    {
        bool debugDefault = IsDebugDefault();
        CoreWebView2Settings settings = core.Settings;
        settings.AreDevToolsEnabled = ReadFlag("INKGUIDE_DEVTOOLS", debugDefault);
        settings.AreDefaultContextMenusEnabled = ReadFlag("INKGUIDE_CONTEXT_MENU", debugDefault);
        settings.AreBrowserAcceleratorKeysEnabled = ReadFlag("INKGUIDE_ACCELERATORS", debugDefault);
        settings.IsZoomControlEnabled = ReadFlag("INKGUIDE_ZOOM", debugDefault);
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        AppLogMessages.SettingsApplied(_logger, debugDefault);
    }

    private static bool IsDebugDefault()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static bool ReadFlag(string variable, bool defaultValue)
    {
        string? raw = Environment.GetEnvironmentVariable(variable);
        if (raw is null)
        {
            return defaultValue;
        }

        return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private void MapVirtualHosts(CoreWebView2 core, Hosting.UiResolution ui)
    {
        // SetVirtualHostNameToFolderMapping throws for missing folders. The
        // ui-missing case is already reported via dialog + app.getUiStatus;
        // ensure the directory so startup never crashes on top of that.
        string uiFolder = ui.Folder ?? AppPaths.UiBundledDir;
        Directory.CreateDirectory(uiFolder);
        Directory.CreateDirectory(_paths.ContentCacheDir);
        Directory.CreateDirectory(_paths.PreviewCacheDir);
        core.SetVirtualHostNameToFolderMapping(
            AppIdentity.UiHost, uiFolder, CoreWebView2HostResourceAccessKind.Deny);
        core.SetVirtualHostNameToFolderMapping(
            AppIdentity.CacheHost, _paths.ContentCacheDir, CoreWebView2HostResourceAccessKind.DenyCors);
        core.SetVirtualHostNameToFolderMapping(
            AppIdentity.UserContentHost, _paths.PreviewCacheDir, CoreWebView2HostResourceAccessKind.DenyCors);
        AppLogMessages.HostsMapped(_logger, uiFolder, ui.Source);
    }

    private void WireEvents(CoreWebView2 core, CliOptions cli, Hosting.UiResolution ui)
    {
        string? devUrl = EffectiveDevUrl(cli);
        core.NavigationStarting += (sender, e) =>
        {
            if (!IsAllowedDocument(e.Uri, devUrl))
            {
                AppLogMessages.NavBlocked(_logger);
                e.Cancel = true;
            }
        };
        core.FrameNavigationStarting += (sender, e) =>
        {
            if (!IsAllowedDocument(e.Uri, devUrl))
            {
                AppLogMessages.FrameBlocked(_logger);
                e.Cancel = true;
            }
        };
        core.NewWindowRequested += (sender, e) =>
        {
            e.Handled = true;
            if (IsExternalScheme(e.Uri))
            {
                OpenInSystemBrowser(e.Uri);
            }
            else
            {
                AppLogMessages.PopupBlocked(_logger);
            }
        };
        core.PermissionRequested += (sender, e) =>
        {
            e.State = CoreWebView2PermissionState.Deny;
        };
        core.DownloadStarting += (sender, e) =>
        {
            // Cancelled here; the app's own transfer flow (transfers.*) owns
            // downloads once the Bridge backend module is loaded.
            e.Cancel = true;
            AppLogMessages.DownloadIntercepted(_logger);
        };
        core.ProcessFailed += (sender, e) =>
        {
            AppLogMessages.ProcessFailed(_logger, e.ProcessFailedKind.ToString());
            ReloadThrottled(core);
        };
    }

    private static bool IsAllowedDocument(string? uri, string? devUrl)
    {
        if (string.IsNullOrEmpty(uri) || uri == "about:blank")
        {
            return true;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (parsed.Host.Equals(AppIdentity.UiHost, StringComparison.OrdinalIgnoreCase) ||
            parsed.Host.Equals(AppIdentity.CacheHost, StringComparison.OrdinalIgnoreCase) ||
            parsed.Host.Equals(AppIdentity.UserContentHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (devUrl is not null && Uri.TryCreate(devUrl, UriKind.Absolute, out Uri? dev) &&
            parsed.Scheme == dev.Scheme &&
            parsed.Host.Equals(dev.Host, StringComparison.OrdinalIgnoreCase) &&
            parsed.Port == dev.Port)
        {
            return true;
        }

        return false;
    }

    private static bool IsExternalScheme(string? uri)
    {
        if (!Uri.TryCreate(uri ?? string.Empty, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        return parsed.Scheme == Uri.UriSchemeHttp ||
               parsed.Scheme == Uri.UriSchemeHttps ||
               parsed.Scheme == "mailto";
    }

    private void OpenInSystemBrowser(string? uri)
    {
        Hosting.ExternalBrowser.Open(uri, _logger);
    }

    private string? EffectiveDevUrl(CliOptions cli)
    {
        if (string.IsNullOrWhiteSpace(cli.UiDevUrl))
        {
            return null;
        }

#if DEBUG
        AppLogMessages.DevUrlActive(_logger, cli.UiDevUrl);
        return cli.UiDevUrl;
#else
        AppLogMessages.DevUrlIgnored(_logger);
        return null;
#endif
    }

    private void ReloadThrottled(CoreWebView2 core)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        while (_reloadAttempts.Count > 0 && now - _reloadAttempts.Peek() > TimeSpan.FromMinutes(1))
        {
            _reloadAttempts.Dequeue();
        }

        if (_reloadAttempts.Count >= 3)
        {
            AppLogMessages.ReloadPaused(_logger);
            return;
        }

        _reloadAttempts.Enqueue(now);
        try
        {
            core.Reload();
            AppLogMessages.Reloaded(_logger);
        }
        catch (Exception ex)
        {
            AppLogMessages.ReloadFailed(_logger, ex);
        }
    }
}
