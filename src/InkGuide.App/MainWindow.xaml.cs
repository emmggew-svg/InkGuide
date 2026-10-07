using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using InkGuide.App.Bridge;
using InkGuide.App.Hosting;
using InkGuide.App.WebView;
using Microsoft.Extensions.Logging;

namespace InkGuide.App;

/// <summary>
/// The single application window. It contains only the WebView2 control;
/// native chrome stays default. All wiring delegates to injected host
/// services — there is no business logic here.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly AppPaths _paths;
    private readonly CliOptions _cli;
    private readonly UiResolution _ui;
    private readonly BridgeTransport _transport;
    private readonly IReadOnlyList<IBridgeRequestRouter> _routers;
    private readonly WindowStateStore _states;
    private readonly WebViewHostSetup _setup;
    private readonly Action _quit;
    private readonly Action _restart;
    private bool _initialized;

    internal MainWindow(
        ILogger<MainWindow> logger,
        AppPaths paths,
        CliOptions cli,
        UiResolution ui,
        BridgeTransport transport,
        IReadOnlyList<IBridgeRequestRouter> routers,
        WindowStateStore states,
        WebViewHostSetup setup,
        Action quit,
        Action restart)
    {
        _logger = logger;
        _paths = paths;
        _cli = cli;
        _ui = ui;
        _transport = transport;
        _routers = routers;
        _states = states;
        _setup = setup;
        _quit = quit;
        _restart = restart;

        InitializeComponent();
        _states.Restore(this);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>Forwards second-instance launch paths as an app.openPaths bridge event.</summary>
    internal void ForwardOpenPaths(string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Length == 0)
        {
            return;
        }

        string payload = JsonSerializer.Serialize(new { paths }, BridgeEnvelope.Options);
        _ = EmitAndLogAsync("app.openPaths", payload);
    }

    private async Task EmitAndLogAsync(string method, string payload)
    {
        try
        {
            await _transport.EmitEventAsync(method, payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogMessages.EmitFailed(_logger, ex, method);
        }
    }

    // Async void: genuine WPF event handler; failures are caught and logged.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            HostServices host = new(
                _paths,
                _cli,
                _ui,
                Hosting.AppVersion.Current,
                SetBackgroundAsync,
                SetTitleAsync,
                url => ExternalBrowser.Open(url, _logger),
                _quit,
                _restart,
                (method, payload) => _transport.EmitEventAsync(method, payload));

            _transport.Configure(host, _routers, _cli.UiDevUrl);

            Microsoft.Web.WebView2.Core.CoreWebView2? core =
                await _setup.InitializeAsync(Browser, _cli, _ui).ConfigureAwait(true);
            if (core is null)
            {
                AppLogMessages.WebViewNotReady(_logger);
            }
        }
        catch (Exception ex)
        {
            AppLogMessages.InitFailed(_logger, ex);
            NativeDialog.ShowFatal("Could not start the interface. See the log file for details.");
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _states.Save(this);
        _transport.Detach();
    }

    private Task SetBackgroundAsync(string color)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            try
            {
                object? converted = new BrushConverter().ConvertFromString(color);
                if (converted is SolidColorBrush brush)
                {
                    Background = brush;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is NotSupportedException)
            {
                AppLogMessages.BadColor(_logger);
            }
        }).Task;
    }

    private Task SetTitleAsync(string title)
    {
        return Dispatcher.InvokeAsync(() => { Title = title; }).Task;
    }
}
