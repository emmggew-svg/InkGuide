using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using InkGuide.App.Bridge;
using InkGuide.App.Hosting;
using InkGuide.App.WebView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace InkGuide.App;

/// <summary>
/// Composition root. Wires folders, logging, single-instance, the WebView2
/// host and the bridge transport. Contains no business logic: domain work is
/// served by the Bridge backend module (if present) behind the transport.
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private SingleInstance? _singleInstance;
    private ILogger<App>? _logger;

    // Async void is the framework's event-handler shape for startup; all work
    // is awaited inside and failures shut the app down with a logged reason.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HookExceptionHandlers();

        CliOptions cli = CliOptions.Parse(e.Args);
        if (cli.Error is not null)
        {
            NativeDialog.ShowInfo(CliOptions.Usage + "\n\nError: " + cli.Error);
            Shutdown(2);
            return;
        }

        if (cli.ShowHelp)
        {
            NativeDialog.ShowInfo(CliOptions.Usage);
            Shutdown(0);
            return;
        }

        AppPaths paths = AppPaths.Resolve(cli);
        paths.EnsureCreated();

        string logLevel = Environment.GetEnvironmentVariable("INKGUIDE_LOG_LEVEL") ?? "Information";
        ILoggerFactory loggerFactory = SerilogSetup.CreateFactory(paths, logLevel);
        _logger = loggerFactory.CreateLogger<App>();

        try
        {
            if (cli.Reset)
            {
                var reset = new ResetService(loggerFactory.CreateLogger<ResetService>());
                reset.TryReset(paths);
            }

            var singleInstance = new SingleInstance(loggerFactory.CreateLogger<SingleInstance>());
            if (!singleInstance.TryAcquire())
            {
                await SingleInstance.ForwardToFirstInstanceAsync(cli.OpenPaths, CancellationToken.None).ConfigureAwait(true);
                SingleInstance.ActivateFirstWindow();
                Shutdown(0);
                return;
            }

            _singleInstance = singleInstance;

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);
            builder.Services.AddSingleton(cli);
            builder.Services.AddSingleton(paths);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(singleInstance);
            builder.Services.AddSingleton<WindowStateStore>();
            builder.Services.AddSingleton<UiOverride>();
            builder.Services.AddSingleton<ResetService>();
            builder.Services.AddSingleton<DragDropHandler>();
            builder.Services.AddSingleton<FallbackRouter>();
            builder.Services.AddSingleton(sp => new BridgeTransport(
                sp.GetRequiredService<ILogger<BridgeTransport>>(),
                sp.GetRequiredService<TimeProvider>(),
                Dispatcher.CurrentDispatcher,
                sp.GetRequiredService<DragDropHandler>()));
            builder.Services.AddSingleton<WebViewHostSetup>();
            builder.Services.AddSingleton<BridgeModuleLoader>();

            var loader = new BridgeModuleLoader(loggerFactory.CreateLogger<BridgeModuleLoader>());
            loader.ConfigureBackend(builder.Services);

            _host = builder.Build();
            await _host.StartAsync().ConfigureAwait(true);

            UiResolution ui = _host.Services.GetRequiredService<UiOverride>().Resolve();
            if (ui.Folder is null)
            {
                AppLogMessages.NoUsableUi(_logger, ui.Status);
                NativeDialog.ShowMissingUi("Status: " + ui.Status);
            }

            var routers = new List<IBridgeRequestRouter>();
            ExternalBridgeHandler? backend = loader.CreateHandler();
            if (backend is not null)
            {
                routers.Add(backend);
            }

            routers.Add(_host.Services.GetRequiredService<FallbackRouter>());

            var window = new MainWindow(
                _host.Services.GetRequiredService<ILogger<MainWindow>>(),
                paths,
                cli,
                ui,
                _host.Services.GetRequiredService<BridgeTransport>(),
                routers,
                _host.Services.GetRequiredService<WindowStateStore>(),
                _host.Services.GetRequiredService<WebViewHostSetup>(),
                Quit,
                Restart);

            MainWindow = window;
            singleInstance.ArgumentsReceived += window.ForwardOpenPaths;
            singleInstance.StartListener(_host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);

            window.Show();

            if (cli.OpenPaths.Length > 0)
            {
                window.ForwardOpenPaths(cli.OpenPaths);
            }
        }
        catch (Exception ex)
        {
            if (_logger is null)
            {
                NativeDialog.ShowFatal(ex.Message);
            }
            else
            {
                AppLogMessages.StartupFailed(_logger, ex);
                NativeDialog.ShowFatal(ex.Message);
            }

            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                await _host.StopAsync().ConfigureAwait(true);
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                AppLogMessages.ShutdownError(_logger, ex);
            }
        }
        finally
        {
            _singleInstance?.Dispose();
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }

    private void Quit()
    {
        Dispatcher.Invoke(Shutdown);
    }

    private void Restart()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe is not null)
            {
                var startInfo = new ProcessStartInfo { FileName = exe, UseShellExecute = true };
                Process.Start(startInfo);
            }
        }
        catch (Exception ex)
        {
            ILogger<App>? snapshot = _logger;
            if (snapshot is not null)
            {
                AppLogMessages.RestartFailed(snapshot, ex);
            }
        }
        finally
        {
            Dispatcher.Invoke(Shutdown);
        }
    }

    private void HookExceptionHandlers()
    {
        DispatcherUnhandledException += (sender, args) =>
        {
            ILogger<App>? snapshot = _logger;
            if (snapshot is not null)
            {
                AppLogMessages.UiThreadCrash(snapshot, args.Exception);
            }

            NativeDialog.ShowFatal("The interface hit an unexpected error but the app is still running. Queued work is preserved.");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            try
            {
                ILogger<App>? snapshot = _logger;
                if (snapshot is not null)
                {
                    AppLogMessages.FatalCrash(snapshot, args.ExceptionObject as Exception);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                Log.CloseAndFlush();
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            ILogger<App>? snapshot = _logger;
            if (snapshot is not null)
            {
                AppLogMessages.BackgroundTaskFault(snapshot, args.Exception);
            }

            args.SetObserved();
        };
    }
}
