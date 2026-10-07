using System.Text.Json;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Services;

namespace InkGuide.Bridge.Handlers;

/// <summary>app.* handler.</summary>
public sealed class AppHandler : HandlerBase
{
    public AppHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "app";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "app.hello" => Call<HelloRequest, HelloResponse>(paramsJson, context, Backend.HelloAsync),
            "app.getInfo" => Call(context, Backend.GetInfoAsync),
            "app.getUiStatus" => Call(context, Backend.GetUiStatusAsync),
            "app.quit" => Call(context, Backend.QuitAsync),
            "app.restart" => Call(context, Backend.RestartAsync),
            "app.openLogsFolder" => Call(context, Backend.OpenLogsFolderAsync),
            "app.openExternalUrl" => Call<OpenUrlRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.OpenExternalUrlAsync(req.Url, ct)),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>window.* handler.</summary>
public sealed class WindowHandler : HandlerBase
{
    public WindowHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "window";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "window.setBackground" => Call<SetBackgroundRequest, EmptyResult>(paramsJson, context, Backend.SetBackgroundAsync),
            "window.setTitle" => Call<SetTitleRequest, EmptyResult>(paramsJson, context, Backend.SetTitleAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>settings.* handler.</summary>
public sealed class SettingsHandler : HandlerBase
{
    public SettingsHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "settings";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "settings.get" => Call(context, Backend.GetSettingsAsync),
            "settings.update" => Call<UpdateSettingsRequest, SettingsDto>(paramsJson, context, Backend.UpdateSettingsAsync),
            "settings.reset" => Call(context, Backend.ResetSettingsAsync),
            "settings.getUiState" => Call<UiStateRequest, string?>(paramsJson, context, (req, ct) => Backend.GetUiStateAsync(req.Key, ct)),
            "settings.setUiState" => Call<UiStateRequest, EmptyResult>(paramsJson, context, Backend.SetUiStateAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>diagnostics.* handler.</summary>
public sealed class DiagnosticsHandler : HandlerBase
{
    public DiagnosticsHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "diagnostics";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "diagnostics.exportBundle" => Call(context, Backend.ExportBundleAsync),
            "diagnostics.getHealth" => Call(context, Backend.GetHealthAsync),
            "diagnostics.log" => Call<LogRequest, EmptyResult>(paramsJson, context, Backend.RelayLogAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>rules.* handler.</summary>
public sealed class RulesHandler : HandlerBase
{
    public RulesHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "rules";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "rules.getCatalogInfo" => Call(context, Backend.GetCatalogInfoAsync),
            "rules.reload" => Call(context, Backend.ReloadRulesAsync),
            "rules.validate" => Call<ValidateRulesRequest, RulesValidationDto>(paramsJson, context, Backend.ValidateRulesAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>storage.* handler.</summary>
public sealed class StorageHandler : HandlerBase
{
    public StorageHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "storage";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "storage.getUsage" => Call(context, Backend.GetStorageUsageAsync),
            "storage.getCacheStats" => Call(context, Backend.GetCacheStatsAsync),
            "storage.clearCache" => Call(context, Backend.ClearCacheAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}
