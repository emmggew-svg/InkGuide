using System;
using System.IO;

namespace InkGuide.App;

/// <summary>
/// Resolves and provisions the application folders (MASTER_PROMPT Section 17).
/// Portable mode stores data next to the executable when --portable is passed
/// or a portable.flag file sits beside it.
/// </summary>
internal sealed class AppPaths
{
    public string BaseDir { get; }
    public bool IsPortable { get; }

    public string DataDir => Resolve("data");
    public string CacheDir => Resolve("cache");
    public string ContentCacheDir => Resolve(Path.Combine("cache", "content"));
    public string PreviewCacheDir => Resolve(Path.Combine("cache", "preview"));
    public string ExternalDir => Resolve("external");
    public string LogsDir => Resolve("logs");
    public string WebView2Dir => Resolve("webview2");
    public string RulesDir => Resolve("rules");
    public string UiOverrideDir => Resolve("ui-override");
    public string SecretsDir => Resolve("secrets");
    public string SettingsPath => Path.Combine(BaseDir, "settings.json");
    public string WindowStatePath => Path.Combine(BaseDir, "window-state.json");

    /// <summary>Bundled UI output (copied by publish.cmd; produced by the UI build).</summary>
    public static string UiBundledDir => Path.Combine(AppContext.BaseDirectory, "ui");

    private AppPaths(string baseDir, bool isPortable)
    {
        BaseDir = baseDir;
        IsPortable = isPortable;
    }

    public static AppPaths Resolve(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string exeDir = AppContext.BaseDirectory;
        bool flagFile = File.Exists(Path.Combine(exeDir, "portable.flag"));
        bool portable = options.Portable || flagFile;

        string baseDir;
        if (options.DataDir is not null && options.DataDir.Length > 0)
        {
            baseDir = Path.GetFullPath(options.DataDir);
            portable = false;
        }
        else if (portable)
        {
            baseDir = Path.Combine(exeDir, "data");
        }
        else
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.AppName);
        }

        return new AppPaths(baseDir, portable);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ContentCacheDir);
        Directory.CreateDirectory(PreviewCacheDir);
        Directory.CreateDirectory(ExternalDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(WebView2Dir);
        Directory.CreateDirectory(RulesDir);
        Directory.CreateDirectory(SecretsDir);
    }

    private string Resolve(string relative)
    {
        return Path.Combine(BaseDir, relative);
    }
}
