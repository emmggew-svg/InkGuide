using System.Collections.Generic;

namespace InkGuide.Bridge.Contracts;

/// <summary>Account descriptor.</summary>
public sealed record AccountDto(string Id, string Email, string DisplayName, bool IsActive, bool ReauthRequired, string ScopeMode);

/// <summary>accounts.connect request.</summary>
public sealed record ConnectRequest(bool FullDrive);

/// <summary>accounts.importOAuthClient request.</summary>
public sealed record ImportOAuthClientRequest(string ClientJson);

/// <summary>accounts.getOAuthStatus response.</summary>
public sealed record OAuthStatusDto(bool Configured, string? Source);

/// <summary>Workspace descriptor.</summary>
public sealed record WorkspaceDto(string Id, string Name, string ProviderFolderId);

/// <summary>workspace.getStats response.</summary>
public sealed record WorkspaceStatsDto(int RepoCount, long TotalBytes, int NodeCount);

/// <summary>Repository descriptor.</summary>
public sealed record RepoDto(string Id, string Name, string Kind);

/// <summary>repos.create request.</summary>
public sealed record CreateRepoRequest(string Name, string Kind, string? Template, string? IdempotencyKey);

/// <summary>repos.rename request.</summary>
public sealed record RenameRepoRequest(string Name, string? IdempotencyKey);

/// <summary>repos.getOverview response.</summary>
public sealed record RepoOverviewDto(
    string Id,
    long TotalBytes,
    int FileCount,
    int FolderCount,
    IReadOnlyDictionary<string, long> BytesByKind,
    IReadOnlyList<ProjectDto> Projects,
    string? ReadmeExcerpt);

/// <summary>repos.exportZip response (host-side path chosen via native dialog).</summary>
public sealed record ExportZipResponse(string ZipPath);

/// <summary>Project descriptor.</summary>
public sealed record ProjectDto(string Id, string RepositoryId, string Path, string Name, string ProjectType, IReadOnlyList<string> Tags, bool Detected, bool Confirmed);

/// <summary>projects.update request.</summary>
public sealed record UpdateProjectRequest(string? Name, string? ProjectType, IReadOnlyList<string>? Tags, string? IdempotencyKey);

/// <summary>projects.move request.</summary>
public sealed record MoveProjectRequest(string NewPath, string? IdempotencyKey);

/// <summary>Node descriptor (cached view; preview/download via URLs, never bytes).</summary>
public sealed record NodeDto(
    string Id,
    string? ParentId,
    string Name,
    bool IsFolder,
    string? MimeType,
    long Size,
    string? Md5,
    string? Version,
    string ModifiedTime,
    bool Trashed,
    string Path,
    string? Description,
    string? PreviewUrl,
    string? DownloadUrl);

/// <summary>nodes.list request.</summary>
public sealed record ListNodesRequest(string? ParentId, string? Cursor, int Limit, string? Sort, string? Filter, bool IncludeTrashed);

/// <summary>nodes.createFile request.</summary>
public sealed record CreateFileRequest(string ParentId, string Name, string? MimeType, string? Text, string? IdempotencyKey);

/// <summary>nodes.rename / nodes.move / nodes.copy requests.</summary>
public sealed record RenameNodeRequest(string NewName, string? IdempotencyKey);

/// <summary>nodes.move request.</summary>
public sealed record MoveNodeRequest(string NewParentId, string? IdempotencyKey);

/// <summary>nodes.copy request.</summary>
public sealed record CopyNodeRequest(string NewParentId, string? NewName, string? IdempotencyKey);

/// <summary>nodes.prepareDelete response (issues the confirmation token).</summary>
public sealed record PrepareDeleteResponse(string ConfirmationToken, string Summary, IReadOnlyList<string> AffectedPaths);

/// <summary>nodes.deletePermanent request.</summary>
public sealed record DeletePermanentRequest(string ConfirmationToken, string? IdempotencyKey);

/// <summary>nodes.readText response.</summary>
public sealed record ReadTextResponse(string Text, string Encoding, string Eol, bool HasBom, string Version);

/// <summary>nodes.writeText request (conflict-safe via baseVersion).</summary>
public sealed record WriteTextRequest(string Text, string BaseVersion, string? IdempotencyKey);

