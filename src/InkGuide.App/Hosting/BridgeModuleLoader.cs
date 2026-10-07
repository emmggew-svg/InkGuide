using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Hosting;

/// <summary>
/// Loads the optional Bridge backend module without a compile-time dependency
/// (InkGuide.Bridge must not reference the WPF host, and the host must build
/// before the Bridge contracts land). Discovery contract — also documented in
/// docs/ARCHITECTURE.md for the Bridge owner:
/// <list type="bullet">
/// <item>type <c>InkGuide.Bridge.BridgeModule</c> (public static class), with</item>
/// <item><c>public static void Configure(IServiceCollection services)</c> (optional),</item>
/// <item><c>public static bool CanHandle(string method)</c>,</item>
/// <item><c>public static Task&lt;string?&gt; HandleAsync(string method, string paramsJson, CancellationToken ct)</c>
/// returning result-payload JSON, or null when the method is not served. Typed
/// failures throw <c>InkGuide.Bridge.BridgeMethodException</c> exposing
/// string Code, string Message and bool Retryable.</item>
/// </list>
/// Absence of the module is a normal, logged state — not an error.
/// </summary>
internal sealed class BridgeModuleLoader
{
    private readonly ILogger<BridgeModuleLoader> _logger;

    public BridgeModuleLoader(ILogger<BridgeModuleLoader> logger)
    {
        _logger = logger;
    }

    public void ConfigureBackend(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        MethodInfo? configure = FindMethod("Configure", new[] { typeof(IServiceCollection) }, typeof(void));
        if (configure is null)
        {
            AppLogMessages.NoConfigure(_logger);
            return;
        }

        try
        {
            configure.Invoke(null, new object[] { services });
            AppLogMessages.BackendRegistered(_logger);
        }
        catch (Exception ex)
        {
            AppLogMessages.ConfigureFailed(_logger, ex);
        }
    }

    public Bridge.ExternalBridgeHandler? CreateHandler()
    {
        Type? module = FindModuleType();
        if (module is null)
        {
            return null;
        }

        MethodInfo? canHandle = FindMethodOn(module, "CanHandle", new[] { typeof(string) }, typeof(bool));
        MethodInfo? handleAsync = FindMethodOn(module, "HandleAsync",
            new[] { typeof(string), typeof(string), typeof(CancellationToken) }, typeof(Task<string?>));
        if (canHandle is null || handleAsync is null)
        {
            AppLogMessages.BadEntryPoints(_logger);
            return null;
        }

        AppLogMessages.BackendLoaded(_logger);
        return new Bridge.ExternalBridgeHandler(canHandle, handleAsync, _logger);
    }

    private static Type? FindModuleType()
    {
        try
        {
            Assembly bridge = typeof(App).Assembly
                .GetReferencedAssemblies()
                .Select(name =>
                {
                    try
                    {
                        return Assembly.Load(name);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                })
                .FirstOrDefault(a => a is not null && a.GetName().Name == "InkGuide.Bridge")
                ?? Assembly.Load("InkGuide.Bridge");

            return bridge.GetType("InkGuide.Bridge.BridgeModule", throwOnError: false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static MethodInfo? FindMethod(string name, Type[] parameters, Type returnType)
    {
        Type? module = FindModuleType();
        return module is null ? null : FindMethodOn(module, name, parameters, returnType);
    }

    private static MethodInfo? FindMethodOn(Type module, string name, Type[] parameters, Type returnType)
    {
        MethodInfo? method = module.GetMethod(name, BindingFlags.Public | BindingFlags.Static, parameters);
        if (method is null || method.ReturnType != returnType)
        {
            return null;
        }

        return method;
    }
}
