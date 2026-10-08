using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Storage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    private const int MaxSnapshotFiles = 20000;
    private const int MaxDiffLines = 500;

    public async Task<Result<SnapshotDto>> CreateSnapshotAsync(CreateSnapshotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        RepositoryEntity? repo = await FindRepoAsync(accountId, request.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<SnapshotDto>(BackendResults.NotFound("Repository was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<SnapshotDto>(resolved.Failure!);
        }

        string mode = string.IsNullOrWhiteSpace(request.Mode) ? await SnapshotDefaultModeAsync(cancellationToken).ConfigureAwait(false) : request.Mode.Trim().ToLowerInvariant();
        if (mode != "pin" && mode != "copy")
        {
            return Result.Fail<SnapshotDto>(BackendResults.InvalidParams("Mode must be pin or copy."));
        }

        string snapshotId = "snap-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12];
        List<SnapshotEntryEntity> entries = new();
        var queue = new Queue<(string FolderId, string Prefix)>();
        queue.Enqueue((repo.ProviderFolderId, string.Empty));
        int listed = 0;
        while (queue.Count > 0 && listed < MaxSnapshotFiles)
        {
            (string folderId, string prefix) = queue.Dequeue();
            string? cursor = null;
            do
            {
                var page = await resolved.Value.Provider.ListChildrenAsync(folderId, cursor, 200, cancellationToken).ConfigureAwait(false);
                if (!page.IsSuccess || page.Value is null)
                {
                    return Result.Fail<SnapshotDto>(BackendResults.ToBridge(page.Failure!));
                }

                foreach (var item in page.Value.Items)
                {
                    if (item.Trashed || string.Equals(item.Name, ".inkguide", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string path = string.IsNullOrEmpty(prefix) ? item.Name : prefix + "/" + item.Name;
                    listed++;
                    if (item.IsFolder)
                    {
                        queue.Enqueue((item.Id, path));
                        continue;
                    }

                    string? revisionId = item.HeadRevisionId ?? item.Version;
                    string? copyNodeId = null;
                    bool pinned = false;
                    if (mode == "pin" && !string.IsNullOrEmpty(revisionId))
                    {
                        var pin = await resolved.Value.Provider.PinRevisionAsync(item.Id, revisionId, true, cancellationToken).ConfigureAwait(false);
                        pinned = pin.IsSuccess;
                    }

                    if (mode == "copy" || !pinned)
                    {
                        copyNodeId = await CopyForSnapshotAsync(resolved.Value.Provider, repo.ProviderFolderId, snapshotId, path, item.Id, cancellationToken).ConfigureAwait(false);
                    }

                    entries.Add(new SnapshotEntryEntity
                    {
                        SnapshotId = snapshotId,
                        NodeId = item.Id,
                        Path = path,
                        Size = item.Size,
                        Md5 = item.Md5,
                        HeadRevisionId = revisionId,
                        CopyNodeId = copyNodeId,
                    });
                }

                cursor = page.Value.NextCursor;
            }
            while (cursor is not null && listed < MaxSnapshotFiles);
        }

        string manifestJson = JsonSerializer.Serialize(entries.Select(e => new { e.Path, e.NodeId, e.Size, e.Md5, e.HeadRevisionId, e.CopyNodeId }), JsonOptions);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.Snapshots.Add(new SnapshotEntity
            {
                Id = snapshotId,
                AccountId = accountId,
                RepositoryId = repo.Id,
                ProjectId = request.ProjectId,
                Message = request.Message ?? string.Empty,
                Mode = mode,
                ManifestJson = manifestJson,
                CreatedAtUtc = _clock.GetUtcNow(),
            });
            foreach (SnapshotEntryEntity entry in entries)
            {
                db.SnapshotEntries.Add(entry);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "snapshots.create", repo.ProviderFolderId, "Snapshot '" + (request.Message ?? snapshotId) + "' (" + entries.Count + " files, " + mode + ").", false, null, null, cancellationToken).ConfigureAwait(false);

        SnapshotEntity created = await GetSnapshotRowAsync(snapshotId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Snapshot row vanished after save.");
        return Result.Ok(ToSnapshotDto(created));
    }

    public async Task<Result<IReadOnlyList<SnapshotDto>>> ListSnapshotsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoryId);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var list = await _misc.ListSnapshotsAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess || list.Value is null)
        {
            return Result.Fail<IReadOnlyList<SnapshotDto>>(BackendResults.ToBridge(list.Failure!));
        }

        var dtos = new List<SnapshotDto>();
        foreach (SnapshotEntity row in list.Value)
        {
            if (string.Equals(row.AccountId, accountId, StringComparison.Ordinal))
            {
                dtos.Add(ToSnapshotDto(row));
            }
        }

        return Result.Ok<IReadOnlyList<SnapshotDto>>(dtos);
    }

    public async Task<Result<SnapshotDto>> GetSnapshotAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        SnapshotEntity? row = await GetSnapshotRowAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null || !string.Equals(row.AccountId, accountId, StringComparison.Ordinal))
        {
            return Result.Fail<SnapshotDto>(BackendResults.NotFound("Snapshot '" + id + "' was not found."));
        }

        return Result.Ok(ToSnapshotDto(row));
    }

    public async Task<Result<SnapshotDiffDto>> DiffSnapshotsAsync(string id, string otherId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(otherId);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, SnapshotEntryView>? left = await SnapshotEntriesAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        Dictionary<string, SnapshotEntryView>? right = await SnapshotEntriesAsync(accountId, otherId, cancellationToken).ConfigureAwait(false);
        if (left is null || right is null)
        {
            return Result.Fail<SnapshotDiffDto>(BackendResults.NotFound("Snapshot was not found."));
        }

        var added = new List<string>();
        var removed = new List<string>();
        var modified = new List<string>();
        var moved = new List<string>();
        var rightByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, SnapshotEntryView> kv in right)
        {
            rightByNode[kv.Value.NodeId + "|" + kv.Value.Md5] = kv.Key;
        }

        foreach (KeyValuePair<string, SnapshotEntryView> kv in left)
        {
            if (!right.TryGetValue(kv.Key, out SnapshotEntryView? other))
            {
                // Same content elsewhere? then it moved/renamed.
                if (rightByNode.TryGetValue(kv.Value.NodeId + "|" + kv.Value.Md5, out string? newPath) && newPath is not null)
                {
                    moved.Add(kv.Key + " -> " + newPath);
                }
                else
                {
                    removed.Add(kv.Key);
                }
            }
            else if (!string.Equals(kv.Value.Md5, other.Md5, StringComparison.Ordinal) || kv.Value.Size != other.Size)
            {
                modified.Add(kv.Key);
            }
        }

        var leftPaths = new HashSet<string>(left.Keys, StringComparer.Ordinal);
        foreach (string path in right.Keys)
        {
            if (!leftPaths.Contains(path) && !moved.Exists(m => m.EndsWith(" -> " + path, StringComparison.Ordinal)))
            {
                added.Add(path);
            }
        }

        return Result.Ok(new SnapshotDiffDto(added, removed, modified, moved));
    }

    public async Task<Result<TextDiffResponse>> TextDiffAsync(TextDiffRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<string> left = await SnapshotFileTextAsync(accountId, request.SnapshotA, request.Path, cancellationToken).ConfigureAwait(false);
        if (!left.IsSuccess)
        {
            return Result.Fail<TextDiffResponse>(left.Failure!);
        }

        Result<string> right = await SnapshotFileTextAsync(accountId, request.SnapshotB, request.Path, cancellationToken).ConfigureAwait(false);
        if (!right.IsSuccess)
        {
            return Result.Fail<TextDiffResponse>(right.Failure!);
        }

        DiffPlex.DiffBuilder.Model.DiffPaneModel pane;
        try
        {
            pane = DiffPlex.DiffBuilder.InlineDiffBuilder.Diff(
                left.Value ?? string.Empty,
                right.Value ?? string.Empty,
                true,
                false,
                DiffPlex.Chunkers.LineChunker.Instance);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            return Result.Fail<TextDiffResponse>(BackendResults.Internal("Diff failed: " + ex.Message));
        }

        var lines = new List<string>();
        bool truncated = false;

        foreach (DiffPlex.DiffBuilder.Model.DiffPiece piece in pane.Lines)
        {
            if (lines.Count >= MaxDiffLines)
            {
                truncated = true;
                break;
            }

            lines.Add(piece.Type switch
            {
                DiffPlex.DiffBuilder.Model.ChangeType.Inserted => "+" + piece.Text,
                DiffPlex.DiffBuilder.Model.ChangeType.Deleted => "-" + piece.Text,
                _ => " " + piece.Text,
            });
        }

        return Result.Ok(new TextDiffResponse(lines, truncated));
    }

    public async Task<Result<EmptyResult>> RestoreSnapshotAsync(string id, RestoreSnapshotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        SnapshotEntity? snapshot = await GetSnapshotRowAsync(id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || !string.Equals(snapshot.AccountId, accountId, StringComparison.Ordinal))
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Snapshot '" + id + "' was not found."));
        }

        RepositoryEntity? repo = await FindRepoAsync(accountId, snapshot.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Repository was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<SnapshotEntryEntity> entries = await db.SnapshotEntries.AsNoTracking().Where(e => e.SnapshotId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var wanted = request.Paths is null ? null : new HashSet<string>(request.Paths, StringComparer.Ordinal);
        var unrestorable = new List<string>();
        int restored = 0;
        foreach (SnapshotEntryEntity entry in entries)
        {
            if (wanted is not null && !wanted.Contains(entry.Path))
            {
                continue;
            }

            var bytes = await SnapshotEntryBytesAsync(resolved.Value.Provider, entry, cancellationToken).ConfigureAwait(false);
            if (!bytes.IsSuccess || bytes.Value is null)
            {
                unrestorable.Add(entry.Path);
                continue;
            }

            string? targetId = await FindNodeByRepoPathAsync(accountId, repo.ProviderFolderId, entry.Path, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
            if (targetId is null)
            {
                // Recreate missing files (parents first).
                string? dir = entry.Path.Contains('/') ? entry.Path[..entry.Path.LastIndexOf('/')] : null;
                string parentId = repo.ProviderFolderId;
                if (dir is not null)
                {
                    string? created = await EnsureRemotePathAsync(resolved.Value.Provider, repo.ProviderFolderId, dir, cancellationToken).ConfigureAwait(false);
                    if (created is null)
                    {
                        unrestorable.Add(entry.Path);
                        continue;
                    }

                    parentId = created;
                }

                string fileName = entry.Path.Contains('/') ? entry.Path[(entry.Path.LastIndexOf('/') + 1)..] : entry.Path;
                await using var createStream = new MemoryStream(bytes.Value, writable: false);
                var uploaded = await resolved.Value.Provider.UploadAsync(parentId, fileName, createStream, "application/octet-stream", null, null, cancellationToken).ConfigureAwait(false);
                if (!uploaded.IsSuccess || uploaded.Value is null)
                {
                    unrestorable.Add(entry.Path);
                    continue;
                }

                await MirrorNodeAsync(accountId, uploaded.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var remote = await resolved.Value.Provider.GetAsync(targetId, cancellationToken).ConfigureAwait(false);
                if (!remote.IsSuccess || remote.Value is null)
                {
                    unrestorable.Add(entry.Path);
                    continue;
                }

                if (remote.Value.Trashed)
                {
                    _ = await resolved.Value.Provider.RestoreAsync(targetId, cancellationToken).ConfigureAwait(false);
                    remote = await resolved.Value.Provider.GetAsync(targetId, cancellationToken).ConfigureAwait(false);
                    if (!remote.IsSuccess || remote.Value is null)
                    {
                        unrestorable.Add(entry.Path);
                        continue;
                    }
                }

                await using var updateStream = new MemoryStream(bytes.Value, writable: false);
                var updated = await resolved.Value.Provider.UpdateContentAsync(targetId, updateStream, remote.Value.Version, null, null, cancellationToken).ConfigureAwait(false);
                if (!updated.IsSuccess || updated.Value is null)
                {
                    unrestorable.Add(entry.Path);
                    continue;
                }

                await MirrorNodeAsync(accountId, updated.Value, cancellationToken).ConfigureAwait(false);
            }

            restored++;
        }

        await JournalAsync(accountId, "snapshots.restore", repo.ProviderFolderId, "Snapshot restored (" + restored + " files).", false, null, null, cancellationToken).ConfigureAwait(false);
        if (unrestorable.Count > 0)
        {
            return Result.Fail<EmptyResult>(BackendResults.Internal("Restored " + restored + " files; cannot restore (permanently deleted without a copy): " + string.Join(", ", unrestorable) + "."));
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> DeleteSnapshotAsync(string id, string? confirmationToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        _ = confirmationToken;
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        SnapshotEntity? snapshot = await GetSnapshotRowAsync(id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || !string.Equals(snapshot.AccountId, accountId, StringComparison.Ordinal))
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Snapshot '" + id + "' was not found."));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            List<SnapshotEntryEntity> entries = await db.SnapshotEntries.Where(e => e.SnapshotId == id).ToListAsync(ct).ConfigureAwait(false);
            foreach (SnapshotEntryEntity entry in entries)
            {
                db.SnapshotEntries.Remove(entry);
            }

            SnapshotEntity? row = await db.Snapshots.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (row is not null)
            {
                db.Snapshots.Remove(row);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "snapshots.delete", null, "Snapshot deleted.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<PagedResult<ActivityDto>>> ListActivityAsync(PagedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        int limit = query.SafeLimit;
        int offset = ParseCursor(query.Cursor);
        var listed = await _misc.ListJournalAsync(accountId, offset + limit, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            return Result.Fail<PagedResult<ActivityDto>>(BackendResults.ToBridge(listed.Failure!));
        }

        var items = new List<ActivityDto>();
        foreach (OperationJournalEntity entry in listed.Value.Skip(offset).Take(limit))
        {
            items.Add(new ActivityDto(entry.Id, entry.Kind, entry.NodeId, entry.Summary, entry.Reversible, entry.Undone, entry.OccurredAtUtc.ToString("o")));
        }

        string? next = offset + items.Count < listed.Value.Count ? (offset + items.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Result.Ok(new PagedResult<ActivityDto>(items, next));
    }

    public async Task<Result<EmptyResult>> UndoAsync(UndoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var listed = await _misc.ListJournalAsync(accountId, 10000, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(listed.Failure!));
        }

        OperationJournalEntity? entry = listed.Value.FirstOrDefault(e => e.Id == request.JournalId);
        if (entry is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Journal entry was not found."));
        }

        if (!entry.Reversible || entry.Undone)
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("This operation cannot be undone."));
        }

        Result<EmptyResult> undone = await UndoEntryAsync(accountId, entry, cancellationToken).ConfigureAwait(false);
        if (!undone.IsSuccess)
        {
            return undone;
        }

        var marked = await _misc.MarkUndoneAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        if (!marked.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(marked.Failure!));
        }

        await JournalAsync(accountId, entry.Kind + ".undo", entry.NodeId, "Undid: " + entry.Summary, false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    private async Task<Result<EmptyResult>> UndoEntryAsync(string accountId, OperationJournalEntity entry, CancellationToken cancellationToken)
    {
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        try
        {
            using JsonDocument doc = string.IsNullOrEmpty(entry.AfterJson) && string.IsNullOrEmpty(entry.BeforeJson)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(entry.AfterJson ?? entry.BeforeJson ?? "{}");
            JsonElement payload = doc.RootElement;
            switch (entry.Kind)
            {
                case "nodes.rename":
                case "repos.rename":
                    {
                        string oldName = GetPayloadString(payload, "oldName");
                        string? nodeId = entry.Kind == "repos.rename" ? await RepoFolderIdAsync(accountId, entry.NodeId, cancellationToken).ConfigureAwait(false) : entry.NodeId;
                        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(nodeId))
                        {
                            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                        }

                        var renamed = await resolved.Value.Provider.RenameAsync(nodeId, oldName, cancellationToken).ConfigureAwait(false);
                        if (!renamed.IsSuccess || renamed.Value is null)
                        {
                            return Result.Fail<EmptyResult>(BackendResults.ToBridge(renamed.Failure!));
                        }

                        await MirrorNodeAsync(accountId, renamed.Value, cancellationToken).ConfigureAwait(false);
                        return Result.Ok(new EmptyResult());
                    }

                case "nodes.move":
                case "organize.apply":
                    {
                        if (entry.Kind == "organize.apply")
                        {
                            if (!payload.TryGetProperty("moves", out JsonElement moves) || moves.ValueKind != JsonValueKind.Array)
                            {
                                return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                            }

                            foreach (JsonElement move in moves.EnumerateArray())
                            {
                                string mid = GetPayloadString(move, "nodeId");
                                string from = GetPayloadString(move, "fromParent");
                                if (string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(from))
                                {
                                    continue;
                                }

                                var back = await resolved.Value.Provider.MoveAsync(mid, from, cancellationToken).ConfigureAwait(false);
                                if (back.IsSuccess && back.Value is not null)
                                {
                                    await MirrorNodeAsync(accountId, back.Value, cancellationToken).ConfigureAwait(false);
                                }
                            }

                            return Result.Ok(new EmptyResult());
                        }

                        string oldParent = GetPayloadString(payload, "oldParent");
                        if (string.IsNullOrEmpty(oldParent) || string.IsNullOrEmpty(entry.NodeId))
                        {
                            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                        }

                        var moved = await resolved.Value.Provider.MoveAsync(entry.NodeId, oldParent, cancellationToken).ConfigureAwait(false);
                        if (!moved.IsSuccess || moved.Value is null)
                        {
                            return Result.Fail<EmptyResult>(BackendResults.ToBridge(moved.Failure!));
                        }

                        await MirrorNodeAsync(accountId, moved.Value, cancellationToken).ConfigureAwait(false);
                        return Result.Ok(new EmptyResult());
                    }

                case "nodes.trash":
                case "repos.delete":
                    {
                        string? nodeId = entry.Kind == "repos.delete" ? await RepoFolderIdAsync(accountId, entry.NodeId, cancellationToken).ConfigureAwait(false) : entry.NodeId;
                        if (string.IsNullOrEmpty(nodeId))
                        {
                            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                        }

                        var restored = await resolved.Value.Provider.RestoreAsync(nodeId, cancellationToken).ConfigureAwait(false);
                        if (!restored.IsSuccess || restored.Value is null)
                        {
                            return Result.Fail<EmptyResult>(BackendResults.ToBridge(restored.Failure!));
                        }

                        await MirrorNodeAsync(accountId, restored.Value, cancellationToken).ConfigureAwait(false);
                        return Result.Ok(new EmptyResult());
                    }

                case "nodes.restore":
                    {
                        if (string.IsNullOrEmpty(entry.NodeId))
                        {
                            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                        }

                        var trashed = await resolved.Value.Provider.TrashAsync(entry.NodeId, cancellationToken).ConfigureAwait(false);
                        if (!trashed.IsSuccess || trashed.Value is null)
                        {
                            return Result.Fail<EmptyResult>(BackendResults.ToBridge(trashed.Failure!));
                        }

                        await MirrorNodeAsync(accountId, trashed.Value, cancellationToken).ConfigureAwait(false);
                        return Result.Ok(new EmptyResult());
                    }

                case "nodes.createFolder":
                case "nodes.createFile":
                case "repos.create":
                    {
                        string? nodeId = entry.Kind == "repos.create" ? await RepoFolderIdAsync(accountId, entry.NodeId, cancellationToken).ConfigureAwait(false) : entry.NodeId;
                        if (string.IsNullOrEmpty(nodeId))
                        {
                            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                        }

                        var trashed = await resolved.Value.Provider.TrashAsync(nodeId, cancellationToken).ConfigureAwait(false);
                        if (!trashed.IsSuccess || trashed.Value is null)
                        {
                            return Result.Fail<EmptyResult>(BackendResults.ToBridge(trashed.Failure!));
                        }

                        await MirrorNodeAsync(accountId, trashed.Value, cancellationToken).ConfigureAwait(false);
                        return Result.Ok(new EmptyResult());
                    }

                case "tags.assign":
                    {
                        string tagId = GetPayloadString(payload, "tagId");
                        string nodeId = GetPayloadString(payload, "nodeId");
                        if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(nodeId))
                        {
                            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                        }

                        return await UnassignTagAsync(new AssignTagRequest(tagId, nodeId, null), cancellationToken).ConfigureAwait(false);
                    }

                case "tags.unassign":
                    {
                        string tagId = GetPayloadString(payload, "tagId");
                        string nodeId = GetPayloadString(payload, "nodeId");
                        if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(nodeId))
                        {
                            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                        }

                        return await AssignTagToIdAsync(tagId, nodeId, cancellationToken).ConfigureAwait(false);
                    }

                case "insight.setSummary":
                    {
                        string oldSummary = GetPayloadString(payload, "oldSummary");
                        if (string.IsNullOrEmpty(entry.NodeId))
                        {
                            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is missing."));
                        }

                        var back = await SetSummaryAsync(entry.NodeId, new SetSummaryRequest(oldSummary, null), cancellationToken).ConfigureAwait(false);
                        return back.IsSuccess ? Result.Ok(new EmptyResult()) : Result.Fail<EmptyResult>(back.Failure!);
                    }

                default:
                    return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Operation '" + entry.Kind + "' is not reversible."));
            }
        }
        catch (JsonException)
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Undo payload is corrupt."));
        }
    }

    private async Task<string?> RepoFolderIdAsync(string accountId, string? repoId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(repoId))
        {
            return null;
        }

        RepositoryEntity? repo = await FindRepoAsync(accountId, repoId, cancellationToken).ConfigureAwait(false);
        return repo?.ProviderFolderId;
    }

    private static string GetPayloadString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private async Task<string> SnapshotDefaultModeAsync(CancellationToken cancellationToken)
    {
        string mode = await _settings.GetAsync(BackendSettingsService.Keys.SnapshotDefaultMode, cancellationToken).ConfigureAwait(false);
        return string.Equals(mode, "copy", StringComparison.OrdinalIgnoreCase) ? "copy" : "pin";
    }

    private static async Task<string?> CopyForSnapshotAsync(IStorageProvider provider, string repoFolderId, string snapshotId, string path, string nodeId, CancellationToken cancellationToken)
    {
        string? dataRoot = await EnsureRemotePathAsync(provider, repoFolderId, ".inkguide/snapshots/" + snapshotId + "/data", cancellationToken).ConfigureAwait(false);
        if (dataRoot is null)
        {
            return null;
        }

        string? dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : null;
        string parentId = dataRoot;
        if (dir is not null)
        {
            string? created = await EnsureRemotePathAsync(provider, dataRoot, dir, cancellationToken).ConfigureAwait(false);
            if (created is null)
            {
                return null;
            }

            parentId = created;
        }

        var copied = await provider.CopyAsync(nodeId, parentId, null, cancellationToken).ConfigureAwait(false);
        return copied.IsSuccess && copied.Value is not null ? copied.Value.Id : null;
    }

    private async Task<SnapshotEntity?> GetSnapshotRowAsync(string id, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Snapshots.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
    }

    private static SnapshotDto ToSnapshotDto(SnapshotEntity row)
    {
        return new SnapshotDto(row.Id, row.RepositoryId, row.ProjectId, row.Message, row.Mode, row.CreatedAtUtc.ToString("o"));
    }

    private async Task<Dictionary<string, SnapshotEntryView>?> SnapshotEntriesAsync(string accountId, string id, CancellationToken cancellationToken)
    {
        if (string.Equals(id, "current", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        SnapshotEntity? snapshot = await GetSnapshotRowAsync(id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || !string.Equals(snapshot.AccountId, accountId, StringComparison.Ordinal))
        {
            return null;
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<SnapshotEntryEntity> rows = await db.SnapshotEntries.AsNoTracking().Where(e => e.SnapshotId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var map = new Dictionary<string, SnapshotEntryView>(StringComparer.Ordinal);
        foreach (SnapshotEntryEntity row in rows)
        {
            map[row.Path] = new SnapshotEntryView(row.NodeId, row.Size, row.Md5, row.HeadRevisionId, row.CopyNodeId);
        }

        return map;
    }

    private async Task<Result<string>> SnapshotFileTextAsync(string accountId, string snapshotId, string path, CancellationToken cancellationToken)
    {
        if (string.Equals(snapshotId, "current", StringComparison.OrdinalIgnoreCase))
        {
            Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return Result.Fail<string>(resolved.Failure!);
            }

            RepositoryEntity? repo = await FindRepoOfPathAsync(accountId, path, cancellationToken).ConfigureAwait(false);
            if (repo is null)
            {
                return Result.Fail<string>(BackendResults.NotFound("File was not found."));
            }

            string? nodeId = await FindNodeByRepoPathAsync(accountId, repo.ProviderFolderId, path, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
            if (nodeId is null)
            {
                return Result.Fail<string>(BackendResults.NotFound("File was not found."));
            }

            await using var buffer = new MemoryStream();
            var downloaded = await resolved.Value.Provider.DownloadAsync(nodeId, buffer, null, cancellationToken).ConfigureAwait(false);
            if (!downloaded.IsSuccess)
            {
                return Result.Fail<string>(BackendResults.ToBridge(downloaded.Failure!));
            }

            return Result.Ok(TextCodec.Decode(buffer.ToArray()).Text);
        }

        SnapshotEntity? snapshot = await GetSnapshotRowAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || !string.Equals(snapshot.AccountId, accountId, StringComparison.Ordinal))
        {
            return Result.Fail<string>(BackendResults.NotFound("Snapshot was not found."));
        }

        Result<ResolvedProvider> provider = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!provider.IsSuccess || provider.Value is null)
        {
            return Result.Fail<string>(provider.Failure!);
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        SnapshotEntryEntity? entry = await db.SnapshotEntries.AsNoTracking().FirstOrDefaultAsync(e => e.SnapshotId == snapshotId && e.Path == path, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Result.Fail<string>(BackendResults.NotFound("File is not in the snapshot."));
        }

        var bytes = await SnapshotEntryBytesAsync(provider.Value.Provider, entry, cancellationToken).ConfigureAwait(false);
        if (!bytes.IsSuccess || bytes.Value is null)
        {
            return Result.Fail<string>(bytes.Failure!);
        }

        return Result.Ok(TextCodec.Decode(bytes.Value).Text);
    }

    private async Task<RepositoryEntity?> FindRepoOfPathAsync(string accountId, string path, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> repos = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (RepositoryEntity repo in repos)
        {
            Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (resolved.IsSuccess && resolved.Value is not null)
            {
                string? found = await FindNodeByRepoPathAsync(accountId, repo.ProviderFolderId, path, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
                if (found is not null)
                {
                    return repo;
                }
            }
        }

        return null;
    }

    private static async Task<Result<byte[]?>> SnapshotEntryBytesAsync(IStorageProvider provider, SnapshotEntryEntity entry, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(entry.CopyNodeId))
        {
            await using var copy = new MemoryStream();
            var downloaded = await provider.DownloadAsync(entry.CopyNodeId, copy, null, cancellationToken).ConfigureAwait(false);
            if (downloaded.IsSuccess)
            {
                return Result.Ok<byte[]?>(copy.ToArray());
            }
        }

        if (!string.IsNullOrEmpty(entry.HeadRevisionId))
        {
            await using var revision = new MemoryStream();
            var content = await provider.GetRevisionContentAsync(entry.NodeId, entry.HeadRevisionId, revision, cancellationToken).ConfigureAwait(false);
            if (content.IsSuccess)
            {
                return Result.Ok<byte[]?>(revision.ToArray());
            }
        }

        return Result.Fail<byte[]?>(BackendResults.NotFound("Snapshot content for '" + entry.Path + "' is unavailable (purged without a copy)."));
    }

    private sealed record SnapshotEntryView(string NodeId, long Size, string? Md5, string? RevisionId, string? CopyNodeId);
}