/// <summary>nodes.getPreview response (URLs only).</summary>
public sealed record PreviewDto(string Kind, string? PreviewUrl, string? HtmlSnippet, IReadOnlyList<IReadOnlyList<string>>? TablePreview, string? MetadataJson);

/// <summary>nodes.revisions item.</summary>
public sealed record RevisionDto(string Id, string ModifiedTime, long Size, bool KeepForever);

/// <summary>Transfer job descriptor.</summary>
public sealed record TransferDto(string Id, string Direction, string? NodeId, string FileName, long TotalBytes, long TransferredBytes, string State, string? Error);

/// <summary>transfers.uploadPaths request.</summary>
public sealed record UploadPathsRequest(string ParentId, IReadOnlyList<string> LocalPaths, string? IdempotencyKey);

/// <summary>import.plan response (dry run).</summary>
public sealed record ImportPlanDto(int FileCount, int FolderCount, long TotalBytes, int IgnoredCount, IReadOnlyList<string> Collisions, IReadOnlyList<string> DetectedProjects, IReadOnlyList<string> SanitizedNames);

/// <summary>import.execute request.</summary>
public sealed record ImportExecuteRequest(string RepositoryId, string LocalPath, string? IdempotencyKey);

/// <summary>import.status response.</summary>
public sealed record ImportStatusDto(string State, int Done, int Total, string? Error);

/// <summary>Insight descriptor.</summary>
public sealed record InsightDto(string NodeId, string Kind, string? Role, string? Summary, double Confidence, string Source, bool Sensitive, string AnalyzedAt);

/// <summary>insight.setSummary / setKind requests.</summary>
public sealed record SetSummaryRequest(string Summary, string? IdempotencyKey);

/// <summary>insight.setKind request.</summary>
public sealed record SetKindRequest(string Kind, string? IdempotencyKey);

/// <summary>Tag descriptor (colorToken is a token, never a chosen color).</summary>
public sealed record TagDto(string Id, string Name, string? Namespace, string? Description, string Origin, string? ColorToken, bool IsSystem, int UsageCount);

/// <summary>tags.assign / unassign / bulkAssign requests.</summary>
public sealed record AssignTagRequest(string TagId, string NodeId, string? IdempotencyKey);

/// <summary>tags.bulkAssign request.</summary>
public sealed record BulkAssignRequest(string TagId, IReadOnlyList<string> NodeIds, string? IdempotencyKey);

/// <summary>tags.merge request.</summary>
public sealed record MergeTagsRequest(string IntoTagId, string? IdempotencyKey);

/// <summary>search.query request.</summary>
public sealed record SearchQueryRequest(
    string Text,
    IReadOnlyList<string>? TagIds,
    IReadOnlyList<string>? Kinds,
    string? RepoId,
    string? ProjectId,
    string? ModifiedFrom,
    string? ModifiedTo,
    long? SizeMin,
    long? SizeMax,
    bool IncludeTrashed,
    string? Cursor,
    int Limit);

/// <summary>search hit.</summary>
public sealed record SearchHitDto(string NodeId, string Name, string Path, bool IsFolder, string? Highlight);

/// <summary>Sync status descriptor.</summary>
public sealed record SyncStatusDto(string State, string? LastError, string? LastSyncAt);

/// <summary>Outbox entry descriptor.</summary>
public sealed record OutboxDto(string OpId, string OperationType, string State, int Attempts, string? LastError, string CreatedAt);

/// <summary>Conflict descriptor.</summary>
public sealed record ConflictDto(string Id, string NodeId, string Kind, string? ConflictCopyPath, string? BaseVersion, string? RemoteVersion, string? Resolution, string CreatedAt);

/// <summary>sync.resolveConflict request (+ optional merged text).</summary>
public sealed record ResolveConflictRequest(string Resolution, string? MergedText, string? IdempotencyKey);

/// <summary>Snapshot descriptor.</summary>
public sealed record SnapshotDto(string Id, string RepositoryId, string? ProjectId, string Message, string Mode, string CreatedAt);

