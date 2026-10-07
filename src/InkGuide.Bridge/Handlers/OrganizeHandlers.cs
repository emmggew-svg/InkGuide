using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Services;

namespace InkGuide.Bridge.Handlers;

/// <summary>repos.* handler.</summary>
public sealed class ReposHandler : HandlerBase
{
    public ReposHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "repos";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "repos.list" => Call(context, Backend.ListReposAsync),
            "repos.create" => Call<CreateRepoRequest, RepoDto>(paramsJson, context, Backend.CreateRepoAsync),
            "repos.rename" => Call<RenameRepoByIdRequest, RepoDto>(paramsJson, context, (req, ct) => Backend.RenameRepoAsync(req.Id, new RenameRepoRequest(req.Name, req.IdempotencyKey), ct)),
            "repos.delete" => DeleteAsync(paramsJson, context),
            "repos.get" => Call<IdRequest, RepoDto>(paramsJson, context, (req, ct) => Backend.GetRepoAsync(req.Id, ct)),
            "repos.getOverview" => Call<IdRequest, RepoOverviewDto>(paramsJson, context, (req, ct) => Backend.GetRepoOverviewAsync(req.Id, ct)),
            "repos.exportZip" => Call<ExportZipByIdRequest, ExportZipResponse>(paramsJson, context, (req, ct) => Backend.ExportZipAsync(req.Id, new ExportZipRequest(req.DestinationPath), ct)),
            "repos.adoptExistingFolder" => Call<AdoptFolderRequest, RepoDto>(paramsJson, context, Backend.AdoptExistingFolderAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }

    private async Task<Result<JsonElement?>> DeleteAsync(JsonElement? paramsJson, BridgeRequestContext context)
    {
        Result<DeleteRepoRequest> parsed = Parse<DeleteRepoRequest>(paramsJson);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Fail(parsed.Failure!);
        }

        if (!Confirmations.Validate(parsed.Value.ConfirmationToken, "repos.delete", parsed.Value.Id))
        {
            return Fail(Confirmations.Required("repos.delete", parsed.Value.Id, "Delete this repository? It is moved to trash first."));
        }

        Result<EmptyResult> result = await Backend.DeleteRepoAsync(parsed.Value.Id, parsed.Value.ConfirmationToken, context.CancellationToken).ConfigureAwait(false);
        return Wrap(result);
    }
}

/// <summary>projects.* handler.</summary>
public sealed class ProjectsHandler : HandlerBase
{
    public ProjectsHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "projects";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "projects.list" => Call<ListProjectsRequest, IReadOnlyList<ProjectDto>>(paramsJson, context, (req, ct) => Backend.ListProjectsAsync(req.RepositoryId, ct)),
            "projects.get" => Call<IdRequest, ProjectDto>(paramsJson, context, (req, ct) => Backend.GetProjectAsync(req.Id, ct)),
            "projects.confirm" => Call<IdRequest, ProjectDto>(paramsJson, context, (req, ct) => Backend.ConfirmProjectAsync(req.Id, ct)),
            "projects.update" => Call<UpdateProjectByIdRequest, ProjectDto>(paramsJson, context, (req, ct) => Backend.UpdateProjectAsync(req.Id, new UpdateProjectRequest(req.Name, req.ProjectType, req.Tags, req.IdempotencyKey), ct)),
            "projects.remove" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.RemoveProjectAsync(req.Id, ct)),
            "projects.move" => Call<MoveProjectByIdRequest, ProjectDto>(paramsJson, context, (req, ct) => Backend.MoveProjectAsync(req.Id, new MoveProjectRequest(req.NewPath, req.IdempotencyKey), ct)),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>insight.* handler.</summary>
