using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Services;

namespace InkGuide.Bridge.Handlers;

/// <summary>nodes.* handler (confirmation-guarded destructive ops).</summary>
public sealed class NodesHandler : HandlerBase
{
    public NodesHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "nodes";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "nodes.list" => Call<ListNodesRequest, PagedResult<NodeDto>>(paramsJson, context, Backend.ListNodesAsync),
            "nodes.get" => Call<IdRequest, NodeDto>(paramsJson, context, (req, ct) => Backend.GetNodeAsync(req.Id, ct)),
            "nodes.getPath" => Call<IdRequest, IReadOnlyList<NodeDto>>(paramsJson, context, (req, ct) => Backend.GetNodePathAsync(req.Id, ct)),
            "nodes.createFolder" => Call<CreateFolderRequest, NodeDto>(paramsJson, context, (req, ct) => Backend.CreateFolderAsync(req.ParentId, req.Name, req.IdempotencyKey, ct)),
            "nodes.createFile" => Call<CreateFileRequest, NodeDto>(paramsJson, context, Backend.CreateFileAsync),
            "nodes.rename" => Call<RenameNodeByIdRequest, NodeDto>(paramsJson, context, (req, ct) => Backend.RenameNodeAsync(req.Id, new RenameNodeRequest(req.NewName, req.IdempotencyKey), ct)),
            "nodes.move" => Call<MoveNodeByIdRequest, NodeDto>(paramsJson, context, (req, ct) => Backend.MoveNodeAsync(req.Id, new MoveNodeRequest(req.NewParentId, req.IdempotencyKey), ct)),
            "nodes.copy" => Call<CopyNodeByIdRequest, NodeDto>(paramsJson, context, (req, ct) => Backend.CopyNodeAsync(req.Id, new CopyNodeRequest(req.NewParentId, req.NewName, req.IdempotencyKey), ct)),
            "nodes.trash" => Call<IdRequest, NodeDto>(paramsJson, context, (req, ct) => Backend.TrashNodeAsync(req.Id, ct)),
            "nodes.restore" => Call<IdRequest, NodeDto>(paramsJson, context, (req, ct) => Backend.RestoreNodeAsync(req.Id, ct)),
            "nodes.prepareDelete" => PrepareDeleteAsync(paramsJson, context),
            "nodes.deletePermanent" => DeletePermanentAsync(paramsJson, context),
            "nodes.readText" => Call<IdRequest, ReadTextResponse>(paramsJson, context, (req, ct) => Backend.ReadTextAsync(req.Id, ct)),
            "nodes.writeText" => Call<WriteTextByIdRequest, ReadTextResponse>(paramsJson, context, (req, ct) => Backend.WriteTextAsync(req.Id, new WriteTextRequest(req.Text, req.BaseVersion, req.IdempotencyKey), ct)),
            "nodes.getPreview" => Call<IdRequest, PreviewDto>(paramsJson, context, (req, ct) => Backend.GetPreviewAsync(req.Id, ct)),
            "nodes.openExternal" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.OpenExternalAsync(req.Id, ct)),
            "nodes.openInBrowser" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.OpenInBrowserAsync(req.Id, ct)),
            "nodes.revisions" => Call<IdRequest, IReadOnlyList<RevisionDto>>(paramsJson, context, (req, ct) => Backend.ListRevisionsAsync(req.Id, ct)),
            "nodes.restoreRevision" => Call<RestoreRevisionByIdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.RestoreRevisionAsync(req.Id, new RestoreRevisionRequest(req.RevisionId), ct)),
            "nodes.setFavorite" => Call<SetFavoriteByIdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.SetFavoriteAsync(req.Id, new SetFavoriteRequest(req.Favorite), ct)),
            "nodes.listFavorites" => Call(context, Backend.ListFavoritesAsync),
            "nodes.listTrash" => Call<PagedQuery, PagedResult<NodeDto>>(paramsJson, context, Backend.ListTrashAsync),
            "nodes.emptyTrash" => EmptyTrashAsync(paramsJson, context),
            _ => Task.FromResult(Unknown(method)),
        };
    }

    private async Task<Result<JsonElement?>> PrepareDeleteAsync(JsonElement? paramsJson, BridgeRequestContext context)
    {
        Result<IdRequest> parsed = Parse<IdRequest>(paramsJson);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Fail(parsed.Failure!);
        }

        Result<PrepareDeleteInfo> info = await Backend.PrepareDeleteAsync(parsed.Value.Id, context.CancellationToken).ConfigureAwait(false);
        if (!info.IsSuccess || info.Value is null)
        {
            return Fail(info.Failure!);
        }

        string token = Confirmations.Issue("nodes.deletePermanent", parsed.Value.Id);
        return Wrap(Result.Ok(new PrepareDeleteResponse(token, info.Value.Summary, info.Value.AffectedPaths)));
    }

    private async Task<Result<JsonElement?>> DeletePermanentAsync(JsonElement? paramsJson, BridgeRequestContext context)
    {
        Result<DeletePermanentByIdRequest> parsed = Parse<DeletePermanentByIdRequest>(paramsJson);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Fail(parsed.Failure!);
        }

        if (!Confirmations.Validate(parsed.Value.ConfirmationToken, "nodes.deletePermanent", parsed.Value.Id))
        {
            return Fail(Confirmations.Required("nodes.deletePermanent", parsed.Value.Id, "Permanently delete this item? This cannot be undone."));
        }

        Result<EmptyResult> result = await Backend.DeletePermanentAsync(parsed.Value.Id, new DeletePermanentRequest(parsed.Value.ConfirmationToken!, parsed.Value.IdempotencyKey), context.CancellationToken).ConfigureAwait(false);
        return Wrap(result);
    }

    private async Task<Result<JsonElement?>> EmptyTrashAsync(JsonElement? paramsJson, BridgeRequestContext context)
    {
        Result<EmptyTrashRequest> parsed = Parse<EmptyTrashRequest>(paramsJson);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Fail(parsed.Failure!);
        }

        if (!Confirmations.Validate(parsed.Value.ConfirmationToken, "nodes.emptyTrash", "trash"))
        {
            return Fail(Confirmations.Required("nodes.emptyTrash", "trash", "Permanently delete everything in trash? This cannot be undone."));
        }

        Result<EmptyResult> result = await Backend.EmptyTrashAsync(parsed.Value.ConfirmationToken, context.CancellationToken).ConfigureAwait(false);
        return Wrap(result);
    }
}

