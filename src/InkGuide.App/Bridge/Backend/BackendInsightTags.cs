using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Analysis.Tags;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Platform.Ai;
using InkGuide.Infrastructure.Storage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    public async Task<Result<InsightDto>> GetInsightAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeInsightEntity? row = await db.NodeInsights.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
        if (row is null || !string.Equals(row.AccountId, accountId, StringComparison.Ordinal))
        {
            return Result.Fail<InsightDto>(BackendResults.NotFound("Insight for '" + nodeId + "' was not found."));
        }

        return Result.Ok(ToInsightDto(row));
    }

    public async Task<Result<InsightDto>> ReanalyzeAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext check = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeInsightEntity? existing = await check.NodeInsights.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
        if (existing is not null && string.Equals(existing.Source, "user", StringComparison.OrdinalIgnoreCase))
        {
            // User-authored text is never overwritten automatically (precedence user > ai > rule).
            return Result.Ok(ToInsightDto(existing));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<InsightDto>(resolved.Failure!);
        }

        try
        {
            await _sync.AnalyzeOneAsync(accountId, resolved.Value.Provider, nodeId, cancellationToken).ConfigureAwait(false);
            await MaybeEnrichAiAsync(accountId, resolved.Value, nodeId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return Result.Fail<InsightDto>(BackendResults.Internal("Re-analysis failed: " + ex.Message));
        }

        return await GetInsightAsync(nodeId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<InsightDto>> SetSummaryAsync(string nodeId, SetSummaryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? node = await FindNodeAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
        if (node is null)
        {
            return Result.Fail<InsightDto>(BackendResults.NotFound("Node '" + nodeId + "' was not found."));
        }

        await using InkGuideDbContext existingDb = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeInsightEntity? prior = await existingDb.NodeInsights.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
        string oldSummary = prior?.Summary ?? string.Empty;

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            NodeInsightEntity? row = await db.NodeInsights.FindAsync(new object[] { nodeId }, ct).ConfigureAwait(false);
            if (row is null)
            {
                row = new NodeInsightEntity { NodeId = nodeId, AccountId = accountId, Kind = "unknown" };
                db.NodeInsights.Add(row);
            }

            row.AccountId = accountId;
            row.Summary = request.Summary ?? string.Empty;
            row.Source = "user";
            row.Confidence = 1.0;
            row.AnalyzedAtUtc = _clock.GetUtcNow();
            row.RulesetVersion = _holder.Current.RulesetVersion;
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        // A user-authored description is also written back to Drive (durable + visible).
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (resolved.IsSuccess && resolved.Value is not null)
        {
            var wrote = await resolved.Value.Provider.SetMetadataAsync(nodeId, request.Summary, null, cancellationToken).ConfigureAwait(false);
            if (wrote.IsSuccess && wrote.Value is not null)
            {
                await MirrorNodeAsync(accountId, wrote.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        await JournalAsync(accountId, "insight.setSummary", nodeId, "Description edited.", true, JsonOf(new { oldSummary }), null, cancellationToken).ConfigureAwait(false);
        await _sync.IndexForSearchAsync(accountId, nodeId, null, cancellationToken).ConfigureAwait(false);
        return await GetInsightAsync(nodeId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<InsightDto>> SetKindAsync(string nodeId, SetKindRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? node = await FindNodeAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
        if (node is null)
        {
            return Result.Fail<InsightDto>(BackendResults.NotFound("Node '" + nodeId + "' was not found."));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            NodeInsightEntity? row = await db.NodeInsights.FindAsync(new object[] { nodeId }, ct).ConfigureAwait(false);
            if (row is null)
            {
                row = new NodeInsightEntity { NodeId = nodeId, AccountId = accountId };
                db.NodeInsights.Add(row);
            }

            row.AccountId = accountId;
            row.Kind = request.Kind;
            row.Source = "user";
            row.AnalyzedAtUtc = _clock.GetUtcNow();
            row.RulesetVersion = _holder.Current.RulesetVersion;
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "insight.setKind", nodeId, "Kind set to '" + request.Kind + "'.", true, null, null, cancellationToken).ConfigureAwait(false);
        return await GetInsightAsync(nodeId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<InsightDto>>> BulkReanalyzeAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        var list = new List<InsightDto>();
        foreach (string nodeId in nodeIds.Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var one = await ReanalyzeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            if (one.IsSuccess && one.Value is not null)
            {
                list.Add(one.Value);
            }
        }

        return Result.Ok<IReadOnlyList<InsightDto>>(list);
    }

    public async Task<Result<QueueStatusDto>> GetInsightQueueStatusAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int queued = await db.AnalysisJobs.AsNoTracking().CountAsync(j => j.AccountId == accountId && j.State == "Queued", cancellationToken).ConfigureAwait(false);
        return Result.Ok(new QueueStatusDto(queued, 0));
    }

    public async Task<Result<IReadOnlyList<TagDto>>> ListTagsAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<TagEntity> defs = await db.Tags.AsNoTracking().Where(t => t.AccountId == accountId).OrderBy(t => t.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<NodeTagEntity> assigns = await db.NodeTags.AsNoTracking().Where(t => t.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<TagDto>();
        foreach (TagEntity def in defs)
        {
            int usage = assigns.Count(a => string.Equals(a.TagId, def.Id, StringComparison.Ordinal));
            list.Add(new TagDto(def.Id, def.Name, def.Namespace, def.Description, def.Origin, def.ColorToken, def.IsSystem, usage));
        }

        return Result.Ok<IReadOnlyList<TagDto>>(list);
    }

    public async Task<Result<TagDto>> CreateTagAsync(TagDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Fail<TagDto>(BackendResults.InvalidParams("Tag name is empty."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext check = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<string> existingNames = await check.Tags.AsNoTracking().Where(t => t.AccountId == accountId).Select(t => t.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (existingNames.Any(n => string.Equals(n, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail<TagDto>(new OperationError("ALREADY_EXISTS", "A tag named '" + request.Name.Trim() + "' already exists.", false, null));
        }

        string tagId = "tag-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12];
        string? ns = request.Name.Contains(':') ? request.Name[..request.Name.IndexOf(':')] : request.Namespace;
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.Tags.Add(new TagEntity
            {
                Id = tagId,
                AccountId = accountId,
                Name = request.Name.Trim(),
                Namespace = ns,
                Description = request.Description,
                Origin = "user",
                ColorToken = request.ColorToken,
                UpdatedAtUtc = _clock.GetUtcNow(),
            });
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        // Mirror the definition into every repo manifest for portability.
        await MirrorTagDefEverywhereAsync(accountId, new ManifestTagDef(tagId, request.Name.Trim(), ns, request.Description, "user", request.ColorToken, false), cancellationToken).ConfigureAwait(false);
        await JournalAsync(accountId, "tags.create", null, "Tag '" + request.Name.Trim() + "' created.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new TagDto(tagId, request.Name.Trim(), ns, request.Description, "user", request.ColorToken, false, 0));
    }

    public async Task<Result<TagDto>> UpdateTagAsync(string id, TagDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        TagEntity? row = await db.Tags.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
        if (row is null || !string.Equals(row.AccountId, accountId, StringComparison.Ordinal))
        {
            return Result.Fail<TagDto>(BackendResults.NotFound("Tag '" + id + "' was not found."));
        }

        if (!string.IsNullOrWhiteSpace(request.Name) && !string.Equals(request.Name.Trim(), row.Name, StringComparison.OrdinalIgnoreCase))
        {
            List<string> siblingNames = await db.Tags.AsNoTracking().Where(t => t.AccountId == accountId && t.Id != id).Select(t => t.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (siblingNames.Any(n => string.Equals(n, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Fail<TagDto>(new OperationError("ALREADY_EXISTS", "A tag named '" + request.Name.Trim() + "' already exists.", false, null));
            }
        }

        await _gate.ExecuteWriteAsync(async ct =>
        {
            TagEntity? target = await db.Tags.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (target is not null)
            {
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    target.Name = request.Name.Trim();
                }

                target.Namespace = request.Namespace;
                target.Description = request.Description;
                target.ColorToken = request.ColorToken;
                target.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        await MirrorTagDefEverywhereAsync(accountId, new ManifestTagDef(id, row.Name, row.Namespace, row.Description, row.Origin, row.ColorToken, row.IsSystem), cancellationToken).ConfigureAwait(false);
        return Result.Ok(new TagDto(id, row.Name, row.Namespace, row.Description, row.Origin, row.ColorToken, row.IsSystem, await TagUsageAsync(accountId, id, cancellationToken).ConfigureAwait(false)));
    }

    public async Task<Result<TagDto>> MergeTagsAsync(string id, MergeTagsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        TagEntity? source = await db.Tags.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
        TagEntity? target = await db.Tags.FindAsync(new object[] { request.IntoTagId }, cancellationToken).ConfigureAwait(false);
        if (source is null || target is null ||
            !string.Equals(source.AccountId, accountId, StringComparison.Ordinal) ||
            !string.Equals(target.AccountId, accountId, StringComparison.Ordinal))
        {
            return Result.Fail<TagDto>(BackendResults.NotFound("Tag was not found."));
        }

        await _gate.ExecuteWriteAsync(async ct =>
        {
            List<NodeTagEntity> assigns = await db.NodeTags.Where(t => t.TagId == id).ToListAsync(ct).ConfigureAwait(false);
            foreach (NodeTagEntity assign in assigns)
            {
                bool exists = await db.NodeTags.AnyAsync(t => t.TagId == target.Id && t.NodeId == assign.NodeId, ct).ConfigureAwait(false);
                if (exists)
                {
                    db.NodeTags.Remove(assign);
                }
                else
                {
                    assign.TagId = target.Id;
                }
            }

            TagEntity? doomed = await db.Tags.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (doomed is not null)
            {
                db.Tags.Remove(doomed);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "tags.merge", null, "Tag '" + source.Name + "' merged into '" + target.Name + "'.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new TagDto(target.Id, target.Name, target.Namespace, target.Description, target.Origin, target.ColorToken, target.IsSystem, await TagUsageAsync(accountId, target.Id, cancellationToken).ConfigureAwait(false)));
    }

    public async Task<Result<EmptyResult>> DeleteTagAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        TagEntity? row = await db.Tags.FindAsync(new object[] { id }, cancellationToken).ConfigureAwait(false);
        if (row is null || !string.Equals(row.AccountId, accountId, StringComparison.Ordinal))
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Tag '" + id + "' was not found."));
        }

        await _gate.ExecuteWriteAsync(async ct =>
        {
            List<NodeTagEntity> assigns = await db.NodeTags.Where(t => t.TagId == id).ToListAsync(ct).ConfigureAwait(false);
            foreach (NodeTagEntity assign in assigns)
            {
                db.NodeTags.Remove(assign);
            }

            TagEntity? doomed = await db.Tags.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (doomed is not null)
            {
                db.Tags.Remove(doomed);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "tags.delete", null, "Tag '" + row.Name + "' deleted.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> AssignTagAsync(AssignTagRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await AssignTagToIdAsync(request.TagId, request.NodeId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EmptyResult>> UnassignTagAsync(AssignTagRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        TagEntity? tag = await db.Tags.FindAsync(new object[] { request.TagId }, cancellationToken).ConfigureAwait(false);
        NodeEntity? node = await FindNodeAsync(accountId, request.NodeId, cancellationToken).ConfigureAwait(false);
        if (tag is null || node is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Tag or node was not found."));
        }

        var unassigned = await _tags.UnassignAsync(request.TagId, request.NodeId, cancellationToken).ConfigureAwait(false);
        if (!unassigned.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(unassigned.Failure!));
        }

        await _sync.IndexForSearchAsync(accountId, request.NodeId, null, cancellationToken).ConfigureAwait(false);

        // Removing an auto tag records a suppression so it is never re-applied.
        if (string.Equals(tag.Origin, "auto", StringComparison.OrdinalIgnoreCase))
        {
            await SuppressTagAsync(accountId, null, tag.Name, node.Id, cancellationToken).ConfigureAwait(false);
        }

        RepositoryEntity? repo = await FindRepoForNodeAsync(accountId, node, cancellationToken).ConfigureAwait(false);
        if (repo is not null)
        {
            Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (resolved.IsSuccess && resolved.Value is not null)
            {
                _ = await _manifests.UnassignTagAsync(resolved.Value.Provider, ManifestScope(resolved.Value), repo.ProviderFolderId, node.Id, request.TagId, cancellationToken).ConfigureAwait(false);
            }
        }

        await JournalAsync(accountId, "tags.unassign", node.Id, "Tag '" + tag.Name + "' removed.", true, null, JsonOf(new { tagId = tag.Id, nodeId = node.Id }), cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> BulkAssignTagAsync(BulkAssignRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (string nodeId in request.NodeIds.Take(500))
        {
            var one = await AssignTagToIdAsync(request.TagId, nodeId, cancellationToken).ConfigureAwait(false);
            if (!one.IsSuccess)
            {
                return one;
            }
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<int>> GetTagUsageAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(await TagUsageAsync(accountId, id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<TagDto>>> ListTagSuggestionsAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<IReadOnlyList<TagDto>>(resolved.Failure!);
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> repos = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var suggestions = new Dictionary<string, TagDto>(StringComparer.OrdinalIgnoreCase);
        foreach (RepositoryEntity repo in repos)
        {
            var detected = await DetectAndTagTreeAsync(accountId, resolved.Value, repo, cancellationToken).ConfigureAwait(false);
            if (!detected.IsSuccess || detected.Value is null)
            {
                continue;
            }

            // Collect below-threshold tags as suggestions (auto-applied ones are already assigned).
            foreach (var project in detected.Value)
            {
                string? folderId = string.IsNullOrEmpty(project.RelativePath)
                    ? repo.ProviderFolderId
                    : await FindNodeByRepoPathAsync(accountId, repo.ProviderFolderId, project.RelativePath, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
                if (folderId is null)
                {
                    continue;
                }

                List<string> assigned = (await ProjectTagsForNodeAsync(accountId, folderId, cancellationToken).ConfigureAwait(false)).ToList();
                List<string> suppressed = await SuppressedNamesAsync(accountId, cancellationToken).ConfigureAwait(false);
                foreach (string tag in project.Tags)
                {
                    if (assigned.Contains(tag) || suppressed.Contains(tag) || suggestions.ContainsKey(tag + "@" + folderId))
                    {
                        continue;
                    }

                    suggestions[tag + "@" + folderId] = new TagDto("suggest:" + folderId + ":" + tag, tag, null, null, "auto", null, false, 0);
                }
            }
        }

        return Result.Ok<IReadOnlyList<TagDto>>(suggestions.Values.OrderBy(t => t.Name).ToList());
    }

    public async Task<Result<EmptyResult>> AcceptTagSuggestionAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        // Suggestion id shape: "suggest:{nodeId}:{tag}".
        string[] parts = id.Split(':');
        if (parts.Length < 3 || parts[0] != "suggest")
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Suggestion id is malformed."));
        }

        string nodeId = parts[1];
        string tag = string.Join(":", parts.Skip(2));
        var assigned = await AssignTagToIdAsync(await EnsureNamedTagAsync(tag, cancellationToken).ConfigureAwait(false), nodeId, cancellationToken).ConfigureAwait(false);
        if (!assigned.IsSuccess)
        {
            return assigned;
        }

        // Acceptance overrides any node-scoped suppression.
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            List<TagSuppressionEntity> rows = await db.TagSuppressions.Where(s => s.AccountId == accountId && s.TagName == tag && s.NodeId == nodeId).ToListAsync(ct).ConfigureAwait(false);
            foreach (TagSuppressionEntity row in rows)
            {
                db.TagSuppressions.Remove(row);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> DismissTagSuggestionAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string[] parts = id.Split(':');
        if (parts.Length < 3 || parts[0] != "suggest")
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Suggestion id is malformed."));
        }

        string nodeId = parts[1];
        string tag = string.Join(":", parts.Skip(2));
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await SuppressTagAsync(accountId, nodeId, tag, nodeId, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    private async Task<Result<EmptyResult>> AssignTagToIdAsync(string tagId, string nodeId, CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        TagEntity? tag = await db.Tags.FindAsync(new object[] { tagId }, cancellationToken).ConfigureAwait(false);
        NodeEntity? node = await FindNodeAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
        if (tag is null || node is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Tag or node was not found."));
        }

        var assigned = await _tags.AssignAsync(accountId, tagId, nodeId, cancellationToken).ConfigureAwait(false);
        if (!assigned.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(assigned.Failure!));
        }

        await _sync.IndexForSearchAsync(accountId, nodeId, null, cancellationToken).ConfigureAwait(false);

        RepositoryEntity? repo = await FindRepoForNodeAsync(accountId, node, cancellationToken).ConfigureAwait(false);
        if (repo is not null)
        {
            Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (resolved.IsSuccess && resolved.Value is not null)
            {
                _ = await _manifests.AssignTagAsync(resolved.Value.Provider, ManifestScope(resolved.Value), repo.ProviderFolderId, nodeId, tagId, cancellationToken).ConfigureAwait(false);
            }
        }

        await JournalAsync(accountId, "tags.assign", nodeId, "Tag '" + tag.Name + "' assigned.", true, null, JsonOf(new { tagId = tag.Id, nodeId }), cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    private async Task<string> EnsureNamedTagAsync(string name, CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        TagEntity? existing = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.AccountId == accountId && t.Name == name, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        string tagId = "tag-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12];
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.Tags.Add(new TagEntity { Id = tagId, AccountId = accountId, Name = name, Origin = "auto", UpdatedAtUtc = _clock.GetUtcNow() });
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return tagId;
    }

    private async Task SuppressTagAsync(string accountId, string? nodeId, string tagName, string scopeNodeId, CancellationToken cancellationToken)
    {
        _ = scopeNodeId;
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            bool exists = await db.TagSuppressions.AnyAsync(s => s.AccountId == accountId && s.TagName == tagName && s.NodeId == nodeId, ct).ConfigureAwait(false);
            if (!exists)
            {
                db.TagSuppressions.Add(new TagSuppressionEntity
                {
                    AccountId = accountId,
                    TagName = tagName,
                    NodeId = nodeId,
                    SuppressedAtUtc = _clock.GetUtcNow(),
                });
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> TagUsageAsync(string accountId, string tagId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.NodeTags.AsNoTracking().CountAsync(t => t.AccountId == accountId && t.TagId == tagId, cancellationToken).ConfigureAwait(false);
    }

    private async Task MirrorTagDefEverywhereAsync(string accountId, ManifestTagDef def, CancellationToken cancellationToken)
    {
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return;
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> repos = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (RepositoryEntity repo in repos)
        {
            _ = await _manifests.SaveTagDefAsync(resolved.Value.Provider, ManifestScope(resolved.Value), repo.ProviderFolderId, def, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<RepositoryEntity?> FindRepoForNodeAsync(string accountId, NodeEntity node, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(node.RepoId))
        {
            return await FindRepoAsync(accountId, node.RepoId, cancellationToken).ConfigureAwait(false);
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> repos = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        string? parent = node.ParentId;
        for (int i = 0; i < 64 && parent is not null; i++)
        {
            foreach (RepositoryEntity repo in repos)
            {
                if (string.Equals(repo.ProviderFolderId, parent, StringComparison.Ordinal))
                {
                    return repo;
                }
            }

            NodeEntity? row = await FindNodeAsync(accountId, parent, cancellationToken).ConfigureAwait(false);
            parent = row?.ParentId;
        }

        return null;
    }

    private async Task<IReadOnlyList<string>> ProjectTagsForNodeAsync(string accountId, string folderId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<NodeTagEntity> assigns = await db.NodeTags.AsNoTracking().Where(t => t.NodeId == folderId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = new List<string>();
        foreach (NodeTagEntity assign in assigns)
        {
            TagEntity? tag = await db.Tags.FindAsync(new object[] { assign.TagId }, cancellationToken).ConfigureAwait(false);
            if (tag is not null && string.Equals(tag.AccountId, accountId, StringComparison.Ordinal))
            {
                names.Add(tag.Name);
            }
        }

        return names;
    }

    private async Task MaybeEnrichAiAsync(string accountId, ResolvedProvider resolved, string nodeId, CancellationToken cancellationToken)
    {
        await SyncAiOptionsFromSettingsAsync(cancellationToken).ConfigureAwait(false);
        string enabled = await _settings.GetAsync(BackendSettingsService.Keys.AiEnabled, cancellationToken).ConfigureAwait(false);
        string consent = await _settings.GetAsync(BackendSettingsService.Keys.AiConsentAt, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(consent))
        {
            return;
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeInsightEntity? row = await db.NodeInsights.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
        if (row is null || row.Sensitive || string.Equals(row.Source, "user", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        NodeEntity? node = await FindNodeAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
        if (node is null || node.IsFolder)
        {
            return;
        }

        await using var buffer = new MemoryStream();
        var downloaded = await resolved.Provider.DownloadAsync(nodeId, buffer, new ContentRange(0, 6143), cancellationToken).ConfigureAwait(false);
        if (!downloaded.IsSuccess)
        {
            return;
        }

        string excerpt = TextCodec.Decode(buffer.ToArray()).Text;
        if (excerpt.Length > 6144)
        {
            excerpt = excerpt[..6144];
        }

        var described = await _ai.DescribeAsync(
            new DescribeRequest(nodeId, node.Name, node.MimeType, "{\"kind\":\"" + row.Kind + "\"}", excerpt, Array.Empty<string>(), null, false),
            cancellationToken).ConfigureAwait(false);
        if (!described.IsSuccess || described.Value is null)
        {
            return;
        }

        await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext inner = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            NodeInsightEntity? target = await inner.NodeInsights.FindAsync(new object[] { nodeId }, ct).ConfigureAwait(false);
            if (target is not null && !string.Equals(target.Source, "user", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(described.Value.Summary))
                {
                    target.Summary = described.Value.Summary;
                }

                if (!string.IsNullOrWhiteSpace(described.Value.Role))
                {
                    target.Role = described.Value.Role;
                }

                target.Source = "ai";
                target.Confidence = described.Value.Confidence;
                target.AnalyzedAtUtc = _clock.GetUtcNow();
                _ = await inner.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncAiOptionsFromSettingsAsync(CancellationToken cancellationToken)
    {
        // The provider reads these live; settings are the owner-facing surface.
        string model = await _settings.GetAsync(BackendSettingsService.Keys.AiModel, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(model))
        {
            _aiOptions.Model = model;
        }

        string budget = await _settings.GetAsync(BackendSettingsService.Keys.AiBudgetPerDay, cancellationToken).ConfigureAwait(false);
        if (int.TryParse(budget, out int perDay) && perDay > 0)
        {
            _aiOptions.BudgetPerDay = perDay;
        }

        string enabled = await _settings.GetAsync(BackendSettingsService.Keys.AiEnabled, cancellationToken).ConfigureAwait(false);
        string consent = await _settings.GetAsync(BackendSettingsService.Keys.AiConsentAt, cancellationToken).ConfigureAwait(false);
        _aiOptions.Enabled = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
        _aiOptions.ConsentAcknowledged = !string.IsNullOrEmpty(consent);
        if (DateTimeOffset.TryParse(consent, out DateTimeOffset at))
        {
            _aiOptions.ConsentAtUtc = at;
        }
    }

    private static InsightDto ToInsightDto(NodeInsightEntity row)
    {
        return new InsightDto(
            row.NodeId, row.Kind, row.Role, row.Summary, row.Confidence,
            row.Source, row.Sensitive, row.AnalyzedAtUtc.ToString("o"));
    }
}