public sealed class InsightHandler : HandlerBase
{
    public InsightHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "insight";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "insight.get" => Call<IdRequest, InsightDto>(paramsJson, context, (req, ct) => Backend.GetInsightAsync(req.Id, ct)),
            "insight.reanalyze" => Call<IdRequest, InsightDto>(paramsJson, context, (req, ct) => Backend.ReanalyzeAsync(req.Id, ct)),
            "insight.setSummary" => Call<SetSummaryByIdRequest, InsightDto>(paramsJson, context, (req, ct) => Backend.SetSummaryAsync(req.Id, new SetSummaryRequest(req.Summary, req.IdempotencyKey), ct)),
            "insight.setKind" => Call<SetKindByIdRequest, InsightDto>(paramsJson, context, (req, ct) => Backend.SetKindAsync(req.Id, new SetKindRequest(req.Kind, req.IdempotencyKey), ct)),
            "insight.bulkReanalyze" => Call<BulkReanalyzeRequest, IReadOnlyList<InsightDto>>(paramsJson, context, (req, ct) => Backend.BulkReanalyzeAsync(req.NodeIds, ct)),
            "insight.getQueueStatus" => Call(context, Backend.GetInsightQueueStatusAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>tags.* handler.</summary>
public sealed class TagsHandler : HandlerBase
{
    public TagsHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "tags";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "tags.list" => Call(context, Backend.ListTagsAsync),
            "tags.create" => Call<TagDto, TagDto>(paramsJson, context, Backend.CreateTagAsync),
            "tags.update" => Call<UpdateTagByIdRequest, TagDto>(paramsJson, context, (req, ct) => Backend.UpdateTagAsync(req.Id, req.Tag, ct)),
            "tags.merge" => Call<MergeTagByIdRequest, TagDto>(paramsJson, context, (req, ct) => Backend.MergeTagsAsync(req.Id, new MergeTagsRequest(req.IntoTagId, req.IdempotencyKey), ct)),
            "tags.delete" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.DeleteTagAsync(req.Id, ct)),
            "tags.assign" => Call<AssignTagRequest, EmptyResult>(paramsJson, context, Backend.AssignTagAsync),
            "tags.unassign" => Call<AssignTagRequest, EmptyResult>(paramsJson, context, Backend.UnassignTagAsync),
            "tags.bulkAssign" => Call<BulkAssignRequest, EmptyResult>(paramsJson, context, Backend.BulkAssignTagAsync),
            "tags.getUsage" => Call<IdRequest, int>(paramsJson, context, (req, ct) => Backend.GetTagUsageAsync(req.Id, ct)),
            "tags.listSuggestions" => Call(context, Backend.ListTagSuggestionsAsync),
            "tags.acceptSuggestion" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.AcceptTagSuggestionAsync(req.Id, ct)),
            "tags.dismissSuggestion" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.DismissTagSuggestionAsync(req.Id, ct)),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>sync.* handler.</summary>
public sealed class SyncHandler : HandlerBase
{
    public SyncHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "sync";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "sync.getStatus" => Call(context, Backend.GetSyncStatusAsync),
            "sync.syncNow" => Call(context, Backend.SyncNowAsync),
            "sync.pause" => Call(context, Backend.PauseSyncAsync),
            "sync.resume" => Call(context, Backend.ResumeSyncAsync),
            "sync.listOutbox" => Call(context, Backend.ListOutboxAsync),
            "sync.retryOutbox" => Call<IdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.RetryOutboxAsync(req.Id, ct)),
            "sync.discardOutbox" => DiscardOutboxAsync(paramsJson, context),
            "sync.listConflicts" => Call(context, Backend.ListConflictsAsync),
            "sync.getConflict" => Call<IdRequest, ConflictDto>(paramsJson, context, (req, ct) => Backend.GetConflictAsync(req.Id, ct)),
            "sync.resolveConflict" => Call<ResolveConflictByIdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.ResolveConflictAsync(req.Id, new ResolveConflictRequest(req.Resolution, req.MergedText, req.IdempotencyKey), ct)),
            _ => Task.FromResult(Unknown(method)),
        };
    }

    private async Task<Result<JsonElement?>> DiscardOutboxAsync(JsonElement? paramsJson, BridgeRequestContext context)
    {
        Result<DiscardOutboxRequest> parsed = Parse<DiscardOutboxRequest>(paramsJson);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Fail(parsed.Failure!);
        }

        if (!Confirmations.Validate(parsed.Value.ConfirmationToken, "sync.discardOutbox", parsed.Value.OpId))
        {
            return Fail(Confirmations.Required("sync.discardOutbox", parsed.Value.OpId, "Discard this queued operation? Un-synced work is lost."));
        }

        Result<EmptyResult> result = await Backend.DiscardOutboxAsync(parsed.Value.OpId, parsed.Value.ConfirmationToken, context.CancellationToken).ConfigureAwait(false);
        return Wrap(result);
    }
}

