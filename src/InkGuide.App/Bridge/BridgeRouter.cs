using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.App.Hosting;

namespace InkGuide.App.Bridge;

/// <summary>
/// Outcome of routing one bridge request. Expected failures are values
/// (BridgeError), never exceptions; exceptions are reserved for bugs.
/// </summary>
internal sealed record BridgeOutcome(bool IsError, string PayloadJson, BridgeError? Error)
{
    public static BridgeOutcome Ok(string resultJson) => new(false, resultJson, null);
    public static BridgeOutcome Fail(BridgeError error) => new(true, "{}", error);
}

/// <summary>
/// Host-side seam between the WebView2 transport and request handlers.
/// The App implements host-owned methods (app.*, window.*, diagnostics.log);
/// the Bridge backend module (loaded via <see cref="Hosting.BridgeModuleLoader"/>)
/// serves the domain namespaces. See docs/ARCHITECTURE.md for the contract.
/// </summary>
internal interface IBridgeRequestRouter
{
    bool CanHandle(string method);
    Task<BridgeOutcome> RouteAsync(string method, string paramsJson, HostServices host, CancellationToken ct);
}

/// <summary>Host capabilities handed to routers (no business logic).</summary>
internal sealed record HostServices(
    AppPaths Paths,
    CliOptions CliOptions,
    UiResolution UiResolution,
    string HostVersion,
    Func<string, Task> SetBackgroundAsync,
    Func<string, Task> SetTitleAsync,
    Action<string> OpenExternalUrl,
    Action Quit,
    Action Restart,
    Func<string, string, Task> EmitEventAsync);
