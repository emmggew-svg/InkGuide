namespace InkGuide.App;

/// <summary>
/// Single place for runtime identity strings. Nothing else in the App
/// layer hard-codes the application name, identifiers, hosts, or links.
/// </summary>
internal static class AppIdentity
{
    public const string AppName = "InkGuide";
    public const string AppId = "inkguide.desktop";
    public const string WorkspaceFolderName = "InkGuide Workspace";
    public const string ManifestFolderName = ".inkguide";

    /// <summary>Named mutex for single-instance enforcement (per-user session).</summary>
    public const string SingleInstanceMutexName = @"Local\inkguide.desktop";

    /// <summary>Named pipe used to forward launch arguments to the first instance.</summary>
    public const string SingleInstancePipeName = "inkguide.desktop.ipc";

    /// <summary>Official Microsoft Edge WebView2 Evergreen bootstrapper link.</summary>
    public const string WebView2EvergreenLink = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public const string UiHost = "ui.appassets.example";
    public const string CacheHost = "cache.appassets.example";
    public const string UserContentHost = "usercontent.appassets.example";

    public const int BridgeContractMajor = 1;
    public const int BridgeContractMinor = 0;

    /// <summary>Maximum bridge message size: 1 MB (MASTER_PROMPT Section 14.2).</summary>
    public const int BridgeMaxMessageBytes = 1024 * 1024;

    /// <summary>Default window background, used to avoid white-flash before the UI loads. Overridable via window.setBackground.</summary>
    public const string DefaultBackgroundColor = "#F0F0F0";

    public static string UiOrigin => "https://" + UiHost;
}
