using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;

namespace InkGuide.Bridge.Services;

// Temporary seam: canonical Application use-case services are owned by the Application
// agent (see D-006). Handlers delegate 1:1 to this backend; the host adapts it to real
// services once available. InMemoryBridgeBackend (Testing) implements it for e2e tests.

/// <summary>Backend seam behind every bridge method (thin delegation target).</summary>
public interface IBridgeBackend
{
    // app
    Task<Result<HelloResponse>> HelloAsync(HelloRequest request, CancellationToken cancellationToken);
    Task<Result<AppInfo>> GetInfoAsync(CancellationToken cancellationToken);
    Task<Result<UiStatus>> GetUiStatusAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> QuitAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> RestartAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> OpenLogsFolderAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> OpenExternalUrlAsync(string url, CancellationToken cancellationToken);

    // window
    Task<Result<EmptyResult>> SetBackgroundAsync(SetBackgroundRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> SetTitleAsync(SetTitleRequest request, CancellationToken cancellationToken);

    // settings
    Task<Result<SettingsDto>> GetSettingsAsync(CancellationToken cancellationToken);
    Task<Result<SettingsDto>> UpdateSettingsAsync(UpdateSettingsRequest request, CancellationToken cancellationToken);
    Task<Result<SettingsDto>> ResetSettingsAsync(CancellationToken cancellationToken);
    Task<Result<string?>> GetUiStateAsync(string key, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> SetUiStateAsync(UiStateRequest request, CancellationToken cancellationToken);

    // accounts
    Task<Result<IReadOnlyList<AccountDto>>> ListAccountsAsync(CancellationToken cancellationToken);
    Task<Result<AccountDto>> ConnectAsync(ConnectRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> CancelConnectAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> DisconnectAsync(string accountId, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> SetActiveAsync(string accountId, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ImportOAuthClientAsync(ImportOAuthClientRequest request, CancellationToken cancellationToken);
    Task<Result<OAuthStatusDto>> GetOAuthStatusAsync(CancellationToken cancellationToken);

    // workspace
    Task<Result<WorkspaceDto>> GetWorkspaceAsync(CancellationToken cancellationToken);
    Task<Result<WorkspaceDto>> EnsureWorkspaceAsync(CancellationToken cancellationToken);
    Task<Result<WorkspaceStatsDto>> GetWorkspaceStatsAsync(CancellationToken cancellationToken);

    // repos
    Task<Result<IReadOnlyList<RepoDto>>> ListReposAsync(CancellationToken cancellationToken);
    Task<Result<RepoDto>> CreateRepoAsync(CreateRepoRequest request, CancellationToken cancellationToken);
    Task<Result<RepoDto>> RenameRepoAsync(string id, RenameRepoRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> DeleteRepoAsync(string id, string? confirmationToken, CancellationToken cancellationToken);
    Task<Result<RepoDto>> GetRepoAsync(string id, CancellationToken cancellationToken);
    Task<Result<RepoOverviewDto>> GetRepoOverviewAsync(string id, CancellationToken cancellationToken);
    Task<Result<ExportZipResponse>> ExportZipAsync(string id, ExportZipRequest request, CancellationToken cancellationToken);
    Task<Result<RepoDto>> AdoptExistingFolderAsync(AdoptFolderRequest request, CancellationToken cancellationToken);

    // projects
    Task<Result<IReadOnlyList<ProjectDto>>> ListProjectsAsync(string repoId, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> GetProjectAsync(string id, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> ConfirmProjectAsync(string id, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> UpdateProjectAsync(string id, UpdateProjectRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> RemoveProjectAsync(string id, CancellationToken cancellationToken);
    Task<Result<ProjectDto>> MoveProjectAsync(string id, MoveProjectRequest request, CancellationToken cancellationToken);

    // nodes
    Task<Result<PagedResult<NodeDto>>> ListNodesAsync(ListNodesRequest request, CancellationToken cancellationToken);
    Task<Result<NodeDto>> GetNodeAsync(string id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<NodeDto>>> GetNodePathAsync(string id, CancellationToken cancellationToken);
    Task<Result<NodeDto>> CreateFolderAsync(string parentId, string name, string? idempotencyKey, CancellationToken cancellationToken);
    Task<Result<NodeDto>> CreateFileAsync(CreateFileRequest request, CancellationToken cancellationToken);
    Task<Result<NodeDto>> RenameNodeAsync(string id, RenameNodeRequest request, CancellationToken cancellationToken);
    Task<Result<NodeDto>> MoveNodeAsync(string id, MoveNodeRequest request, CancellationToken cancellationToken);
    Task<Result<NodeDto>> CopyNodeAsync(string id, CopyNodeRequest request, CancellationToken cancellationToken);
    Task<Result<NodeDto>> TrashNodeAsync(string id, CancellationToken cancellationToken);
    Task<Result<NodeDto>> RestoreNodeAsync(string id, CancellationToken cancellationToken);
    Task<Result<PrepareDeleteInfo>> PrepareDeleteAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> DeletePermanentAsync(string id, DeletePermanentRequest request, CancellationToken cancellationToken);
    Task<Result<ReadTextResponse>> ReadTextAsync(string id, CancellationToken cancellationToken);
    Task<Result<ReadTextResponse>> WriteTextAsync(string id, WriteTextRequest request, CancellationToken cancellationToken);
    Task<Result<PreviewDto>> GetPreviewAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> OpenExternalAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> OpenInBrowserAsync(string id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<RevisionDto>>> ListRevisionsAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> RestoreRevisionAsync(string id, RestoreRevisionRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> SetFavoriteAsync(string id, SetFavoriteRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<NodeDto>>> ListFavoritesAsync(CancellationToken cancellationToken);
    Task<Result<PagedResult<NodeDto>>> ListTrashAsync(PagedQuery query, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> EmptyTrashAsync(string? confirmationToken, CancellationToken cancellationToken);

    // transfers
    Task<Result<IReadOnlyList<TransferDto>>> UploadPathsAsync(UploadPathsRequest request, CancellationToken cancellationToken);
    Task<Result<TransferDto>> DownloadAsync(string nodeId, DownloadRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<TransferDto>>> ListTransfersAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> PauseTransferAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ResumeTransferAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> CancelTransferAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> RetryTransferAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ClearFinishedAsync(CancellationToken cancellationToken);

    // import
    Task<Result<ImportPlanDto>> PlanImportAsync(PlanImportRequest request, CancellationToken cancellationToken);
    Task<Result<string>> ExecuteImportAsync(ImportExecuteRequest request, CancellationToken cancellationToken);
    Task<Result<ImportStatusDto>> GetImportStatusAsync(string operationId, CancellationToken cancellationToken);

    // insight
    Task<Result<InsightDto>> GetInsightAsync(string nodeId, CancellationToken cancellationToken);
    Task<Result<InsightDto>> ReanalyzeAsync(string nodeId, CancellationToken cancellationToken);
    Task<Result<InsightDto>> SetSummaryAsync(string nodeId, SetSummaryRequest request, CancellationToken cancellationToken);
    Task<Result<InsightDto>> SetKindAsync(string nodeId, SetKindRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<InsightDto>>> BulkReanalyzeAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken);
    Task<Result<QueueStatusDto>> GetInsightQueueStatusAsync(CancellationToken cancellationToken);

    // tags
    Task<Result<IReadOnlyList<TagDto>>> ListTagsAsync(CancellationToken cancellationToken);
    Task<Result<TagDto>> CreateTagAsync(TagDto request, CancellationToken cancellationToken);
    Task<Result<TagDto>> UpdateTagAsync(string id, TagDto request, CancellationToken cancellationToken);
    Task<Result<TagDto>> MergeTagsAsync(string id, MergeTagsRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> DeleteTagAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> AssignTagAsync(AssignTagRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> UnassignTagAsync(AssignTagRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> BulkAssignTagAsync(BulkAssignRequest request, CancellationToken cancellationToken);
    Task<Result<int>> GetTagUsageAsync(string id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<TagDto>>> ListTagSuggestionsAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> AcceptTagSuggestionAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> DismissTagSuggestionAsync(string id, CancellationToken cancellationToken);

    // search
    Task<Result<PagedResult<SearchHitDto>>> SearchAsync(SearchQueryRequest request, CancellationToken cancellationToken);
    Task<Result<SuggestResponse>> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken);

    // sync
    Task<Result<SyncStatusDto>> GetSyncStatusAsync(CancellationToken cancellationToken);
    Task<Result<SyncStatusDto>> SyncNowAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> PauseSyncAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ResumeSyncAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<OutboxDto>>> ListOutboxAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> RetryOutboxAsync(string opId, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> DiscardOutboxAsync(string opId, string? confirmationToken, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ConflictDto>>> ListConflictsAsync(CancellationToken cancellationToken);
    Task<Result<ConflictDto>> GetConflictAsync(string id, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ResolveConflictAsync(string id, ResolveConflictRequest request, CancellationToken cancellationToken);

    // snapshots
    Task<Result<SnapshotDto>> CreateSnapshotAsync(CreateSnapshotRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<SnapshotDto>>> ListSnapshotsAsync(string repositoryId, CancellationToken cancellationToken);
    Task<Result<SnapshotDto>> GetSnapshotAsync(string id, CancellationToken cancellationToken);
    Task<Result<SnapshotDiffDto>> DiffSnapshotsAsync(string id, string otherId, CancellationToken cancellationToken);
    Task<Result<TextDiffResponse>> TextDiffAsync(TextDiffRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> RestoreSnapshotAsync(string id, RestoreSnapshotRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> DeleteSnapshotAsync(string id, string? confirmationToken, CancellationToken cancellationToken);

    // activity
    Task<Result<PagedResult<ActivityDto>>> ListActivityAsync(PagedQuery query, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> UndoAsync(UndoRequest request, CancellationToken cancellationToken);

    // organize
    Task<Result<PagedResult<NodeDto>>> ListInboxAsync(PagedQuery query, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<OrganizeSuggestionDto>>> SuggestOrganizeAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ApplyOrganizeAsync(OrganizeApplyRequest request, CancellationToken cancellationToken);
    Task<Result<SettingsDto>> GetOrganizeSettingsAsync(CancellationToken cancellationToken);

    // duplicates
    Task<Result<IReadOnlyList<DuplicateGroupDto>>> ScanDuplicatesAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<DuplicateGroupDto>>> ListDuplicatesAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ResolveDuplicatesAsync(ResolveDuplicatesRequest request, CancellationToken cancellationToken);

    // external
    Task<Result<IReadOnlyList<ExternalSessionDto>>> ListExternalSessionsAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> EndExternalSessionAsync(string id, CancellationToken cancellationToken);

    // ai
    Task<Result<AiStatusDto>> GetAiStatusAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> SetAiKeyAsync(SetAiKeyRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ClearAiKeyAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> AcknowledgeDataSharingAsync(AcknowledgeSharingRequest request, CancellationToken cancellationToken);
    Task<Result<EmptyResult>> TestAiAsync(CancellationToken cancellationToken);
    Task<Result<AiUsageDto>> GetAiUsageAsync(CancellationToken cancellationToken);

    // storage
    Task<Result<StorageUsageDto>> GetStorageUsageAsync(CancellationToken cancellationToken);
    Task<Result<CacheStatsDto>> GetCacheStatsAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ClearCacheAsync(CancellationToken cancellationToken);

    // rules
    Task<Result<CatalogInfoDto>> GetCatalogInfoAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> ReloadRulesAsync(CancellationToken cancellationToken);
    Task<Result<RulesValidationDto>> ValidateRulesAsync(ValidateRulesRequest request, CancellationToken cancellationToken);

    // diagnostics
    Task<Result<ExportBundleResponse>> ExportBundleAsync(CancellationToken cancellationToken);
    Task<Result<HealthDto>> GetHealthAsync(CancellationToken cancellationToken);
    Task<Result<EmptyResult>> RelayLogAsync(LogRequest request, CancellationToken cancellationToken);
}
