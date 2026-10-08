using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using InkGuide.Infrastructure.Platform.ExternalEdit;
using InkGuide.Infrastructure.Storage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using InkGuide.App.Hosting;
namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    private const int MaxInlineEditBytes = 5 * 1024 * 1024;
    private const int PreviewTextChars = 20000;
    private const int PreviewTableRows = 200;

    public async Task<Result<PagedResult<NodeDto>>> ListNodesAsync(ListNodesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        string? parentId = request.ParentId;
        if (string.IsNullOrEmpty(parentId))
        {
            Result<WorkspaceDto> workspace = await EnsureWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            if (!workspace.IsSuccess || workspace.Value is null)
            {
                return Result.Fail<PagedResult<NodeDto>>(workspace.Failure!);
            }

            parentId = workspace.Value.ProviderFolderId;
        }

        int limit = request.Limit <= 0 ? 200 : Math.Min(request.Limit, 1000);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<NodeEntity> rows = await db.Nodes.AsNoTracking()
            .Where(n => n.AccountId == accountId && n.ParentId == parentId && (request.IncludeTrashed || !n.Trashed) && n.Name != ".inkguide")
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<NodeEntity> query = rows;
        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            query = query.Where(n => n.Name.Contains(request.Filter, StringComparison.OrdinalIgnoreCase));
        }

        query = (request.Sort ?? "name").ToLowerInvariant() switch
        {
            "modified" => query.OrderByDescending(n => n.ModifiedTimeUtc),
            "size" => query.OrderByDescending(n => n.Size),
            _ => query.OrderBy(n => n.IsFolder ? 0 : 1).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase),
        };

        List<NodeEntity> ordered = query.ToList();
        int offset = ParseCursor(request.Cursor);
        List<NodeDto> items = new();
        foreach (NodeEntity row in ordered.Skip(offset).Take(limit))
        {
            items.Add(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
        }

        string? next = offset + items.Count < ordered.Count ? (offset + items.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Result.Ok(new PagedResult<NodeDto>(items, next));
    }

    public async Task<Result<NodeDto>> GetNodeAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? row = await FindNodeAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<NodeDto>(BackendResults.NotFound("Node '" + id + "' was not found."));
        }

        return Result.Ok(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<NodeDto>>> GetNodePathAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var trail = new List<NodeDto>();
        string? current = id;
        for (int i = 0; i < 64 && current is not null; i++)
        {
            NodeEntity? row = await FindNodeAsync(accountId, current, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                break;
            }

            trail.Add(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
            current = row.ParentId;
        }

        trail.Reverse();
        return Result.Ok<IReadOnlyList<NodeDto>>(trail);
    }

    public async Task<Result<NodeDto>> CreateFolderAsync(string parentId, string name, string? idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentId);
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail<NodeDto>(BackendResults.InvalidParams("Folder name is empty."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<NodeDto>(resolved.Failure!);
        }

        var created = await resolved.Value.Provider.CreateFolderAsync(parentId, name.Trim(), cancellationToken).ConfigureAwait(false);
        if (!created.IsSuccess || created.Value is null)
        {
            if (string.Equals(created.Failure?.Code, "OFFLINE", StringComparison.Ordinal))
            {
                return Result.Fail<NodeDto>(await EnqueueOfflineAsync(accountId, "createFolder", new { parentId, name }, cancellationToken).ConfigureAwait(false));
            }

            return Result.Fail<NodeDto>(BackendResults.ToBridge(created.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, created.Value, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.createFolder", row.Id, "Folder '" + row.Name + "' created.", true, null, JsonOf(new { nodeId = row.Id }), cancellationToken).ConfigureAwait(false);
        return Result.Ok(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<NodeDto>> CreateFileAsync(CreateFileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Fail<NodeDto>(BackendResults.InvalidParams("File name is empty."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<NodeDto>(resolved.Failure!);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(request.Text ?? string.Empty);
        await using var stream = new MemoryStream(bytes, writable: false);
        var uploaded = await resolved.Value.Provider.UploadAsync(request.ParentId, request.Name.Trim(), stream, request.MimeType ?? "text/plain", request.IdempotencyKey, null, cancellationToken).ConfigureAwait(false);
        if (!uploaded.IsSuccess || uploaded.Value is null)
        {
            if (string.Equals(uploaded.Failure?.Code, "OFFLINE", StringComparison.Ordinal))
            {
                return Result.Fail<NodeDto>(await EnqueueOfflineAsync(accountId, "upload", new { request.ParentId, request.Name }, cancellationToken).ConfigureAwait(false));
            }

            return Result.Fail<NodeDto>(BackendResults.ToBridge(uploaded.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, uploaded.Value, cancellationToken).ConfigureAwait(false);
        await TriggerAnalysisAsync(accountId, resolved.Value, row.Id, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.createFile", row.Id, "File '" + row.Name + "' created.", true, null, JsonOf(new { nodeId = row.Id }), cancellationToken).ConfigureAwait(false);
        return Result.Ok(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<NodeDto>> RenameNodeAsync(string id, RenameNodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<NodeDto>(resolved.Failure!);
        }

        NodeEntity? before = await FindNodeAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        var renamed = await resolved.Value.Provider.RenameAsync(id, request.NewName, cancellationToken).ConfigureAwait(false);
        if (!renamed.IsSuccess || renamed.Value is null)
        {
            return Result.Fail<NodeDto>(BackendResults.ToBridge(renamed.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, renamed.Value, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.rename", id, "Renamed to '" + row.Name + "'.", true,
            before is null ? null : JsonOf(new { oldName = before.Name }), null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<NodeDto>> MoveNodeAsync(string id, MoveNodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<NodeDto>(resolved.Failure!);
        }

        NodeEntity? beforeMove = await FindNodeAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        var moved = await resolved.Value.Provider.MoveAsync(id, request.NewParentId, cancellationToken).ConfigureAwait(false);
        if (!moved.IsSuccess || moved.Value is null)
        {
            return Result.Fail<NodeDto>(BackendResults.ToBridge(moved.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, moved.Value, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.move", id, "Moved '" + row.Name + "'.", true,
            beforeMove is null || beforeMove.ParentId is null ? null : JsonOf(new { oldParent = beforeMove.ParentId }), null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<NodeDto>> CopyNodeAsync(string id, CopyNodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<NodeDto>(resolved.Failure!);
        }

        var copied = await resolved.Value.Provider.CopyAsync(id, request.NewParentId, string.IsNullOrEmpty(request.NewName) ? null : request.NewName, cancellationToken).ConfigureAwait(false);
        if (!copied.IsSuccess || copied.Value is null)
        {
            return Result.Fail<NodeDto>(BackendResults.ToBridge(copied.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, copied.Value, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.copy", row.Id, "Copied '" + row.Name + "'.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<NodeDto>> TrashNodeAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<NodeDto>(resolved.Failure!);
        }

        var trashed = await resolved.Value.Provider.TrashAsync(id, cancellationToken).ConfigureAwait(false);
        if (!trashed.IsSuccess || trashed.Value is null)
        {
            return Result.Fail<NodeDto>(BackendResults.ToBridge(trashed.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, trashed.Value, cancellationToken).ConfigureAwait(false);
        await MarkSubtreeTrashedAsync(accountId, row.Id, true, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.trash", id, "Trashed '" + row.Name + "'.", true, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<NodeDto>> RestoreNodeAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<NodeDto>(resolved.Failure!);
        }

        var restored = await resolved.Value.Provider.RestoreAsync(id, cancellationToken).ConfigureAwait(false);
        if (!restored.IsSuccess || restored.Value is null)
        {
            return Result.Fail<NodeDto>(BackendResults.ToBridge(restored.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, restored.Value, cancellationToken).ConfigureAwait(false);
        await MarkSubtreeTrashedAsync(accountId, row.Id, false, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.restore", id, "Restored '" + row.Name + "'.", true, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<PrepareDeleteInfo>> PrepareDeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? row = await FindNodeAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<PrepareDeleteInfo>(BackendResults.NotFound("Node '" + id + "' was not found."));
        }

        int affected = 1 + await CountDescendantsAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new PrepareDeleteInfo(
            "Permanently delete '" + row.Name + "' and " + (affected - 1) + " contained items? This cannot be undone.",
            new[] { row.PathText }));
    }

    public async Task<Result<EmptyResult>> DeletePermanentAsync(string id, DeletePermanentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? row = await FindNodeAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Node '" + id + "' was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        // Collect the subtree first: permanent deletion removes every row.
        var doomed = new List<string> { id };
        await CollectDescendantsAsync(accountId, id, doomed, cancellationToken).ConfigureAwait(false);

        var deleted = await resolved.Value.Provider.DeletePermanentAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(deleted.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            foreach (string nodeId in doomed)
            {
                NodeEntity? target = await db.Nodes.FindAsync(new object[] { nodeId }, ct).ConfigureAwait(false);
                if (target is not null)
                {
                    db.Nodes.Remove(target);
                }
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "nodes.deletePermanent", id, "Permanently deleted '" + row.Name + "'.", false, null, null, cancellationToken).ConfigureAwait(false);
        await _search.RemoveNodeAsync(id, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<ReadTextResponse>> ReadTextAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<ReadTextResponse>(resolved.Failure!);
        }

        var remote = await resolved.Value.Provider.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            return Result.Fail<ReadTextResponse>(BackendResults.ToBridge(remote.Failure!));
        }

        if (remote.Value.IsFolder)
        {
            return Result.Fail<ReadTextResponse>(BackendResults.InvalidParams("Folders have no text content."));
        }

        if (remote.Value.Size > MaxInlineEditBytes)
        {
            return Result.Fail<ReadTextResponse>(new OperationError("TOO_LARGE_TO_EDIT", "File exceeds the 5 MB inline-edit limit.", false, null));
        }

        await using var buffer = new MemoryStream();
        var downloaded = await resolved.Value.Provider.DownloadAsync(id, buffer, null, cancellationToken).ConfigureAwait(false);
        if (!downloaded.IsSuccess)
        {
            return Result.Fail<ReadTextResponse>(BackendResults.ToBridge(downloaded.Failure!));
        }

        byte[] bytes = buffer.ToArray();
        if (bytes.Contains((byte)0))
        {
            return Result.Fail<ReadTextResponse>(BackendResults.InvalidParams("File is binary and cannot be edited as text."));
        }

        TextCodec.DecodedText decoded = TextCodec.Decode(bytes);
        return Result.Ok(new ReadTextResponse(decoded.Text, decoded.Encoding, decoded.Eol, decoded.HasBom, remote.Value.Version ?? "1"));
    }

    public async Task<Result<ReadTextResponse>> WriteTextAsync(string id, WriteTextRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<ReadTextResponse>(resolved.Failure!);
        }

        var remote = await resolved.Value.Provider.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            return Result.Fail<ReadTextResponse>(BackendResults.ToBridge(remote.Failure!));
        }

        string currentVersion = remote.Value.Version ?? "1";
        if (!string.Equals(currentVersion, request.BaseVersion, StringComparison.Ordinal))
        {
            await RecordContentConflictAsync(accountId, resolved.Value, remote.Value, request.Text, cancellationToken).ConfigureAwait(false);
            return Result.Fail<ReadTextResponse>(new OperationError("CONFLICT_VERSION", "Remote version moved; local copy kept as a conflict file.", true, null));
        }

        // Preserve the remote encoding/BOM/EOL by re-reading the current bytes.
        await using var current = new MemoryStream();
        var downloaded = await resolved.Value.Provider.DownloadAsync(id, current, null, cancellationToken).ConfigureAwait(false);
        if (!downloaded.IsSuccess)
        {
            return Result.Fail<ReadTextResponse>(BackendResults.ToBridge(downloaded.Failure!));
        }

        byte[] encoded = TextCodec.Encode(request.Text ?? string.Empty, current.ToArray());
        await using var stream = new MemoryStream(encoded, writable: false);
        var updated = await resolved.Value.Provider.UpdateContentAsync(id, stream, request.BaseVersion, null, null, cancellationToken).ConfigureAwait(false);
        if (!updated.IsSuccess || updated.Value is null)
        {
            if (string.Equals(updated.Failure?.Code, "CONFLICT_VERSION", StringComparison.Ordinal))
            {
                await RecordContentConflictAsync(accountId, resolved.Value, remote.Value, request.Text ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }

            return Result.Fail<ReadTextResponse>(BackendResults.ToBridge(updated.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, updated.Value, cancellationToken).ConfigureAwait(false);
        await TriggerAnalysisAsync(accountId, resolved.Value, row.Id, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.writeText", id, "Edited '" + row.Name + "'.", false, null, null, cancellationToken).ConfigureAwait(false);
        return await ReadTextAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PreviewDto>> GetPreviewAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<PreviewDto>(resolved.Failure!);
        }

        var remote = await resolved.Value.Provider.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            return Result.Fail<PreviewDto>(BackendResults.ToBridge(remote.Failure!));
        }

        StorageItem item = remote.Value;
        string kind = await InsightKindAsync(accountId, item, cancellationToken).ConfigureAwait(false);

        if (item.IsFolder)
        {
            int children = await ChildCountAsync(accountId, item.Id, cancellationToken).ConfigureAwait(false);
            return Result.Ok(new PreviewDto("folder", null, null, null, "{\"children\":" + children + "}"));
        }

        if (kind == "image-raster" || kind == "image-vector" || kind == "audio" || kind == "video" || kind == "pdf")
        {
            string? url = await CacheRemoteAsync(accountId, resolved.Value.Provider, item, cancellationToken).ConfigureAwait(false);
            return Result.Ok(new PreviewDto(kind, url, null, null, "{\"size\":" + item.Size + "}"));
        }

        if (kind.StartsWith("google-", StringComparison.Ordinal))
        {
            string link = item.WebViewLink ?? string.Empty;
            return Result.Ok(new PreviewDto(kind, null, null, null, "{\"webViewLink\":" + JsonOf(link) + ",\"note\":\"Google-native files open in the system browser.\"}"));
        }

        if (item.Size > MaxInlineEditBytes)
        {
            return Result.Ok(new PreviewDto(kind, null, null, null, "{\"size\":" + item.Size + ",\"note\":\"Too large to preview inline.\"}"));
        }

        await using var buffer = new MemoryStream();
        var downloaded = await resolved.Value.Provider.DownloadAsync(id, buffer, null, cancellationToken).ConfigureAwait(false);
        if (!downloaded.IsSuccess)
        {
            return Result.Fail<PreviewDto>(BackendResults.ToBridge(downloaded.Failure!));
        }

        byte[] bytes = buffer.ToArray();
        if (kind == "data-tabular" || item.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || item.Name.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Ok(new PreviewDto(kind, null, null, ParseTable(bytes), null));
        }

        if (kind == "data-structured" || kind == "config" || item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string text = Encoding.UTF8.GetString(bytes);
            bool valid = IsValidJson(text);
            string excerpt = text.Length > PreviewTextChars ? text[..PreviewTextChars] : text;
            return Result.Ok(new PreviewDto(kind, null, null, null, "{\"valid\":" + (valid ? "true" : "false") + ",\"text\":" + JsonOf(excerpt) + "}"));
        }

        if (kind == "archive")
        {
            return Result.Ok(new PreviewDto(kind, null, null, null, "{\"entries\":" + JsonOf(ListZipEntries(bytes)) + "}"));
        }

        if (bytes.Contains((byte)0))
        {
            return Result.Ok(new PreviewDto(kind, null, null, null, "{\"size\":" + item.Size + ",\"binary\":true}"));
        }

        string decoded = TextCodec.Decode(bytes).Text;
        if (decoded.Length > PreviewTextChars)
        {
            decoded = decoded[..PreviewTextChars];
        }

        if (kind == "web-page" && item.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            // Sandboxed by the UI (separate virtual host + sandboxed iframe).
            string? url = await CacheRemoteAsync(accountId, resolved.Value.Provider, item, cancellationToken).ConfigureAwait(false);
            return Result.Ok(new PreviewDto("html", url, null, null, null));
        }

        return Result.Ok(new PreviewDto(kind, null, null, null, "{\"text\":" + JsonOf(decoded) + "}"));
    }

    public async Task<Result<EmptyResult>> OpenExternalAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        var remote = await resolved.Value.Provider.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(remote.Failure!));
        }

        if (remote.Value.IsFolder)
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Folders cannot be opened externally."));
        }

        if (ExternalEditService.RequiresConfirmation(remote.Value.Name))
        {
            // The UI contract carries no confirmation token for this call, so risky
            // extensions stay blocked rather than opening on an assumed consent.
            return Result.Fail<EmptyResult>(new OperationError("PERMISSION_DENIED", "Opening '" + remote.Value.Name + "' needs an explicit confirmation, which the current UI contract does not carry.", false, null));
        }

        await using var buffer = new MemoryStream();
        var downloaded = await resolved.Value.Provider.DownloadAsync(id, buffer, null, cancellationToken).ConfigureAwait(false);
        if (!downloaded.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(downloaded.Failure!));
        }

        buffer.Position = 0;
        var session = await _external.StartSessionAsync(accountId, id, remote.Value.Name, remote.Value.Version ?? "1", buffer, cancellationToken).ConfigureAwait(false);
        if (!session.IsSuccess || session.Value is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(session.Failure!));
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = session.Value.LocalPath,
                UseShellExecute = true,
            };
            _ = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            return Result.Fail<EmptyResult>(BackendResults.Internal("Could not open the default application."));
        }

        await _host.EmitEventAsync("external.sessionChanged", session.Value.SessionId).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> OpenInBrowserAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        var remote = await resolved.Value.Provider.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(remote.Failure!));
        }

        if (string.IsNullOrEmpty(remote.Value.WebViewLink))
        {
            return Result.Fail<EmptyResult>(new OperationError("UNSUPPORTED_OPERATION", "This provider exposes no browser link for the item.", false, null));
        }

        _host.OpenExternalUrl(remote.Value.WebViewLink);
        _ = cancellationToken;
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<IReadOnlyList<RevisionDto>>> ListRevisionsAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<IReadOnlyList<RevisionDto>>(resolved.Failure!);
        }

        var revisions = await resolved.Value.Provider.ListRevisionsAsync(id, cancellationToken).ConfigureAwait(false);
        if (!revisions.IsSuccess || revisions.Value is null)
        {
            return Result.Fail<IReadOnlyList<RevisionDto>>(BackendResults.ToBridge(revisions.Failure!));
        }

        var list = new List<RevisionDto>();
        foreach (var revision in revisions.Value)
        {
            list.Add(new RevisionDto(revision.Id, revision.ModifiedTimeUtc.ToString("o"), revision.Size, revision.KeepForever));
        }

        return Result.Ok<IReadOnlyList<RevisionDto>>(list);
    }

    public async Task<Result<EmptyResult>> RestoreRevisionAsync(string id, RestoreRevisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        var remote = await resolved.Value.Provider.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(remote.Failure!));
        }

        await using var buffer = new MemoryStream();
        var content = await resolved.Value.Provider.GetRevisionContentAsync(id, request.RevisionId, buffer, cancellationToken).ConfigureAwait(false);
        if (!content.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(content.Failure!));
        }

        buffer.Position = 0;
        var restored = await resolved.Value.Provider.UpdateContentAsync(id, buffer, remote.Value.Version, null, null, cancellationToken).ConfigureAwait(false);
        if (!restored.IsSuccess || restored.Value is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(restored.Failure!));
        }

        NodeEntity row = await MirrorNodeAsync(accountId, restored.Value, cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "nodes.restoreRevision", id, "Restored revision of '" + row.Name + "' as a new version.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> SetFavoriteAsync(string id, SetFavoriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            FavoriteEntity? existing = await db.Favorites.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (request.Favorite && existing is null)
            {
                db.Favorites.Add(new FavoriteEntity { NodeId = id, AccountId = accountId, PinnedAtUtc = _clock.GetUtcNow() });
            }
            else if (!request.Favorite && existing is not null)
            {
                db.Favorites.Remove(existing);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<IReadOnlyList<NodeDto>>> ListFavoritesAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<FavoriteEntity> favorites = await db.Favorites.AsNoTracking().Where(f => f.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<NodeDto>();
        foreach (FavoriteEntity favorite in favorites)
        {
            NodeEntity? row = await FindNodeAsync(accountId, favorite.NodeId, cancellationToken).ConfigureAwait(false);
            if (row is not null && !row.Trashed)
            {
                list.Add(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
            }
        }

        return Result.Ok<IReadOnlyList<NodeDto>>(list);
    }

    public async Task<Result<PagedResult<NodeDto>>> ListTrashAsync(PagedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot ORDER BY DateTimeOffset: sort the trashed set in memory.
        List<NodeEntity> rows = (await db.Nodes.AsNoTracking().Where(n => n.AccountId == accountId && n.Trashed).ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(n => n.ModifiedTimeUtc)
            .ToList();
        int limit = query.SafeLimit;
        int offset = ParseCursor(query.Cursor);
        var items = new List<NodeDto>();
        foreach (NodeEntity row in rows.Skip(offset).Take(limit))
        {
            items.Add(await ToDtoAsync(row, cancellationToken).ConfigureAwait(false));
        }

        string? next = offset + items.Count < rows.Count ? (offset + items.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Result.Ok(new PagedResult<NodeDto>(items, next));
    }

    public async Task<Result<EmptyResult>> EmptyTrashAsync(string? confirmationToken, CancellationToken cancellationToken)
    {
        _ = confirmationToken;
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<NodeEntity> rows = await db.Nodes.Where(n => n.AccountId == accountId && n.Trashed).ToListAsync(cancellationToken).ConfigureAwait(false);
        var failed = new List<string>();
        foreach (NodeEntity row in rows)
        {
            var deleted = await resolved.Value.Provider.DeletePermanentAsync(row.Id, cancellationToken).ConfigureAwait(false);
            if (deleted.IsSuccess)
            {
                db.Nodes.Remove(row);
            }
            else
            {
                failed.Add(row.Name);
            }
        }

        _ = await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (failed.Count > 0)
        {
            return Result.Fail<EmptyResult>(BackendResults.Internal("Could not permanently delete: " + string.Join(", ", failed) + "."));
        }

        await JournalAsync(accountId, "nodes.emptyTrash", null, "Trash emptied.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    private async Task<NodeEntity?> FindNodeAsync(string accountId, string nodeId, CancellationToken cancellationToken)
    {
        // Node ids are provider-unique; the account check prevents cross-account leaks.
        var got = await _nodes.GetAsync(nodeId, cancellationToken).ConfigureAwait(false);
        if (!got.IsSuccess || got.Value is null || !string.Equals(got.Value.AccountId, accountId, StringComparison.Ordinal))
        {
            return null;
        }

        return got.Value;
    }

    private async Task<NodeEntity> MirrorNodeAsync(string accountId, StorageItem item, CancellationToken cancellationToken)
    {
        string? repoId = await ResolveRepoForNodeAsync(accountId, item, cancellationToken).ConfigureAwait(false);
        string path = await BuildPathAsync(accountId, item, cancellationToken).ConfigureAwait(false);
        var row = new NodeEntity
        {
            Id = item.Id,
            AccountId = accountId,
            ParentId = item.ParentId,
            Name = item.Name,
            IsFolder = item.IsFolder,
            MimeType = item.MimeType,
            Size = item.Size,
            Md5 = item.Md5,
            Version = item.Version,
            HeadRevisionId = item.HeadRevisionId,
            ModifiedTimeUtc = item.ModifiedTimeUtc,
            Trashed = item.Trashed,
            RepoId = repoId,
            ProjectId = null,
            PathText = path,
            Description = item.Description,
            WebViewLink = item.WebViewLink,
            SyncedAtUtc = _clock.GetUtcNow(),
        };
        var upserted = await _nodes.UpsertAsync(row, cancellationToken).ConfigureAwait(false);
        if (!upserted.IsSuccess)
        {
            AppLogMessages.BackendNote(_logger, "Node mirror failed for " + item.Id + ": " + upserted.Failure?.Message);
        }

        await _sync.IndexForSearchAsync(accountId, row.Id, null, cancellationToken).ConfigureAwait(false);
        return row;
    }

    private async Task<string?> ResolveRepoForNodeAsync(string accountId, StorageItem item, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> repos = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var folderIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (RepositoryEntity repo in repos)
        {
            _ = folderIds.Add(repo.ProviderFolderId);
        }

        if (item.ParentId is not null && folderIds.Contains(item.ParentId))
        {
            foreach (RepositoryEntity repo in repos)
            {
                if (string.Equals(repo.ProviderFolderId, item.ParentId, StringComparison.Ordinal))
                {
                    return repo.Id;
                }
            }
        }

        // Walk up through cached parents to find the enclosing repo.
        string? parent = item.ParentId;
        for (int i = 0; i < 64 && parent is not null; i++)
        {
            foreach (RepositoryEntity repo in repos)
            {
                if (string.Equals(repo.ProviderFolderId, parent, StringComparison.Ordinal))
                {
                    return repo.Id;
                }
            }

            NodeEntity? row = await FindNodeAsync(accountId, parent, cancellationToken).ConfigureAwait(false);
            parent = row?.ParentId;
        }

        return null;
    }

    private async Task<string> BuildPathAsync(string accountId, StorageItem item, CancellationToken cancellationToken)
    {
        var segments = new List<string> { item.Name };
        string? parent = item.ParentId;
        for (int i = 0; i < 64 && parent is not null; i++)
        {
            NodeEntity? row = await FindNodeAsync(accountId, parent, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                break;
            }

            segments.Add(row.Name);
            parent = row.ParentId;
        }

        segments.Reverse();
        return "/" + string.Join('/', segments);
    }

    private async Task MarkSubtreeTrashedAsync(string accountId, string rootId, bool trashed, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var queue = new Queue<string>();
        queue.Enqueue(rootId);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                List<NodeEntity> children = await db.Nodes.Where(n => n.AccountId == accountId && n.ParentId == current).ToListAsync(ct).ConfigureAwait(false);
                foreach (NodeEntity child in children)
                {
                    child.Trashed = trashed;
                    queue.Enqueue(child.Id);
                }
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> CountDescendantsAsync(string accountId, string rootId, CancellationToken cancellationToken)
    {
        var collected = new List<string>();
        await CollectDescendantsAsync(accountId, rootId, collected, cancellationToken).ConfigureAwait(false);
        return collected.Count;
    }

    private async Task CollectDescendantsAsync(string accountId, string rootId, List<string> collected, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var queue = new Queue<string>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            List<NodeEntity> children = await db.Nodes.AsNoTracking().Where(n => n.AccountId == accountId && n.ParentId == current).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (NodeEntity child in children)
            {
                collected.Add(child.Id);
                queue.Enqueue(child.Id);
            }
        }
    }

    private async Task<int> ChildCountAsync(string accountId, string folderId, CancellationToken cancellationToken)
    {
        var counted = await _nodes.CountChildrenAsync(accountId, folderId, cancellationToken).ConfigureAwait(false);
        return counted.IsSuccess ? counted.Value : 0;
    }

    private async Task<string> InsightKindAsync(string accountId, StorageItem item, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeInsightEntity? insight = await db.NodeInsights.FindAsync(new object[] { item.Id }, cancellationToken).ConfigureAwait(false);
        if (insight is not null && !string.IsNullOrEmpty(insight.Kind))
        {
            return insight.Kind;
        }

        if (item.IsFolder)
        {
            return "folder";
        }

        _ = accountId;
        return "unknown";
    }

    private async Task<NodeDto> ToDtoAsync(NodeEntity row, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeInsightEntity? insight = await db.NodeInsights.FindAsync(new object[] { row.Id }, cancellationToken).ConfigureAwait(false);
        return new NodeDto(
            row.Id, row.ParentId, row.Name, row.IsFolder, row.MimeType, row.Size, row.Md5,
            row.Version, row.ModifiedTimeUtc.ToString("o"), row.Trashed, row.PathText,
            insight?.Summary ?? row.Description,
            null, null);
    }

    private async Task<OperationError> EnqueueOfflineAsync(string accountId, string operationType, object payload, CancellationToken cancellationToken)
    {
        Result<string> queued = await EnqueueOutboxAsync(accountId, operationType, payload, cancellationToken).ConfigureAwait(false);
        if (!queued.IsSuccess)
        {
            return queued.Failure!;
        }

        return new OperationError("OFFLINE", "Offline: operation queued as " + queued.Value + ".", false, null);
    }

    private async Task TriggerAnalysisAsync(string accountId, ResolvedProvider resolved, string nodeId, CancellationToken cancellationToken)
    {
        string auto = await _settings.GetAsync(BackendSettingsService.Keys.AnalysisAuto, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(auto, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _sync.AnalyzeOneAsync(accountId, resolved.Provider, nodeId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogMessages.BackendNote(_logger, "Post-mutation analysis failed: " + ex.Message);
        }
    }

    private async Task RecordContentConflictAsync(string accountId, ResolvedProvider resolved, StorageItem remote, string localText, CancellationToken cancellationToken)
    {
        string stamp = _clock.GetUtcNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string copyName = remote.Name + " (conflict " + stamp + ")";
        string conflictId = "conflict-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12];

        await using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(localText), writable: false);
        var copy = await resolved.Provider.UploadAsync(remote.ParentId ?? string.Empty, copyName, buffer, remote.MimeType ?? "text/plain", null, null, cancellationToken).ConfigureAwait(false);
        string? copyPath = copy.IsSuccess && copy.Value is not null ? copy.Value.Name : null;
        if (copy.IsSuccess && copy.Value is not null)
        {
            await MirrorNodeAsync(accountId, copy.Value, cancellationToken).ConfigureAwait(false);
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.ConflictRecords.Add(new ConflictRecordEntity
            {
                Id = conflictId,
                AccountId = accountId,
                NodeId = remote.Id,
                Kind = "Content",
                ConflictCopyNodeId = copy.IsSuccess && copy.Value is not null ? copy.Value.Id : null,
                ConflictCopyPath = copyPath,
                BaseVersion = null,
                RemoteVersion = remote.Version,
                LocalText = localText.Length > 65536 ? localText[..65536] : localText,
                Resolution = null,
                CreatedAtUtc = _clock.GetUtcNow(),
            });
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "conflict.created", remote.Id, "Content conflict on '" + remote.Name + "' kept as '" + copyName + "'.", false, null, null, cancellationToken).ConfigureAwait(false);
        await _host.EmitEventAsync("conflict.created", conflictId).ConfigureAwait(false);
    }

    private static int ParseCursor(string? cursor)
    {
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int offset))
        {
            return Math.Max(0, offset);
        }

        return 0;
    }

    private static string JsonOf(object? value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<IReadOnlyList<string>> ParseTable(byte[] bytes)
    {
        var rows = new List<IReadOnlyList<string>>();
        string text = Encoding.UTF8.GetString(bytes);
        string[] lines = text.Split('\n');
        foreach (string line in lines)
        {
            if (rows.Count >= PreviewTableRows)
            {
                break;
            }

            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                continue;
            }

            char delimiter = trimmed.Contains('\t') ? '\t' : ',';
            rows.Add(trimmed.Split(delimiter).Select(c => c.Trim()).ToList());
        }

        return rows;
    }

    private static List<string> ListZipEntries(byte[] bytes)
    {
        var names = new List<string>();
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                names.Add(entry.FullName);
                if (names.Count >= 200)
                {
                    break;
                }
            }
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return names;
    }

    private async Task<string?> CacheRemoteAsync(string accountId, IStorageProvider provider, StorageItem item, CancellationToken cancellationToken)
    {
        _ = accountId;
        string key = "preview-" + SanitizeKey(item.Id) + "-" + SanitizeKey(item.Version ?? "1");
        await using var buffer = new MemoryStream();
        var downloaded = await provider.DownloadAsync(item.Id, buffer, null, cancellationToken).ConfigureAwait(false);
        if (!downloaded.IsSuccess)
        {
            return null;
        }

        buffer.Position = 0;
        var stored = await _cache.StoreAsync(key, buffer, cancellationToken).ConfigureAwait(false);
        if (!stored.IsSuccess)
        {
            return null;
        }

        return "https://" + AppIdentity.CacheHost + "/" + key + ".bin";
    }

    private static string SanitizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            builder.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' ? c : '_');
        }

        string sanitized = builder.ToString();
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }
}
