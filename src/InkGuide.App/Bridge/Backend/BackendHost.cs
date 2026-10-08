using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Analysis.Catalogs;
using InkGuide.Bridge.Handlers;
using InkGuide.Bridge.Services;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.DependencyInjection;
using InkGuide.Infrastructure.Persistence.Search;
using InkGuide.Infrastructure.Persistence.Sync;
using InkGuide.Infrastructure.Platform.Ai;
using InkGuide.Infrastructure.Platform.Cache;
using InkGuide.Infrastructure.Platform.DependencyInjection;
using InkGuide.Infrastructure.Platform.ExternalEdit;
using InkGuide.Infrastructure.Platform.Secrets;
using InkGuide.Infrastructure.Storage.DependencyInjection;
using InkGuide.Infrastructure.Storage.GoogleDrive;
using InkGuide.Infrastructure.Storage.LocalFolder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge.Backend;

/// <summary>
/// Host capabilities the bridge backend needs from the App shell.
/// Mutable holder: the composition root registers it empty and the main
/// window fills window-bound actions after construction. The backend calls
/// through these delegates and never touches WPF/WebView2 types.
/// </summary>
public sealed class BackendHost
{
    public string HostVersion { get; set; } = "0.0.0";

    public string UiSource { get; set; } = "none";

    public string UiStatus { get; set; } = "ui-missing";

    public string? UiName { get; set; }

    public string? UiVersion { get; set; }

    public Func<string, Task> SetBackgroundAsync { get; set; } = _ => Task.CompletedTask;

    public Func<string, Task> SetTitleAsync { get; set; } = _ => Task.CompletedTask;

    public Action<string> OpenExternalUrl { get; set; } = _ => { };

    public Action Quit { get; set; } = () => { };

    public Action Restart { get; set; } = () => { };

    public Func<string, string, Task> EmitEventAsync { get; set; } = (_, _) => Task.CompletedTask;
}

/// <summary>All file-system inputs for backend composition (plain data, no logic).</summary>
public sealed record BackendCompositionOptions(
    string DataDirectory,
    string SecretsDirectory,
    string ContentCacheDirectory,
    string ExternalEditDirectory,
    string LogsDirectory,
    string ShippedConfigDirectory,
    string UserRulesDirectory,
    string DatabasePath,
    bool LocalProviderEnabled,
    string LocalProviderDirectory,
    bool DriveFullScopeDefault,
    string WorkspaceFolderName,
    string AiModel,
    int AiDailyBudget)
{
    public static BackendCompositionOptions ForDirectories(
        string baseDirectory,
        string shippedConfigDirectory,
        bool localProviderEnabled = false,
        string localProviderDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        return new BackendCompositionOptions(
            baseDirectory,
            Path.Combine(baseDirectory, "secrets"),
            Path.Combine(baseDirectory, "cache", "content"),
            Path.Combine(baseDirectory, "external"),
            Path.Combine(baseDirectory, "logs"),
            shippedConfigDirectory,
            Path.Combine(baseDirectory, "rules"),
            Path.Combine(baseDirectory, "data", "inkguide.db"),
            localProviderEnabled,
            localProviderDirectory,
            false,
            "InkGuide Workspace",
            "claude-haiku-5-5",
            500);
    }
}

