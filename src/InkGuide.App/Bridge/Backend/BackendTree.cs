using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Analysis.Projects;
using InkGuide.Analysis.Tags;
using InkGuide.App.Hosting;
using InkGuide.Bridge.Common;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Storage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    /// <summary>Finds a cached node id by repo-relative path (DB mirror).</summary>
    private async Task<string?> FindNodeIdByPathAsync(string accountId, string repoRelativePath, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<NodeEntity> candidates = await db.Nodes.AsNoTracking()
            .Where(n => n.AccountId == accountId && n.PathText == "/" + repoRelativePath.Trim('/'))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return candidates.FirstOrDefault()?.Id;
    }

    /// <summary>Walks the provider tree following a '/'-separated path under a root folder.</summary>
    private static async Task<string?> FindNodeByRepoPathAsync(
        string accountId,
        string rootFolderId,
        string relativePath,
        IStorageProvider provider,
        CancellationToken cancellationToken)
    {
        _ = accountId;
        string current = rootFolderId;
        string trimmed = relativePath.Trim('/');
        if (string.IsNullOrEmpty(trimmed))
        {
            return current;
        }

        foreach (string segment in trimmed.Split('/'))
        {
            string? cursor = null;
            string? found = null;
            do
            {
                var page = await provider.ListChildrenAsync(current, cursor, 200, cancellationToken).ConfigureAwait(false);
                if (!page.IsSuccess || page.Value is null)
                {
                    return null;
                }

                foreach (var item in page.Value.Items)
                {
                    if (!item.Trashed && string.Equals(item.Name, segment, StringComparison.Ordinal))
                    {
                        found = item.Id;
                        break;
                    }
                }

                cursor = page.Value.NextCursor;
            }
            while (found is null && cursor is not null);

            if (found is null)
            {
                return null;
            }

            current = found;
        }

        return current;
    }

    /// <summary>
    /// Ensures nested folders for a '/'-separated path, mirroring every level
    /// into the DB so browsing sees the whole chain. Returns the deepest id.
    /// </summary>
    private async Task<string?> MirrorRemotePathAsync(
        string accountId,
        IStorageProvider provider,
        string rootFolderId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        string current = rootFolderId;
        foreach (string segment in relativePath.Trim('/').Split('/'))
        {
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            string? found = null;
            string? cursor = null;
            do
            {
                var page = await provider.ListChildrenAsync(current, cursor, 200, cancellationToken).ConfigureAwait(false);
                if (!page.IsSuccess || page.Value is null)
                {
                    return null;
                }

                foreach (var item in page.Value.Items)
                {
                    if (item.IsFolder && !item.Trashed && string.Equals(item.Name, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        found = item.Id;
                        break;
                    }
                }

                cursor = page.Value.NextCursor;
            }
            while (found is null && cursor is not null);

            if (found is null)
            {
                var created = await provider.CreateFolderAsync(current, segment, cancellationToken).ConfigureAwait(false);
                if (!created.IsSuccess || created.Value is null)
                {
                    return null;
                }

                found = created.Value.Id;
            }

            var mirrored = await provider.GetAsync(found, cancellationToken).ConfigureAwait(false);
            if (mirrored.IsSuccess && mirrored.Value is not null)
            {
                await MirrorNodeAsync(accountId, mirrored.Value, cancellationToken).ConfigureAwait(false);
            }

            current = found;
        }

        return current;
    }

    /// <summary>Creates nested folders for a '/'-separated path; returns the deepest folder id.</summary>
    private static async Task<string?> EnsureRemotePathAsync(IStorageProvider provider, string rootFolderId, string relativePath, CancellationToken cancellationToken)
    {
        string current = rootFolderId;
        foreach (string segment in relativePath.Trim('/').Split('/'))
        {
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            string? found = null;
            string? cursor = null;
            do
            {
                var page = await provider.ListChildrenAsync(current, cursor, 200, cancellationToken).ConfigureAwait(false);
                if (!page.IsSuccess || page.Value is null)
                {
                    return null;
                }

                foreach (var item in page.Value.Items)
                {
                    if (item.IsFolder && !item.Trashed && string.Equals(item.Name, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        found = item.Id;
                        break;
                    }
                }

                cursor = page.Value.NextCursor;
            }
            while (found is null && cursor is not null);

            if (found is null)
            {
                var created = await provider.CreateFolderAsync(current, segment, cancellationToken).ConfigureAwait(false);
                if (!created.IsSuccess || created.Value is null)
                {
                    return null;
                }

                found = created.Value.Id;
            }

            current = found;
        }

        return current;
    }

    /// <summary>
    /// Detects projects in a repo tree, persists them (manifest + DB mirror),
    /// and auto-applies confident tags to project folders (suppressions respected).
    /// </summary>
    private async Task<Result<IReadOnlyList<DetectedProject>>> DetectAndTagTreeAsync(
        string accountId,
        ResolvedProvider resolved,
        RepositoryEntity repo,
        CancellationToken cancellationToken)
    {
        var relativePaths = new List<string>();
        var queue = new Queue<(string FolderId, string Prefix)>();
        queue.Enqueue((repo.ProviderFolderId, string.Empty));
        int listed = 0;
        while (queue.Count > 0 && listed < 5000)
        {
            (string folderId, string prefix) = queue.Dequeue();
            string? cursor = null;
            do
            {
                var page = await resolved.Provider.ListChildrenAsync(folderId, cursor, 200, cancellationToken).ConfigureAwait(false);
                if (!page.IsSuccess || page.Value is null)
                {
                    return Result.Fail<IReadOnlyList<DetectedProject>>(BackendResults.ToBridge(page.Failure!));
                }

                foreach (var item in page.Value.Items)
                {
                    if (item.Trashed || string.Equals(item.Name, ".inkguide", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string relative = string.IsNullOrEmpty(prefix) ? item.Name : prefix + "/" + item.Name;
                    relativePaths.Add(item.IsFolder ? relative + "/" : relative);
                    listed++;
                    if (item.IsFolder && relative.Split('/').Length <= 5)
                    {
                        queue.Enqueue((item.Id, relative));
                    }
                }

                cursor = page.Value.NextCursor;
            }
            while (cursor is not null && listed < 5000);
        }

        var detector = new ProjectDetector(_holder.Current.ProjectMarkers);
        ProjectDetectionResult detection = detector.Detect(relativePaths, null);

        string thresholdRaw = await _settings.GetAsync(BackendSettingsService.Keys.AnalysisConfidenceThreshold, cancellationToken).ConfigureAwait(false);
        double threshold = double.TryParse(thresholdRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : TagDecider.DefaultAutoApplyThreshold;

        List<string> suppressed = await SuppressedNamesAsync(accountId, cancellationToken).ConfigureAwait(false);
        var confidences = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (DetectedProject project in detection.Projects)
        {
            foreach (string tag in project.Tags)
            {
                if (!confidences.TryGetValue(tag, out double current) || project.Confidence > current)
                {
                    confidences[tag] = project.Confidence;
                }
            }
        }

        TagDecision decision = TagDecider.Decide(confidences, suppressed, threshold);

        foreach (DetectedProject project in detection.Projects)
        {
            string path = project.RelativePath.Trim('/');
            string name = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : (string.IsNullOrEmpty(path) ? repo.Name : path);
            string projectId = "proj-" + StableId(repo.Id + ":" + path);
            var entry = new ManifestProject(projectId, path, name, project.DetectorId, project.Tags, true, false);
            var saved = await _manifests.SaveProjectAsync(resolved.Provider, ManifestScope(resolved), repo.ProviderFolderId, entry, cancellationToken).ConfigureAwait(false);
            if (!saved.IsSuccess)
            {
                return Result.Fail<IReadOnlyList<DetectedProject>>(BackendResults.ToBridge(saved.Failure!));
            }

            await UpsertProjectRowAsync(accountId, repo.Id, entry, cancellationToken).ConfigureAwait(false);

            // Auto-apply confident tags onto the project folder node.
            string? folderId = string.IsNullOrEmpty(path)
                ? repo.ProviderFolderId
                : await FindNodeByRepoPathAsync(accountId, repo.ProviderFolderId, path, resolved.Provider, cancellationToken).ConfigureAwait(false);
            if (folderId is not null)
            {
                foreach (string tag in decision.AutoApply)
                {
                    if (project.Tags.Contains(tag))
                    {
                        await AssignTagToNodeAsync(accountId, resolved, repo, folderId, tag, "auto", cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        return Result.Ok<IReadOnlyList<DetectedProject>>(detection.Projects);
    }

    private async Task UpsertProjectRowAsync(string accountId, string repoId, ManifestProject entry, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            ProjectEntity? row = await db.Projects.FindAsync(new object[] { entry.Id }, ct).ConfigureAwait(false);
            if (row is null)
            {
                row = new ProjectEntity { Id = entry.Id, AccountId = accountId, RepositoryId = repoId };
                db.Projects.Add(row);
            }

            row.AccountId = accountId;
            row.RepositoryId = repoId;
            row.Path = entry.Path;
            row.Name = entry.Name;
            row.ProjectType = entry.ProjectType;
            row.Detected = entry.Detected;
            if (!row.Confirmed)
            {
                row.Confirmed = entry.Confirmed;
            }

            row.UpdatedAtUtc = _clock.GetUtcNow();
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ensures a tag definition (DB + repo manifest) and assigns it to a node.</summary>
    private async Task<Result> AssignTagToNodeAsync(
        string accountId,
        ResolvedProvider resolved,
        RepositoryEntity repo,
        string nodeId,
        string tagName,
        string origin,
        CancellationToken cancellationToken)
    {
        string tagId = await EnsureTagDefAsync(accountId, resolved, repo, tagName, origin, cancellationToken).ConfigureAwait(false);
        var assigned = await _tags.AssignAsync(accountId, tagId, nodeId, cancellationToken).ConfigureAwait(false);
        if (!assigned.IsSuccess)
        {
            return BackendResults.FromPersistence(assigned);
        }

        var manifested = await _manifests.AssignTagAsync(resolved.Provider, ManifestScope(resolved), repo.ProviderFolderId, nodeId, tagId, cancellationToken).ConfigureAwait(false);
        if (!manifested.IsSuccess)
        {
            return BackendResults.FromStorage(manifested);
        }

        return Result.Ok();
    }

    private async Task<string> EnsureTagDefAsync(
        string accountId,
        ResolvedProvider resolved,
        RepositoryEntity repo,
        string tagName,
        string origin,
        CancellationToken cancellationToken)
    {
        string tagId = "tag-" + tagName.ToLowerInvariant().Replace(":", "-", StringComparison.Ordinal);
        string? ns = tagName.Contains(':') ? tagName[..tagName.IndexOf(':')] : null;
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            TagEntity? existing = await db.Tags.FindAsync(new object[] { tagId }, ct).ConfigureAwait(false);
            if (existing is null)
            {
                db.Tags.Add(new TagEntity
                {
                    Id = tagId,
                    AccountId = accountId,
                    Name = tagName,
                    Namespace = ns,
                    Origin = origin,
                    UpdatedAtUtc = _clock.GetUtcNow(),
                });
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        var def = new ManifestTagDef(tagId, tagName, ns, null, origin, null, false);
        _ = await _manifests.SaveTagDefAsync(resolved.Provider, ManifestScope(resolved), repo.ProviderFolderId, def, cancellationToken).ConfigureAwait(false);
        return tagId;
    }

    private async Task<List<string>> SuppressedNamesAsync(string accountId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<TagSuppressionEntity> rows = await db.TagSuppressions.AsNoTracking().Where(s => s.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = new List<string>();
        foreach (TagSuppressionEntity row in rows)
        {
            if (!names.Contains(row.TagName))
            {
                names.Add(row.TagName);
            }
        }

        return names;
    }

    private static string StableId(string seed)
    {
        byte[] digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(digest)[..12].ToLowerInvariant();
    }

    /// <summary>Finds the repo id owning a folder by walking cached parents to a repo root.</summary>
    private async Task<string?> FindRepoIdByFolderAsync(string accountId, string folderId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> repos = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        string? current = folderId;
        for (int i = 0; i < 64 && current is not null; i++)
        {
            foreach (RepositoryEntity repo in repos)
            {
                if (string.Equals(repo.ProviderFolderId, current, StringComparison.Ordinal))
                {
                    return repo.Id;
                }
            }

            NodeEntity? row = await FindNodeAsync(accountId, current, cancellationToken).ConfigureAwait(false);
            current = row?.ParentId;
        }

        return null;
    }

    /// <summary>Backfills RepoId/PathText for indexed nodes by walking to the repo root.</summary>
    private async Task BackfillRepoIdsAsync(string accountId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> repos = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var byFolder = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (RepositoryEntity repo in repos)
        {
            byFolder[repo.ProviderFolderId] = repo.Id;
        }

        List<NodeEntity> orphans = await db.Nodes.Where(n => n.AccountId == accountId && n.RepoId == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (orphans.Count == 0)
        {
            return;
        }

        Dictionary<string, NodeEntity> index = (await db.Nodes.AsNoTracking().Where(n => n.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(n => n.Id, StringComparer.Ordinal);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            foreach (NodeEntity orphan in orphans)
            {
                NodeEntity? current = orphan;
                var trail = new List<string>();
                for (int i = 0; i < 64 && current is not null; i++)
                {
                    trail.Add(current.Name);
                    if (current.ParentId is not null && byFolder.TryGetValue(current.ParentId, out string? repoId))
                    {
                        trail.Add(string.Empty);
                        NodeEntity? tracked = await db.Nodes.FindAsync(new object[] { orphan.Id }, ct).ConfigureAwait(false);
                        if (tracked is not null)
                        {
                            tracked.RepoId = repoId;
                            trail.Reverse();
                            tracked.PathText = "/" + string.Join('/', trail).Trim('/');
                        }

                        break;
                    }

                    if (current.ParentId is null || !index.TryGetValue(current.ParentId, out NodeEntity? parent))
                    {
                        break;
                    }

                    current = parent;
                }
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
