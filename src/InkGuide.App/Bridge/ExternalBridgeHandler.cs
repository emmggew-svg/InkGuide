using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.App.Hosting;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge;

/// <summary>
/// Delegates non-host-owned bridge methods to the Bridge backend module
/// (discovered by <see cref="Hosting.BridgeModuleLoader"/>) via reflection.
/// A null result means "not served"; a BridgeMethodException carries a typed
/// error (Code/Message/Retryable read reflectively so the host never depends
/// on backend types at compile time).
/// </summary>
internal sealed class ExternalBridgeHandler : IBridgeRequestRouter
{
    private readonly MethodInfo _canHandle;
    private readonly MethodInfo _handleAsync;
    private readonly ILogger _logger;

    public ExternalBridgeHandler(MethodInfo canHandle, MethodInfo handleAsync, ILogger logger)
    {
        _canHandle = canHandle;
        _handleAsync = handleAsync;
        _logger = logger;
    }

    public bool CanHandle(string method)
    {
        try
        {
            return _canHandle.Invoke(null, new object[] { method }) is true;
        }
        catch (Exception ex)
        {
            AppLogMessages.CanHandleFailed(_logger, ex, method);
            return false;
        }
    }

    public async Task<BridgeOutcome> RouteAsync(string method, string paramsJson, HostServices host, CancellationToken ct)
    {
        try
        {
            object? raw = _handleAsync.Invoke(null, new object[] { method, paramsJson, ct });
            if (raw is not Task<string?> task)
            {
                return BridgeOutcome.Fail(BridgeError.Internal("Backend returned an unexpected shape."));
            }

            string? resultJson = await task.ConfigureAwait(false);
            if (resultJson is null)
            {
                return BridgeOutcome.Fail(BridgeError.NotFound(method));
            }

            return BridgeOutcome.Ok(resultJson);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return MapBackendException(ex.InnerException);
        }
        catch (Exception ex)
        {
            AppLogMessages.HandlerThrew(_logger, ex, method);
            return BridgeOutcome.Fail(BridgeError.Internal("Backend handler failed."));
        }
    }

    private BridgeOutcome MapBackendException(Exception ex)
    {
        // Read Code/Message/Retryable reflectively: the backend owns its exception type.
        if (ex.GetType().FullName == "InkGuide.Bridge.BridgeMethodException")
        {
            string code = ReadString(ex, "Code") ?? "UNKNOWN";
            string message = ReadString(ex, "Message") ?? "Backend error.";
            bool retryable = ReadBool(ex, "Retryable");
            AppLogMessages.BackendErrorCode(_logger, code);
            return BridgeOutcome.Fail(new BridgeError(code, message, retryable, null));
        }

        AppLogMessages.HandlerUnexpected(_logger, ex);
        return BridgeOutcome.Fail(BridgeError.Internal("Backend handler failed."));
    }

    private static string? ReadString(Exception ex, string property)
    {
        PropertyInfo? prop = ex.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(ex) as string;
    }

    private static bool ReadBool(Exception ex, string property)
    {
        PropertyInfo? prop = ex.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(ex) is true;
    }
}