/// <summary>snapshots.create request.</summary>
public sealed record CreateSnapshotRequest(string RepositoryId, string? ProjectId, string Message, string Mode, string? IdempotencyKey);

/// <summary>snapshots.diff response.</summary>
public sealed record SnapshotDiffDto(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Modified, IReadOnlyList<string> Moved);

/// <summary>snapshots.restore request (non-destructive).</summary>
public sealed record RestoreSnapshotRequest(IReadOnlyList<string>? Paths, string? IdempotencyKey);

/// <summary>Activity entry descriptor.</summary>
public sealed record ActivityDto(long Id, string Kind, string? NodeId, string Summary, bool Reversible, bool Undone, string OccurredAt);

/// <summary>Organizer suggestion descriptor.</summary>
public sealed record OrganizeSuggestionDto(string NodeId, string DestinationId, string DestinationPath, double Confidence, IReadOnlyList<string> Reasons);

/// <summary>organize.apply request.</summary>
public sealed record OrganizeApplyRequest(IReadOnlyList<OrganizeMoveDto> Moves, string? IdempotencyKey);

/// <summary>One organizer move.</summary>
public sealed record OrganizeMoveDto(string NodeId, string DestinationId);

/// <summary>Duplicate group descriptor.</summary>
public sealed record DuplicateGroupDto(string Md5, long Size, IReadOnlyList<NodeDto> Nodes);

/// <summary>duplicates.resolve request (trash all but kept).</summary>
public sealed record ResolveDuplicatesRequest(string KeepNodeId, IReadOnlyList<string> TrashNodeIds, string? ConfirmationToken, string? IdempotencyKey);

/// <summary>External-edit session descriptor.</summary>
public sealed record ExternalSessionDto(string SessionId, string NodeId, string FileName, string StartedAt);

/// <summary>AI status descriptor.</summary>
public sealed record AiStatusDto(string Status, int UsedToday, int BudgetPerDay, string? Model);

/// <summary>ai.setKey request.</summary>
public sealed record SetAiKeyRequest(string ApiKey);

/// <summary>ai.getUsage response.</summary>
public sealed record AiUsageDto(int UsedToday, int BudgetPerDay, string Day);

/// <summary>Generic id parameter.</summary>
public sealed record IdRequest(string Id);

/// <summary>Queue status (insight/analysis).</summary>
public sealed record QueueStatusDto(int Queued, int Running);

/// <summary>import.plan request.</summary>
public sealed record PlanImportRequest(string LocalPath, string RepositoryId);

/// <summary>snapshots.textDiff request/response.</summary>
public sealed record TextDiffRequest(string SnapshotA, string SnapshotB, string Path);

/// <summary>snapshots.textDiff response (unified diff lines).</summary>
public sealed record TextDiffResponse(IReadOnlyList<string> Lines, bool Truncated);

/// <summary>activity.undo request.</summary>
public sealed record UndoRequest(long JournalId);

/// <summary>search.suggest request/response.</summary>
public sealed record SuggestRequest(string Prefix, int Limit);

/// <summary>search.suggest response.</summary>
public sealed record SuggestResponse(IReadOnlyList<string> Suggestions);

/// <summary>ai.acknowledgeDataSharing request.</summary>
public sealed record AcknowledgeSharingRequest(bool Acknowledged);

/// <summary>rules.validate request.</summary>
public sealed record ValidateRulesRequest(string CatalogJson);

/// <summary>nodes.setFavorite request.</summary>
public sealed record SetFavoriteRequest(bool Favorite);

/// <summary>nodes.restoreRevision request.</summary>
public sealed record RestoreRevisionRequest(string RevisionId);

/// <summary>transfers.download request.</summary>
public sealed record DownloadRequest(string DestinationPath);

/// <summary>repos.exportZip request.</summary>
public sealed record ExportZipRequest(string DestinationPath);

/// <summary>repos.adoptExistingFolder request.</summary>
public sealed record AdoptFolderRequest(string ProviderFolderId, string Name);

/// <summary>nodes.createFolder request.</summary>
public sealed record CreateFolderRequest(string ParentId, string Name, string? IdempotencyKey);