/// <summary>
/// Production composition for the bridge backend. Used by the App composition
/// root AND by integration tests, so the tested container is the shipped one.
/// </summary>
public static class BackendComposition
{
    public static IServiceCollection AddInkGuideBackend(
        IServiceCollection services,
        BackendCompositionOptions options,
        BackendHost host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);

        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath) ?? options.DataDirectory);
        Directory.CreateDirectory(options.SecretsDirectory);
        Directory.CreateDirectory(options.ContentCacheDirectory);
        Directory.CreateDirectory(options.ExternalEditDirectory);
        Directory.CreateDirectory(options.LogsDirectory);
        Directory.CreateDirectory(options.UserRulesDirectory);

        _ = services.AddSingleton(options);
        _ = services.AddSingleton(host);
        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddSingleton<ISecretBackend>(sp =>
            new SecretBackendAdapter(
                sp.GetRequiredService<ISecretStore>(),
                options.SecretsDirectory));
        _ = services.AddHttpBridge();

        _ = services.AddInkGuidePersistence(new PersistenceOptions
        {
            DatabasePath = options.DatabasePath,
        });

        _ = services.AddInkGuideStorage(
            new LocalFolderOptions
            {
                RootDirectory = options.LocalProviderDirectory,
                Enabled = options.LocalProviderEnabled,
            },
            new GoogleDriveOptions
            {
                FullDrive = options.DriveFullScopeDefault,
                WorkspaceFolderName = options.WorkspaceFolderName,
            },
            new DriveResilienceOptions(),
            new GoogleOAuthOptions());

        _ = services.AddInkGuidePlatform(
            options.SecretsDirectory,
            new ContentCacheOptions
            {
                RootDirectory = options.ContentCacheDirectory,
            },
            new ExternalEditOptions
            {
                RootDirectory = options.ExternalEditDirectory,
            },
            new AnthropicOptions
            {
                Model = options.AiModel,
                StateDirectory = Path.Combine(options.DataDirectory, "ai"),
            });

        AnalysisCatalogs catalogs = AnalysisCatalogs.Load(options.ShippedConfigDirectory, options.UserRulesDirectory);
        _ = services.AddSingleton(new CatalogHolder(catalogs, options.ShippedConfigDirectory, options.UserRulesDirectory));

        _ = services.AddSingleton<ConfirmationService>();
        _ = services.AddSingleton<IdempotencyStore>();
        _ = services.AddSingleton<BackendSettingsService>();
        _ = services.AddSingleton<NodeSearchService>();
        _ = services.AddSingleton<BackendOutboxRouter>();
        _ = services.AddSingleton<IOutboxExecutor>(sp => sp.GetRequiredService<BackendOutboxRouter>());
        _ = services.AddSingleton<ISyncHooks>(sp => sp.GetRequiredService<BackendOutboxRouter>());
        _ = services.AddSingleton<IProviderResolver, ProviderResolver>();
        _ = services.AddSingleton<ManifestService>();
        _ = services.AddSingleton<SyncCoordinator>();
        _ = services.AddSingleton(sp => new AnthropicDescriptionProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("inkguide-ai"),
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<AnthropicOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<AnthropicDescriptionProvider>>()));
        _ = services.AddSingleton<SyncCoordinator>();
        _ = services.AddSingleton<IBridgeBackend, InkGuideBridgeBackend>();        _ = services.AddSingleton<IBridgeHandler, AppHandler>();
        _ = services.AddSingleton<IBridgeHandler, WindowHandler>();
        _ = services.AddSingleton<IBridgeHandler, SettingsHandler>();
        _ = services.AddSingleton<IBridgeHandler, DiagnosticsHandler>();
        _ = services.AddSingleton<IBridgeHandler, RulesHandler>();
        _ = services.AddSingleton<IBridgeHandler, StorageHandler>();
        _ = services.AddSingleton<IBridgeHandler, AccountsHandler>();
        _ = services.AddSingleton<IBridgeHandler, WorkspaceHandler>();
        _ = services.AddSingleton<IBridgeHandler, AiHandler>();
        _ = services.AddSingleton<IBridgeHandler, ReposHandler>();
        _ = services.AddSingleton<IBridgeHandler, ProjectsHandler>();
        _ = services.AddSingleton<IBridgeHandler, NodesHandler>();
        _ = services.AddSingleton<IBridgeHandler, TransfersHandler>();
        _ = services.AddSingleton<IBridgeHandler, ImportHandler>();
        _ = services.AddSingleton<IBridgeHandler, InsightHandler>();
        _ = services.AddSingleton<IBridgeHandler, TagsHandler>();
        _ = services.AddSingleton<IBridgeHandler, SearchHandler>();
        _ = services.AddSingleton<IBridgeHandler, SyncHandler>();
        _ = services.AddSingleton<IBridgeHandler, SnapshotsHandler>();
        _ = services.AddSingleton<IBridgeHandler, ActivityHandler>();
        _ = services.AddSingleton<IBridgeHandler, OrganizeHandler>();
        _ = services.AddSingleton<IBridgeHandler, DuplicatesHandler>();
        _ = services.AddSingleton<IBridgeHandler, ExternalHandler>();
        _ = services.AddSingleton<BridgeDispatcher>();

        return services;
    }
}

/// <summary>Post-build startup: database bootstrap + FTS wiring. Called by the App and tests.</summary>
public static class BackendStartup
{
    public static async Task<bool> EnsureAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var bootstrap = (DatabaseBootstrap)services.GetService(typeof(DatabaseBootstrap))!;
        ArgumentNullException.ThrowIfNull(bootstrap);
        bool fts = await bootstrap.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var search = (NodeSearchService)services.GetService(typeof(NodeSearchService))!;
        ArgumentNullException.ThrowIfNull(search);
        search.SetFtsAvailable(fts);

        // External-edit file changes flow back through the backend (conflict-safe).
        var external = (ExternalEditService)services.GetService(typeof(ExternalEditService))!;
        ArgumentNullException.ThrowIfNull(external);
        var backend = (InkGuide.Bridge.Services.IBridgeBackend)services.GetService(typeof(InkGuide.Bridge.Services.IBridgeBackend))!;
        ArgumentNullException.ThrowIfNull(backend);
        external.ChangeHandler = (IExternalChangeHandler)backend;
        _ = await external.ReadoptAsync(cancellationToken).ConfigureAwait(false);
        return fts;
    }

    /// <summary>Locates shipped config/: beside the exe when published, else the repo config/.</summary>
    public static string ShippedConfigDirectory()
    {
        string besideExe = Path.Combine(AppContext.BaseDirectory, "config");
        if (Directory.Exists(besideExe))
        {
            return besideExe;
        }

        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "InkGuide.sln")))
            {
                return Path.Combine(dir, "config");
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return besideExe;
    }
}

/// <summary>Maps App shell types onto backend composition options.</summary>
internal static class BackendAppMapping
{
    internal static IServiceCollection AddBackendInkGuide(
        this IServiceCollection services,
        AppPaths paths,
        CliOptions cli,
        BackendHost host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(host);
        var options = new BackendCompositionOptions(
            paths.BaseDir,
            paths.SecretsDir,
            paths.ContentCacheDir,
            paths.ExternalDir,
            paths.LogsDir,
            BackendStartup.ShippedConfigDirectory(),
            paths.RulesDir,
            Path.Combine(paths.DataDir, "inkguide.db"),
            cli.LocalProviderDir is not null,
            cli.LocalProviderDir ?? string.Empty,
            false,
            AppIdentity.WorkspaceFolderName,
            "claude-haiku-5-5",
            500);
        return BackendComposition.AddInkGuideBackend(services, options, host);
    }
}
