using System.Text.Json;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Services;

namespace InkGuide.Bridge.Handlers;

/// <summary>accounts.* handler.</summary>
public sealed class AccountsHandler : HandlerBase
{
    public AccountsHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "accounts";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "accounts.list" => Call(context, Backend.ListAccountsAsync),
            "accounts.connect" => Call<ConnectRequest, AccountDto>(paramsJson, context, Backend.ConnectAsync),
            "accounts.cancelConnect" => Call(context, Backend.CancelConnectAsync),
            "accounts.disconnect" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.DisconnectAsync(req.Id, ct)),
            "accounts.setActive" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.SetActiveAsync(req.Id, ct)),
            "accounts.importOAuthClient" => Call<ImportOAuthClientRequest, EmptyResult>(paramsJson, context, Backend.ImportOAuthClientAsync),
            "accounts.getOAuthStatus" => Call(context, Backend.GetOAuthStatusAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>workspace.* handler.</summary>
public sealed class WorkspaceHandler : HandlerBase
{
    public WorkspaceHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "workspace";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "workspace.get" => Call(context, Backend.GetWorkspaceAsync),
            "workspace.ensure" => Call(context, Backend.EnsureWorkspaceAsync),
            "workspace.getStats" => Call(context, Backend.GetWorkspaceStatsAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>ai.* handler.</summary>
public sealed class AiHandler : HandlerBase
{
    public AiHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "ai";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "ai.getStatus" => Call(context, Backend.GetAiStatusAsync),
            "ai.setKey" => Call<SetAiKeyRequest, EmptyResult>(paramsJson, context, Backend.SetAiKeyAsync),
            "ai.clearKey" => Call(context, Backend.ClearAiKeyAsync),
            "ai.acknowledgeDataSharing" => Call<AcknowledgeSharingRequest, EmptyResult>(paramsJson, context, Backend.AcknowledgeDataSharingAsync),
            "ai.test" => Call(context, Backend.TestAiAsync),
            "ai.getUsage" => Call(context, Backend.GetAiUsageAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}
