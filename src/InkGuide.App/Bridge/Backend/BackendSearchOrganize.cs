using System;
using System.Collections.Generic;
using System.Linq;
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
    public async Task<Result<PagedResult<SearchHitDto>>> SearchAsync(SearchQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        string text = request.Text ?? string.Empty;
        int limit = request.Limit <= 0 ? 200 : Math.Min(request.Limit, 1000);

        // Full-text candidates from the search service, then precise filters in memory.
        var raw = await _search.QueryAsync(accountId, text, Math.Min(limit * 5, 2000), cancellationToken).ConfigureAwait(false);
        if (!raw.IsSuccess || raw.Value is null)
        {
            return Result.Fail<PagedResult<SearchHitDto>>(BackendResults.ToBridge(raw.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hits = new List<SearchHitDto>();
        foreach (var hit in raw.Value)
        {
            NodeEntity? node = await db.Nodes.FindAsync(new object[] { hit.NodeId }, cancellationToken).ConfigureAwait(false);
            if (node is null || !string.Equals(node.AccountId, accountId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!request.IncludeTrashed && node.Trashed)
            {
                continue;
            }

            if (request.RepoId is not null && !string.Equals(node.RepoId, request.RepoId, StringComparison.Ordinal))
            {
                continue;
            }

            if (request.ProjectId is not null && !string.Equals(node.ProjectId, request.ProjectId, StringComparison.Ordinal))
            {
                continue;
            }

            if (request.SizeMin.HasValue && node.Size < request.SizeMin.Value)
            {
                continue;
            }

            if (request.SizeMax.HasValue && node.Size > request.SizeMax.Value)
            {
                continue;
            }

            if (request.ModifiedFrom is not null && DateTimeOffset.TryParse(request.ModifiedFrom, out DateTimeOffset from) && node.ModifiedTimeUtc < from)
            {
                continue;
            }

            if (request.ModifiedTo is not null && DateTimeOffset.TryParse(request.ModifiedTo, out DateTimeOffset to) && node.ModifiedTimeUtc > to)
            {
                continue;
            }

            if (request.Kinds is not null && request.Kinds.Count > 0)
            {
                NodeInsightEntity? insight = await db.NodeInsights.FindAsync(new object[] { node.Id }, cancellationToken).ConfigureAwait(false);
                string kind = insight?.Kind ?? "unknown";
                if (!request.Kinds.Contains(kind))
                {
                    continue;
                }
            }

            if (request.TagIds is not null && request.TagIds.Count > 0)
            {
                List<NodeTagEntity> assigns = await db.NodeTags.AsNoTracking().Where(t => t.NodeId == node.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
                bool any = false;
                foreach (string tagId in request.TagIds)
                {
                    if (assigns.Any(a => string.Equals(a.TagId, tagId, StringComparison.Ordinal)))
                    {
                        any = true;
                        break;
                    }
                }

                if (!any)
                {
                    continue;
                }
            }

            hits.Add(new SearchHitDto(node.Id, node.Name, node.PathText, node.IsFolder, hit.Highlight));
            if (hits.Count >= limit)
            {
                break;
            }
        }

        int offset = ParseCursor(request.Cursor);
        List<SearchHitDto> page = hits.Skip(offset).Take(limit).ToList();
        string? next = offset + page.Count < hits.Count ? (offset + page.Count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return Result.Ok(new PagedResult<SearchHitDto>(page, next));
    }

    public async Task<Result<SuggestResponse>> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        int limit = request.Limit <= 0 ? 10 : Math.Min(request.Limit, 50);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // OrdinalIgnoreCase StartsWith does not translate to SQLite: filter in memory.
        List<string> names = await db.Nodes.AsNoTracking().Where(n => n.AccountId == accountId && !n.Trashed).Select(n => n.Name).Take(5000).ToListAsync(cancellationToken).ConfigureAwait(false);
        string prefix = request.Prefix ?? string.Empty;
        List<string> suggestions = names.Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Take(limit).ToList();
        return Result.Ok(new SuggestResponse(suggestions));
    }

    public async Task<Result<PagedResult<NodeDto>>> ListInboxAsync(PagedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<WorkspaceDto> workspace = await EnsureWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        if (!workspace.IsSuccess || workspace.Value is null)
        {
            return Result.Fail<PagedResult<NodeDto>>(workspace.Failure!);
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<PagedResult<NodeDto>>(resolved.Failure!);
        }

        StorageItem? inbox = await EnsureFolderAsync(resolved.Value.Provider, workspace.Value.ProviderFolderId, InboxFolderName, cancellationToken).ConfigureAwait(false);
        if (inbox is null)
        {
            return Result.Fail<PagedResult<NodeDto>>(BackendResults.Internal("Inbox folder is unavailable."));
        }

        var list = new ListNodesRequest(inbox.Id, query.Cursor, query.SafeLimit, null, null, false);
        return await ListNodesAsync(list, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<OrganizeSuggestionDto>>> SuggestOrganizeAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<IReadOnlyList<OrganizeSuggestionDto>>(resolved.Failure!);
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> repos = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var suggestions = new List<OrganizeSuggestionDto>();
        foreach (string nodeId in nodeIds.Take(100))
        {
            NodeEntity? node = await FindNodeAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
            if (node is null || node.IsFolder)
            {
                continue;
            }

            NodeInsightEntity? insight = await db.NodeInsights.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
            string kind = insight?.Kind ?? "unknown";
            string? role = insight?.Role;

            // Candidate destinations: every repo root + project folder, scored by
            // kind/role affinity, tag overlap and sibling similarity. Reasons included.
            var ranked = new List<(string Id, string Path, double Score, List<string> Reasons)>();
            foreach (RepositoryEntity repo in repos)
            {
                double score = 0.2;
                var reasons = new List<string> { "repository root" };
                List<ProjectEntity> projects = await db.Projects.AsNoTracking().Where(p => p.RepositoryId == repo.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (ProjectEntity project in projects)
                {
                    double projectScore = 0.2;
                    var projectReasons = new List<string> { "project '" + project.Name + "'" };
                    if (!string.IsNullOrEmpty(role) && project.ProjectType.Contains(role, StringComparison.OrdinalIgnoreCase))
                    {
                        projectScore += 0.3;
                        projectReasons.Add("role matches project type");
                    }

                    if (!string.IsNullOrEmpty(kind) && project.ProjectType.Contains(kind, StringComparison.OrdinalIgnoreCase))
                    {
                        projectScore += 0.2;
                        projectReasons.Add("kind matches project type");
                    }

                    string? folderId = await FindNodeByRepoPathAsync(accountId, repo.ProviderFolderId, project.Path, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
                    if (folderId is null)
                    {
                        continue;
                    }

                    // Sibling similarity: same-kind files already inside score higher.
                    List<NodeEntity> siblings = await db.Nodes.AsNoTracking().Where(n => n.AccountId == accountId && n.ParentId == folderId && !n.Trashed).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
                    int sameKind = 0;
                    foreach (NodeEntity sibling in siblings)
                    {
                        NodeInsightEntity? si = await db.NodeInsights.FindAsync(new object[] { sibling.Id }, cancellationToken).ConfigureAwait(false);
                        if (si is not null && string.Equals(si.Kind, kind, StringComparison.Ordinal))
                        {
                            sameKind++;
                        }
                    }

                    if (sameKind > 0)
                    {
                        projectScore += Math.Min(0.4, sameKind * 0.1);
                        projectReasons.Add(sameKind + " same-kind siblings");
                    }

                    ranked.Add((folderId, project.Path, projectScore, projectReasons));
                }

                ranked.Add((repo.ProviderFolderId, "/", score, reasons));
            }

            var best = ranked.OrderByDescending(r => r.Score).FirstOrDefault();
            if (best.Id is null)
            {
                continue;
            }

            if (string.Equals(best.Id, node.ParentId, StringComparison.Ordinal))
            {
                continue;
            }

            double confidence = Math.Min(0.95, 0.3 + best.Score);
            suggestions.Add(new OrganizeSuggestionDto(nodeId, best.Id, best.Path, confidence, best.Reasons));
        }

        return Result.Ok<IReadOnlyList<OrganizeSuggestionDto>>(suggestions);
    }

    public async Task<Result<EmptyResult>> ApplyOrganizeAsync(OrganizeApplyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        var moves = new List<JournalMove>();
        foreach (OrganizeMoveDto move in request.Moves.Take(500))
        {
            NodeEntity? node = await FindNodeAsync(accountId, move.NodeId, cancellationToken).ConfigureAwait(false);
            if (node is null)
            {
                return Result.Fail<EmptyResult>(BackendResults.NotFound("Node '" + move.NodeId + "' was not found."));
            }

            string? from = node.ParentId;
            var moved = await resolved.Value.Provider.MoveAsync(move.NodeId, move.DestinationId, cancellationToken).ConfigureAwait(false);
            if (!moved.IsSuccess || moved.Value is null)
            {
                return Result.Fail<EmptyResult>(BackendResults.ToBridge(moved.Failure!));
            }

            await MirrorNodeAsync(accountId, moved.Value, cancellationToken).ConfigureAwait(false);
            moves.Add(new JournalMove(move.NodeId, from));
        }

        await JournalAsync(accountId, "organize.apply", null, "Organized " + moves.Count + " items.", true, null, JsonOf(moves), cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<SettingsDto>> GetOrganizeSettingsAsync(CancellationToken cancellationToken)
    {
        // Organizer settings ride on the shared settings service (same schema version).
        return await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<DuplicateGroupDto>>> ScanDuplicatesAsync(CancellationToken cancellationToken)
    {
        return await ListDuplicatesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<DuplicateGroupDto>>> ListDuplicatesAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var found = await _nodes.FindDuplicatesAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!found.IsSuccess || found.Value is null)
        {
            return Result.Fail<IReadOnlyList<DuplicateGroupDto>>(BackendResults.ToBridge(found.Failure!));
        }

        var groups = new Dictionary<string, List<NodeEntity>>(StringComparer.Ordinal);
        foreach (NodeEntity node in found.Value)
        {
            if (node.IsFolder || node.Trashed || string.IsNullOrEmpty(node.Md5))
            {
                continue;
            }

            string key = node.Md5 + ":" + node.Size;
            if (!groups.TryGetValue(key, out List<NodeEntity>? members))
            {
                members = new List<NodeEntity>();
                groups[key] = members;
            }

            members.Add(node);
        }

        var result = new List<DuplicateGroupDto>();
        foreach (KeyValuePair<string, List<NodeEntity>> group in groups)
        {
            if (group.Value.Count < 2)
            {
                continue;
            }

            var dtos = new List<NodeDto>();
            foreach (NodeEntity member in group.Value)
            {
                dtos.Add(await ToDtoAsync(member, cancellationToken).ConfigureAwait(false));
            }

            result.Add(new DuplicateGroupDto(group.Value[0].Md5!, group.Value[0].Size, dtos));
        }

        return Result.Ok<IReadOnlyList<DuplicateGroupDto>>(result);
    }

    public async Task<Result<EmptyResult>> ResolveDuplicatesAsync(ResolveDuplicatesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? kept = await FindNodeAsync(accountId, request.KeepNodeId, cancellationToken).ConfigureAwait(false);
        if (kept is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Kept node was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        foreach (string trashId in request.TrashNodeIds)
        {
            if (string.Equals(trashId, request.KeepNodeId, StringComparison.Ordinal))
            {
                continue;
            }

            var trashed = await resolved.Value.Provider.TrashAsync(trashId, cancellationToken).ConfigureAwait(false);
            if (!trashed.IsSuccess || trashed.Value is null)
            {
                return Result.Fail<EmptyResult>(BackendResults.ToBridge(trashed.Failure!));
            }

            await MirrorNodeAsync(accountId, trashed.Value, cancellationToken).ConfigureAwait(false);
        }

        await JournalAsync(accountId, "duplicates.resolve", kept.Id, "Duplicates resolved, kept '" + kept.Name + "'.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    private sealed record JournalMove(string NodeId, string? FromParent);
}