/// <summary>snapshots.* handler (DiffPlex text diffs).</summary>
public sealed class SnapshotsHandler : HandlerBase
{
    public SnapshotsHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "snapshots";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "snapshots.create" => Call<CreateSnapshotRequest, SnapshotDto>(paramsJson, context, Backend.CreateSnapshotAsync),
            "snapshots.list" => Call<ListSnapshotsRequest, IReadOnlyList<SnapshotDto>>(paramsJson, context, (req, ct) => Backend.ListSnapshotsAsync(req.RepositoryId, ct)),
            "snapshots.get" => Call<IdRequest, SnapshotDto>(paramsJson, context, (req, ct) => Backend.GetSnapshotAsync(req.Id, ct)),
            "snapshots.diff" => Call<DiffSnapshotsRequest, SnapshotDiffDto>(paramsJson, context, (req, ct) => Backend.DiffSnapshotsAsync(req.Id, req.OtherId, ct)),
            "snapshots.textDiff" => Call<TextDiffRequest, TextDiffResponse>(paramsJson, context, Backend.TextDiffAsync),
            "snapshots.restore" => Call<RestoreSnapshotByIdRequest, EmptyResult>(paramsJson, context, (req, ct) => Backend.RestoreSnapshotAsync(req.Id, new RestoreSnapshotRequest(req.Paths, req.IdempotencyKey), ct)),
            "snapshots.delete" => DeleteAsync(paramsJson, context),
            _ => Task.FromResult(Unknown(method)),
        };
    }

    private async Task<Result<JsonElement?>> DeleteAsync(JsonElement? paramsJson, BridgeRequestContext context)
    {
        Result<DeleteSnapshotRequest> parsed = Parse<DeleteSnapshotRequest>(paramsJson);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Fail(parsed.Failure!);
        }

        if (!Confirmations.Validate(parsed.Value.ConfirmationToken, "snapshots.delete", parsed.Value.Id))
        {
            return Fail(Confirmations.Required("snapshots.delete", parsed.Value.Id, "Delete this snapshot record? Copied data may remain on Drive."));
        }

        Result<EmptyResult> result = await Backend.DeleteSnapshotAsync(parsed.Value.Id, parsed.Value.ConfirmationToken, context.CancellationToken).ConfigureAwait(false);
        return Wrap(result);
    }
}

/// <summary>activity.* handler.</summary>
public sealed class ActivityHandler : HandlerBase
{
    public ActivityHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "activity";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "activity.list" => Call<PagedQuery, PagedResult<ActivityDto>>(paramsJson, context, Backend.ListActivityAsync),
            "activity.undo" => Call<UndoRequest, EmptyResult>(paramsJson, context, Backend.UndoAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>organize.* handler.</summary>
public sealed class OrganizeHandler : HandlerBase
{
    public OrganizeHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "organize";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "organize.listInbox" => Call<PagedQuery, PagedResult<NodeDto>>(paramsJson, context, Backend.ListInboxAsync),
            "organize.suggest" => Call<OrganizeSuggestRequest, IReadOnlyList<OrganizeSuggestionDto>>(paramsJson, context, (req, ct) => Backend.SuggestOrganizeAsync(req.NodeIds, ct)),
            "organize.apply" => Call<OrganizeApplyRequest, EmptyResult>(paramsJson, context, Backend.ApplyOrganizeAsync),
            "organize.getSettings" => Call(context, Backend.GetOrganizeSettingsAsync),
            _ => Task.FromResult(Unknown(method)),
        };
    }
}

/// <summary>duplicates.* handler.</summary>
public sealed class DuplicatesHandler : HandlerBase
{
    public DuplicatesHandler(IBridgeBackend backend, ConfirmationService confirmations)
        : base(backend, confirmations)
    {
    }

    /// <inheritdoc />
    public override string Namespace => "duplicates";

    /// <inheritdoc />
    public override Task<Result<JsonElement?>> HandleAsync(string method, JsonElement? paramsJson, BridgeRequestContext context)
    {
        return method switch
        {
            "duplicates.scan" => Call(context, Backend.ScanDuplicatesAsync),
            "duplicates.list" => Call(context, Backend.ListDuplicatesAsync),
            "duplicates.resolve" => ResolveAsync(paramsJson, context),
            _ => Task.FromResult(Unknown(method)),
        };
    }

    private async Task<Result<JsonElement?>> ResolveAsync(JsonElement? paramsJson, BridgeRequestContext context)
    {
        Result<ResolveDuplicatesRequest> parsed = Parse<ResolveDuplicatesRequest>(paramsJson);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            return Fail(parsed.Failure!);
        }

        if (!Confirmations.Validate(parsed.Value.ConfirmationToken, "duplicates.resolve", parsed.Value.KeepNodeId))
        {
            return Fail(Confirmations.Required("duplicates.resolve", parsed.Value.KeepNodeId, "Trash the duplicate files, keeping the chosen one?"));
        }

        Result<EmptyResult> result = await Backend.ResolveDuplicatesAsync(parsed.Value, context.CancellationToken).ConfigureAwait(false);
        return Wrap(result);
    }
}
