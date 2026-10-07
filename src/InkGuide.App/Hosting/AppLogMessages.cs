using System;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Hosting;

/// <summary>
/// All host log messages as LoggerMessage delegates (CA1848). One place for
/// message text, event ids and levels; call sites pass only the ILogger.
/// </summary>
internal static partial class AppLogMessages
{
    // SingleInstance (100-109)
    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Forwarded launch arguments received: {Count}.")]
    public static partial void ForwardedArgs(ILogger logger, int count);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Single-instance pipe error; continuing to listen.")]
    public static partial void PipeError(ILogger logger, Exception ex);

    // WindowStateStore (110-119)
    [LoggerMessage(EventId = 110, Level = LogLevel.Information, Message = "Stored window position is off-screen; using defaults.")]
    public static partial void OffscreenPosition(ILogger logger);

    [LoggerMessage(EventId = 111, Level = LogLevel.Warning, Message = "Could not persist window state.")]
    public static partial void PersistFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 112, Level = LogLevel.Warning, Message = "Could not read window state; using defaults.")]
    public static partial void ReadFailed(ILogger logger, Exception ex);

    // UiOverride (120-129)
    [LoggerMessage(EventId = 120, Level = LogLevel.Information, Message = "Using ui-override from {Dir}.")]
    public static partial void UsingOverride(ILogger logger, string dir);

    [LoggerMessage(EventId = 121, Level = LogLevel.Warning, Message = "Ignoring ui-override: {Reason}. Falling back to bundled UI.")]
    public static partial void IgnoringOverride(ILogger logger, string reason);

    // ExternalBrowser (130-139)
    [LoggerMessage(EventId = 130, Level = LogLevel.Warning, Message = "Refused to open a non-launchable target.")]
    public static partial void LaunchRefused(ILogger logger);

    [LoggerMessage(EventId = 131, Level = LogLevel.Warning, Message = "Could not open the system browser.")]
    public static partial void BrowserOpenFailed(ILogger logger, Exception ex);

    // BridgeModuleLoader (140-149)
    [LoggerMessage(EventId = 140, Level = LogLevel.Information, Message = "Bridge backend module has no Configure entry point; host-only routing active.")]
    public static partial void NoConfigure(ILogger logger);

    [LoggerMessage(EventId = 141, Level = LogLevel.Information, Message = "Bridge backend module registered its services.")]
    public static partial void BackendRegistered(ILogger logger);

    [LoggerMessage(EventId = 142, Level = LogLevel.Error, Message = "Bridge backend Configure failed; continuing with host-only routing.")]
    public static partial void ConfigureFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 143, Level = LogLevel.Warning, Message = "BridgeModule found but the CanHandle/HandleAsync entry points are missing or mis-shaped; ignoring backend.")]
    public static partial void BadEntryPoints(ILogger logger);

    [LoggerMessage(EventId = 144, Level = LogLevel.Information, Message = "Bridge backend module loaded; domain namespaces will be delegated.")]
    public static partial void BackendLoaded(ILogger logger);

    // ExternalBridgeHandler (150-159)
    [LoggerMessage(EventId = 150, Level = LogLevel.Warning, Message = "Backend CanHandle failed for {Method}.")]
    public static partial void CanHandleFailed(ILogger logger, Exception ex, string method);

    [LoggerMessage(EventId = 151, Level = LogLevel.Warning, Message = "Backend error for bridge call: {Code}.")]
    public static partial void BackendErrorCode(ILogger logger, string code);

    [LoggerMessage(EventId = 152, Level = LogLevel.Error, Message = "Backend handler threw for {Method}.")]
    public static partial void HandlerThrew(ILogger logger, Exception ex, string method);

    [LoggerMessage(EventId = 153, Level = LogLevel.Error, Message = "Backend handler threw an unexpected exception.")]
    public static partial void HandlerUnexpected(ILogger logger, Exception ex);

    // DragDropHandler (160-169)
    [LoggerMessage(EventId = 160, Level = LogLevel.Warning, Message = "Rejected drop path inside a reparse point: {Path}.")]
    public static partial void ReparseRejected(ILogger logger, string path);

    [LoggerMessage(EventId = 161, Level = LogLevel.Warning, Message = "Rejected unreadable drop path.")]
    public static partial void UnreadablePath(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 162, Level = LogLevel.Warning, Message = "Rejected unreadable drop directory.")]
    public static partial void UnreadableDir(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 163, Level = LogLevel.Warning, Message = "Rejected reparse-point drop entry.")]
    public static partial void ReparseEntry(ILogger logger);

    [LoggerMessage(EventId = 164, Level = LogLevel.Warning, Message = "Rejected unreadable drop entry.")]
    public static partial void UnreadableEntry(ILogger logger, Exception ex);

    // ResetService (170-179)
    [LoggerMessage(EventId = 170, Level = LogLevel.Warning, Message = "Could not delete directory during reset.")]
    public static partial void ResetDirFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 171, Level = LogLevel.Warning, Message = "Could not delete file during reset.")]
    public static partial void ResetFileFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 172, Level = LogLevel.Information, Message = "Reset completed: cache, settings and window state cleared.")]
    public static partial void ResetDone(ILogger logger);

    // FallbackRouter (180-189)
    [LoggerMessage(EventId = 180, Level = LogLevel.Warning, Message = "Could not open logs folder.")]
    public static partial void OpenLogsFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 181, Level = LogLevel.Warning, Message = "UI: {Message}")]
    public static partial void UiLogWarn(ILogger logger, string message);

    [LoggerMessage(EventId = 182, Level = LogLevel.Error, Message = "UI: {Message}")]
    public static partial void UiLogError(ILogger logger, string message);

    [LoggerMessage(EventId = 183, Level = LogLevel.Debug, Message = "UI: {Message}")]
    public static partial void UiLogDebug(ILogger logger, string message);

    [LoggerMessage(EventId = 184, Level = LogLevel.Information, Message = "UI: {Message}")]
    public static partial void UiLogInfo(ILogger logger, string message);

    [LoggerMessage(EventId = 185, Level = LogLevel.Information, Message = "Bridge handshake served (contract {Major}.{Minor}).")]
    public static partial void HandshakeServed(ILogger logger, int major, int minor);

    // BridgeTransport (190-209)
    [LoggerMessage(EventId = 190, Level = LogLevel.Warning, Message = "Rejected bridge message from unexpected origin.")]
    public static partial void BadOrigin(ILogger logger);

    [LoggerMessage(EventId = 191, Level = LogLevel.Warning, Message = "Rejected oversized bridge message ({Bytes} bytes).")]
    public static partial void Oversized(ILogger logger, int bytes);

    [LoggerMessage(EventId = 192, Level = LogLevel.Warning, Message = "Rejected malformed bridge message.")]
    public static partial void Malformed(ILogger logger);

    [LoggerMessage(EventId = 193, Level = LogLevel.Information, Message = "Drop validated: {Accepted} accepted, {Rejected} rejected.")]
    public static partial void DropStats(ILogger logger, int accepted, int rejected);

    [LoggerMessage(EventId = 194, Level = LogLevel.Error, Message = "Bridge receive path failed.")]
    public static partial void ReceiveFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 195, Level = LogLevel.Warning, Message = "Router CanHandle probe failed.")]
    public static partial void RouterProbeFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 196, Level = LogLevel.Error, Message = "Bridge dispatch failed for {Method}.")]
    public static partial void DispatchFailed(ILogger logger, Exception ex, string method);

    [LoggerMessage(EventId = 197, Level = LogLevel.Debug, Message = "Dropping bridge post: WebView2 not ready.")]
    public static partial void PostDropped(ILogger logger);

    [LoggerMessage(EventId = 198, Level = LogLevel.Warning, Message = "PostWebMessageAsJson failed.")]
    public static partial void PostFailed(ILogger logger, Exception ex);

    // WebViewHostSetup (210-229)
    [LoggerMessage(EventId = 210, Level = LogLevel.Information, Message = "WebView2 runtime version {Version} detected.")]
    public static partial void RuntimeVersion(ILogger logger, string version);

    [LoggerMessage(EventId = 211, Level = LogLevel.Warning, Message = "WebView2 runtime not found.")]
    public static partial void RuntimeMissing(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 212, Level = LogLevel.Error, Message = "Could not create the WebView2 environment.")]
    public static partial void EnvFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 213, Level = LogLevel.Error, Message = "Could not initialize the WebView2 control.")]
    public static partial void ControlFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 214, Level = LogLevel.Information, Message = "WebView2 settings applied (devtools debug-default={Debug}).")]
    public static partial void SettingsApplied(ILogger logger, bool debug);

    [LoggerMessage(EventId = 215, Level = LogLevel.Information, Message = "Virtual hosts mapped: ui={Ui}, source={Source}.")]
    public static partial void HostsMapped(ILogger logger, string ui, string source);

    [LoggerMessage(EventId = 216, Level = LogLevel.Warning, Message = "Blocked navigation to non-allowlisted target.")]
    public static partial void NavBlocked(ILogger logger);

    [LoggerMessage(EventId = 217, Level = LogLevel.Warning, Message = "Blocked frame navigation to non-allowlisted target.")]
    public static partial void FrameBlocked(ILogger logger);

    [LoggerMessage(EventId = 218, Level = LogLevel.Warning, Message = "Blocked new-window request with unsupported scheme.")]
    public static partial void PopupBlocked(ILogger logger);

    [LoggerMessage(EventId = 219, Level = LogLevel.Information, Message = "Intercepted a download; routed to the app transfer flow.")]
    public static partial void DownloadIntercepted(ILogger logger);

    [LoggerMessage(EventId = 220, Level = LogLevel.Error, Message = "WebView2 process failed: {Kind}.")]
    public static partial void ProcessFailed(ILogger logger, string kind);

    [LoggerMessage(EventId = 221, Level = LogLevel.Error, Message = "Too many renderer failures; automatic reload paused. Restart the app to recover.")]
    public static partial void ReloadPaused(ILogger logger);

    [LoggerMessage(EventId = 222, Level = LogLevel.Information, Message = "WebView2 reloaded after process failure.")]
    public static partial void Reloaded(ILogger logger);

    [LoggerMessage(EventId = 223, Level = LogLevel.Error, Message = "WebView2 reload failed.")]
    public static partial void ReloadFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 224, Level = LogLevel.Warning, Message = "--ui-dev-url is ignored in Release builds.")]
    public static partial void DevUrlIgnored(ILogger logger);

    [LoggerMessage(EventId = 225, Level = LogLevel.Warning, Message = "Loading UI from dev server {Url}.")]
    public static partial void DevUrlActive(ILogger logger, string? url);

    // MainWindow (230-239)
    [LoggerMessage(EventId = 230, Level = LogLevel.Warning, Message = "Could not emit {Method}.")]
    public static partial void EmitFailed(ILogger logger, Exception ex, string method);

    [LoggerMessage(EventId = 231, Level = LogLevel.Warning, Message = "WebView2 did not initialize; window stays open for diagnostics.")]
    public static partial void WebViewNotReady(ILogger logger);

    [LoggerMessage(EventId = 232, Level = LogLevel.Error, Message = "Main window initialization failed.")]
    public static partial void InitFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 233, Level = LogLevel.Warning, Message = "Ignoring invalid background color.")]
    public static partial void BadColor(ILogger logger);

    // App composition root (240-249)
    [LoggerMessage(EventId = 240, Level = LogLevel.Warning, Message = "No usable UI folder: {Status}.")]
    public static partial void NoUsableUi(ILogger logger, string status);

    [LoggerMessage(EventId = 241, Level = LogLevel.Warning, Message = "Restart launch failed.")]
    public static partial void RestartFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 242, Level = LogLevel.Error, Message = "Error during shutdown.")]
    public static partial void ShutdownError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 243, Level = LogLevel.Critical, Message = "Startup failed.")]
    public static partial void StartupFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 244, Level = LogLevel.Error, Message = "Unhandled UI-thread exception (kept alive).")]
    public static partial void UiThreadCrash(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 245, Level = LogLevel.Critical, Message = "Unhandled non-UI exception.")]
    public static partial void FatalCrash(ILogger logger, Exception? ex);

    [LoggerMessage(EventId = 246, Level = LogLevel.Error, Message = "Unobserved background task exception.")]
    public static partial void BackgroundTaskFault(ILogger logger, Exception ex);
}