/// <summary>nodes.rename request (by id).</summary>
public sealed record RenameNodeByIdRequest(string Id, string NewName, string? IdempotencyKey);

/// <summary>nodes.move request (by id).</summary>
public sealed record MoveNodeByIdRequest(string Id, string NewParentId, string? IdempotencyKey);

/// <summary>nodes.copy request (by id).</summary>
public sealed record CopyNodeByIdRequest(string Id, string NewParentId, string? NewName, string? IdempotencyKey);

/// <summary>nodes.writeText request (by id).</summary>
public sealed record WriteTextByIdRequest(string Id, string Text, string BaseVersion, string? IdempotencyKey);

/// <summary>nodes.setFavorite request (by id).</summary>
public sealed record SetFavoriteByIdRequest(string Id, bool Favorite);

/// <summary>nodes.restoreRevision request (by id).</summary>
public sealed record RestoreRevisionByIdRequest(string Id, string RevisionId);

/// <summary>nodes.deletePermanent request (by id).</summary>
public sealed record DeletePermanentByIdRequest(string Id, string? ConfirmationToken, string? IdempotencyKey);

/// <summary>repos.delete request.</summary>
public sealed record DeleteRepoRequest(string Id, string? ConfirmationToken);

/// <summary>nodes.emptyTrash request.</summary>
public sealed record EmptyTrashRequest(string? ConfirmationToken);

/// <summary>transfers.download request (by node).</summary>
public sealed record DownloadByNodeRequest(string NodeId, string DestinationPath);

/// <summary>snapshots.restore request (by id).</summary>
public sealed record RestoreSnapshotByIdRequest(string Id, IReadOnlyList<string>? Paths, string? IdempotencyKey);

/// <summary>snapshots.diff request.</summary>
public sealed record DiffSnapshotsRequest(string Id, string OtherId);

/// <summary>snapshots.delete request.</summary>
public sealed record DeleteSnapshotRequest(string Id, string? ConfirmationToken);

/// <summary>sync.discardOutbox request.</summary>
public sealed record DiscardOutboxRequest(string OpId, string? ConfirmationToken);

/// <summary>sync.resolveConflict request (by id).</summary>
public sealed record ResolveConflictByIdRequest(string Id, string Resolution, string? MergedText, string? IdempotencyKey);

/// <summary>organize.suggest request.</summary>
public sealed record OrganizeSuggestRequest(IReadOnlyList<string> NodeIds);

/// <summary>insight.bulkReanalyze request.</summary>
public sealed record BulkReanalyzeRequest(IReadOnlyList<string> NodeIds);

/// <summary>tags.merge request (by id).</summary>
public sealed record MergeTagByIdRequest(string Id, string IntoTagId, string? IdempotencyKey);

/// <summary>projects.list request.</summary>
public sealed record ListProjectsRequest(string RepositoryId);

/// <summary>snapshots.list request.</summary>
public sealed record ListSnapshotsRequest(string RepositoryId);

/// <summary>nodes.prepareDelete backend info (token issued by the handler).</summary>
public sealed record PrepareDeleteInfo(string Summary, IReadOnlyList<string> AffectedPaths);

/// <summary>repos.rename request (by id).</summary>
public sealed record RenameRepoByIdRequest(string Id, string Name, string? IdempotencyKey);

/// <summary>repos.exportZip request (by id).</summary>
public sealed record ExportZipByIdRequest(string Id, string DestinationPath);

/// <summary>projects.update request (by id).</summary>
public sealed record UpdateProjectByIdRequest(string Id, string? Name, string? ProjectType, IReadOnlyList<string>? Tags, string? IdempotencyKey);

/// <summary>projects.move request (by id).</summary>
public sealed record MoveProjectByIdRequest(string Id, string NewPath, string? IdempotencyKey);

/// <summary>insight.setSummary request (by id).</summary>
public sealed record SetSummaryByIdRequest(string Id, string Summary, string? IdempotencyKey);

/// <summary>insight.setKind request (by id).</summary>
public sealed record SetKindByIdRequest(string Id, string Kind, string? IdempotencyKey);

/// <summary>tags.update request (by id).</summary>
public sealed record UpdateTagByIdRequest(string Id, TagDto Tag);