/// <summary>transfers.* handler.</summary>
public sealed class TransfersHandler : HandlerBase
{
    public TransfersHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "transfers";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "transfers.uploadPaths" => Call<UploadPathsRequest, IReadOnlyList<TransferDto>>(paramsJson, context, Backend.UploadPathsAsync),
            "transfers.download" => Call<DownloadByNodeRequest, TransferDto>(paramsJson, context, (req, ct) => Backend.DownloadAsync(req.NodeId, new DownloadRequest(req.DestinationPath), ct)),
            "transfers.list" => Call(context, Backend.ListTransfersAsync),
            "transfers.pause" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.PauseTransferAsync(req.Id, ct)),
            "transfers.resume" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.ResumeTransferAsync(req.Id, ct)),
            "transfers.cancel" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.CancelTransferAsync(req.Id, ct)),
            "transfers.retry" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.RetryTransferAsync(req.Id, ct)),
            "transfers.clearFinished" => Call(context, Backend.ClearFinishedAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>import.* handler.</summary>
public sealed class ImportHandler : HandlerBase
{
    public ImportHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "import";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "import.plan" => Call<PlanImportRequest, ImportPlanDto>(paramsJson, context, Backend.PlanImportAsync),
            "import.execute" => Call<ImportExecuteRequest, string>(paramsJson, context, Backend.ExecuteImportAsync),
            "import.status" => Call<IdRequest, ImportStatusDto>(paramsJson, context, (req, ct) => Backend.GetImportStatusAsync(req.Id, ct)),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>external.* handler.</summary>
public sealed class ExternalHandler : HandlerBase
{
    public ExternalHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "external";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "external.listSessions" => Call(context, Backend.ListExternalSessionsAsync),
            "external.endSession" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.EndExternalSessionAsync(req.Id, ct)),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>search.* handler.</summary>
public sealed class SearchHandler : HandlerBase
{
    public SearchHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "search";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "search.query" => Call<SearchQueryRequest, PagedResult<SearchHitDto>>(paramsJson, context, Backend.SearchAsync),
            "search.suggest" => Call<SuggestRequest, SuggestResponse>(paramsJson, context, Backend.SuggestAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}
