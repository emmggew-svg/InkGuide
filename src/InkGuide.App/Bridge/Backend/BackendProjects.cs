using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Analysis.Catalogs;
using InkGuide.Analysis.Projects;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Persistence.Sync;
using InkGuide.Infrastructure.Storage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    private readonly ConcurrentDictionary<string, ImportOperation> _imports = new(StringComparer.Ordinal);

    public async Task<Result<IReadOnlyList<ProjectDto>>> ListProjectsAsync(string repoId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        RepositoryEntity? repo = await FindRepoAsync(accountId, repoId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<IReadOnlyList<ProjectDto>>(BackendResults.NotFound("Repository '" + repoId + "' was not found."));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<ProjectEntity> rows = await db.Projects.AsNoTracking().Where(p => p.RepositoryId == repoId).OrderBy(p => p.Path).ToListAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<ProjectDto>();
        foreach (ProjectEntity row in rows)
        {
            IReadOnlyList<string> tags = await ProjectTagsAsync(accountId, repo.ProviderFolderId, row.Id, cancellationToken).ConfigureAwait(false);
            list.Add(new ProjectDto(row.Id, row.RepositoryId, row.Path, row.Name, row.ProjectType, tags, row.Detected, row.Confirmed));
        }

        return Result.Ok<IReadOnlyList<ProjectDto>>(list);
    }

    public async Task<Result<ProjectDto>> GetProjectAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        ProjectEntity? row = await FindProjectAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Project '" + id + "' was not found."));
        }

        RepositoryEntity? repo = await FindRepoAsync(accountId, row.RepositoryId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> tags = repo is null
            ? Array.Empty<string>()
            : await ProjectTagsAsync(accountId, repo.ProviderFolderId, row.Id, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new ProjectDto(row.Id, row.RepositoryId, row.Path, row.Name, row.ProjectType, tags, row.Detected, row.Confirmed));
    }

    public async Task<Result<ProjectDto>> ConfirmProjectAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        ProjectEntity? row = await FindProjectAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Project '" + id + "' was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<ProjectDto>(resolved.Failure!);
        }

        RepositoryEntity? repo = await FindRepoAsync(accountId, row.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Repository was not found."));
        }

        var updated = new ManifestProject(row.Id, row.Path, row.Name, row.ProjectType, Array.Empty<string>(), row.Detected, true);
        var saved = await _manifests.SaveProjectAsync(resolved.Value.Provider, ManifestScope(resolved.Value), repo.ProviderFolderId, updated, cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result.Fail<ProjectDto>(BackendResults.ToBridge(saved.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            ProjectEntity? target = await db.Projects.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (target is not null)
            {
                target.Confirmed = true;
                target.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "projects.confirm", id, "Project '" + row.Name + "' confirmed.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new ProjectDto(row.Id, row.RepositoryId, row.Path, row.Name, row.ProjectType, Array.Empty<string>(), row.Detected, true));
    }

    public async Task<Result<ProjectDto>> UpdateProjectAsync(string id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        ProjectEntity? row = await FindProjectAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Project '" + id + "' was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<ProjectDto>(resolved.Failure!);
        }

        RepositoryEntity? repo = await FindRepoAsync(accountId, row.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Repository was not found."));
        }

        string name = string.IsNullOrWhiteSpace(request.Name) ? row.Name : request.Name.Trim();
        string type = string.IsNullOrWhiteSpace(request.ProjectType) ? row.ProjectType : request.ProjectType.Trim();
        var updated = new ManifestProject(row.Id, row.Path, name, type, request.Tags ?? Array.Empty<string>(), row.Detected, row.Confirmed);
        var saved = await _manifests.SaveProjectAsync(resolved.Value.Provider, ManifestScope(resolved.Value), repo.ProviderFolderId, updated, cancellationToken).ConfigureAwait(false);        if (!saved.IsSuccess)
        {
            return Result.Fail<ProjectDto>(BackendResults.ToBridge(saved.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            ProjectEntity? target = await db.Projects.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (target is not null)
            {
                target.Name = name;
                target.ProjectType = type;
                target.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "projects.update", id, "Project '" + name + "' updated.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new ProjectDto(row.Id, row.RepositoryId, row.Path, name, type, request.Tags ?? Array.Empty<string>(), row.Detected, row.Confirmed));
    }

    public async Task<Result<EmptyResult>> RemoveProjectAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        ProjectEntity? row = await FindProjectAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Project '" + id + "' was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        RepositoryEntity? repo = await FindRepoAsync(accountId, row.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Repository was not found."));
        }

        // Project removal drops the logical unit, never the files.
        var removed = await _manifests.RemoveProjectAsync(resolved.Value.Provider, ManifestScope(resolved.Value), repo.ProviderFolderId, id, cancellationToken).ConfigureAwait(false);
        if (!removed.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(removed.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            ProjectEntity? target = await db.Projects.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (target is not null)
            {
                db.Projects.Remove(target);
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "projects.remove", id, "Project '" + row.Name + "' removed (files kept).", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<ProjectDto>> MoveProjectAsync(string id, MoveProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.NewPath))
        {
            return Result.Fail<ProjectDto>(BackendResults.InvalidParams("NewPath is empty."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        ProjectEntity? row = await FindProjectAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Project '" + id + "' was not found."));
        }

        RepositoryEntity? repo = await FindRepoAsync(accountId, row.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Repository was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<ProjectDto>(resolved.Failure!);
        }

        // Locate the project folder by its recorded path, then move it on the provider.
        string? folderId = await FindNodeByRepoPathAsync(accountId, repo.ProviderFolderId, row.Path, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
        if (folderId is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Project folder was not found on the provider."));
        }

        string newPath = request.NewPath.Trim().Trim('/');
        string? newParentPath = newPath.Contains('/') ? newPath[..newPath.LastIndexOf('/')] : string.Empty;
        string? newParentId = string.IsNullOrEmpty(newParentPath)
            ? repo.ProviderFolderId
            : await FindNodeByRepoPathAsync(accountId, repo.ProviderFolderId, newParentPath, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
        if (newParentId is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.NotFound("Destination folder was not found."));
        }

        var moved = await resolved.Value.Provider.MoveAsync(folderId, newParentId, cancellationToken).ConfigureAwait(false);
        if (!moved.IsSuccess || moved.Value is null)
        {
            return Result.Fail<ProjectDto>(BackendResults.ToBridge(moved.Failure!));
        }

        string newName = newPath.Contains('/') ? newPath[(newPath.LastIndexOf('/') + 1)..] : newPath;
        if (!string.Equals(moved.Value.Name, newName, StringComparison.Ordinal))
        {
            var renamed = await resolved.Value.Provider.RenameAsync(folderId, newName, cancellationToken).ConfigureAwait(false);
            if (!renamed.IsSuccess)
            {
                return Result.Fail<ProjectDto>(BackendResults.ToBridge(renamed.Failure!));
            }
        }

        var updated = new ManifestProject(row.Id, newPath, newName, row.ProjectType, Array.Empty<string>(), row.Detected, row.Confirmed);
        var saved = await _manifests.SaveProjectAsync(resolved.Value.Provider, ManifestScope(resolved.Value), repo.ProviderFolderId, updated, cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result.Fail<ProjectDto>(BackendResults.ToBridge(saved.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            ProjectEntity? target = await db.Projects.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (target is not null)
            {
                target.Path = newPath;
                target.Name = newName;
                target.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "projects.move", id, "Project moved to '" + newPath + "'.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new ProjectDto(row.Id, row.RepositoryId, newPath, newName, row.ProjectType, Array.Empty<string>(), row.Detected, row.Confirmed));
    }

    public async Task<Result<ImportPlanDto>> PlanImportAsync(PlanImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LocalPath) || !Directory.Exists(request.LocalPath))
        {
            return Result.Fail<ImportPlanDto>(BackendResults.InvalidParams("LocalPath does not exist."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<ImportPlanDto>(resolved.Failure!);
        }

        IReadOnlyList<string> ignore = _holder.Current.IgnorePatterns;
        var files = new List<string>();
        var ignored = new List<string>();
        CollectLocalFiles(request.LocalPath, request.LocalPath, ignore, files, ignored, cancellationToken);

        long totalBytes = 0;
        foreach (string file in files)
        {
            try
            {
                totalBytes += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // Collisions: remote names already present at the import target root.
        RepositoryEntity? repo = await FindRepoAsync(accountId, request.RepositoryId, cancellationToken).ConfigureAwait(false);
        var collisions = new List<string>();
        var topLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(request.LocalPath, file);
            string top = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            _ = topLevel.Add(top);
        }

        if (repo is not null)
        {
            var existing = await resolved.Value.Provider.ListChildrenAsync(repo.ProviderFolderId, null, 1000, cancellationToken).ConfigureAwait(false);
            if (existing.IsSuccess && existing.Value is not null)
            {
                var remoteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in existing.Value.Items)
                {
                    if (!item.Trashed)
                    {
                        _ = remoteNames.Add(item.Name);
                    }
                }

                foreach (string top in topLevel)
                {
                    if (remoteNames.Contains(top))
                    {
                        collisions.Add(top);
                    }
                }
            }
        }

        var relativePaths = new List<string>();
        var excerpts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(request.LocalPath, file).Replace(Path.DirectorySeparatorChar, '/');
            relativePaths.Add(relative);
            string name = Path.GetFileName(file);
            if (IsMarkerCandidate(name))
            {
                try
                {
                    string text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    excerpts[relative] = text.Length > 32768 ? text[..32768] : text;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        var detection = new ProjectDetector(_holder.Current.ProjectMarkers).Detect(relativePaths, excerpts);
        var detected = new List<string>();
        foreach (var project in detection.Projects)
        {
            detected.Add(project.DetectorId + "@" + (string.IsNullOrEmpty(project.RelativePath) ? "/" : project.RelativePath));
        }

        var sanitized = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            {
                sanitized.Add(Path.GetRelativePath(request.LocalPath, file));
            }
        }

        int folders = CountLocalFolders(request.LocalPath, ignore);
        return Result.Ok(new ImportPlanDto(files.Count, folders, totalBytes, ignored.Count, collisions, detected, sanitized));
    }

    public Task<Result<string>> ExecuteImportAsync(ImportExecuteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string id = "import-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12];
        var operation = new ImportOperation(id, request.RepositoryId, request.LocalPath, "queued", 0, 0, null);
        _imports[id] = operation;
        _ = Task.Run(() => RunImportAsync(operation), CancellationToken.None);
        _ = cancellationToken;
        return Task.FromResult(Result.Ok(id));
    }

    public Task<Result<ImportStatusDto>> GetImportStatusAsync(string operationId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!_imports.TryGetValue(operationId, out ImportOperation? operation))
        {
            return Task.FromResult(Result.Fail<ImportStatusDto>(BackendResults.NotFound("Import '" + operationId + "' was not found.")));
        }

        return Task.FromResult(Result.Ok(new ImportStatusDto(operation.State, operation.Done, operation.Total, operation.Error)));
    }

    private async Task RunImportAsync(ImportOperation operation)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        operation.Cancel = cts;
        try
        {
            operation.State = "running";
            string accountId = await ActiveAccountAsync(cts.Token).ConfigureAwait(false);
            RepositoryEntity? repo = await FindRepoAsync(accountId, operation.RepositoryId, cts.Token).ConfigureAwait(false);
            if (repo is null)
            {
                operation.State = "failed";
                operation.Error = "Repository was not found.";
                return;
            }

            Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cts.Token).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                operation.State = "failed";
                operation.Error = resolved.Failure?.Message ?? "No provider.";
                return;
            }

            IReadOnlyList<string> ignore = _holder.Current.IgnorePatterns;
            var files = new List<string>();
            var ignored = new List<string>();
            CollectLocalFiles(operation.LocalPath, operation.LocalPath, ignore, files, ignored, cts.Token);
            operation.Total = files.Count;

            var folders = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                cts.Token.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(operation.LocalPath, file);
                string? dir = Path.GetDirectoryName(relative);
                string parentId = repo.ProviderFolderId;
                if (!string.IsNullOrEmpty(dir))
                {
                    string key = dir.Replace(Path.DirectorySeparatorChar, '/');
                    if (!folders.TryGetValue(key, out string? folderId))
                    {
                        folderId = await MirrorRemotePathAsync(accountId, resolved.Value.Provider, repo.ProviderFolderId, key, cts.Token).ConfigureAwait(false);
                        if (folderId is null)
                        {
                            operation.State = "failed";
                            operation.Error = "Could not create folder '" + key + "'.";
                            return;
                        }

                        folders[key] = folderId;
                    }

                    parentId = folderId;
                }

                string name = Path.GetFileName(file);
                byte[] bytes;
                try
                {
                    bytes = await File.ReadAllBytesAsync(file, cts.Token).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    operation.State = "failed";
                    operation.Error = ex.Message;
                    return;
                }
                catch (UnauthorizedAccessException ex)
                {
                    operation.State = "failed";
                    operation.Error = ex.Message;
                    return;
                }

                // Analyze from the local source before upload (no re-download).
                string opId = "op-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
                string staging = StageContent(bytes, opId);
                Result<string> queued = await EnqueueOutboxAsync(accountId, OutboxTypes.Upload, new
                {
                    parentId,
                    name,
                    mime = MimeGuess(name),
                    stagingPath = staging,
                    opId,
                }, cts.Token).ConfigureAwait(false);
                if (!queued.IsSuccess)
                {
                    operation.State = "failed";
                    operation.Error = queued.Failure?.Message;
                    return;
                }

                operation.Done++;
            }

            // Replay everything, then detect projects over the new tree.
            var replayed = await _sync.ForAccount(accountId).SyncNowAsync(null, cts.Token).ConfigureAwait(false);
            if (!replayed.IsSuccess)
            {
                operation.State = "failed";
                operation.Error = replayed.Failure?.Message;
                return;
            }

            await DetectAndTagTreeAsync(accountId, resolved.Value, repo, cts.Token).ConfigureAwait(false);
            await JournalAsync(accountId, "import.execute", repo.ProviderFolderId, "Imported " + operation.Done + " files.", false, null, null, cts.Token).ConfigureAwait(false);
            operation.State = "done";
        }
        catch (OperationCanceledException)
        {
            operation.State = "cancelled";
        }
        catch (Exception ex)
        {
            operation.State = "failed";
            operation.Error = ex.Message;
        }
    }

    private async Task<ProjectEntity?> FindProjectAsync(string accountId, string projectId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ProjectEntity? row = await db.Projects.FindAsync(new object[] { projectId }, cancellationToken).ConfigureAwait(false);
        if (row is null || !string.Equals(row.AccountId, accountId, StringComparison.Ordinal))
        {
            return null;
        }

        return row;
    }

    private async Task<IReadOnlyList<string>> ProjectTagsAsync(string accountId, string repoFolderId, string projectId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ProjectEntity? row = await db.Projects.FindAsync(new object[] { projectId }, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Array.Empty<string>();
        }

        // Tags live on the project folder node; resolve it by recorded path first.
        string? folderId = await FindNodeIdByPathAsync(accountId, row.Path, cancellationToken).ConfigureAwait(false);
        if (folderId is null)
        {
            return Array.Empty<string>();
        }

        List<NodeTagEntity> assigns = await db.NodeTags.AsNoTracking().Where(t => t.NodeId == folderId).ToListAsync(cancellationToken).ConfigureAwait(false);
        _ = repoFolderId;
        var names = new List<string>();
        foreach (NodeTagEntity assign in assigns)
        {
            TagEntity? tag = await db.Tags.FindAsync(new object[] { assign.TagId }, cancellationToken).ConfigureAwait(false);
            if (tag is not null && !names.Contains(tag.Name))
            {
                names.Add(tag.Name);
            }
        }

        return names;
    }

    private static void CollectLocalFiles(string root, string current, IReadOnlyList<string> ignore, List<string> files, List<string> ignored, CancellationToken cancellationToken)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(current);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (string entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                ignored.Add(entry);
                continue;
            }

            string relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
            string name = Path.GetFileName(entry);
            if (IsIgnored(ignore, name, relative))
            {
                ignored.Add(entry);
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                CollectLocalFiles(root, entry, ignore, files, ignored, cancellationToken);
            }
            else
            {
                files.Add(entry);
            }
        }
    }

    private static int CountLocalFolders(string root, IReadOnlyList<string> ignore)
    {
        int count = 0;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateDirectories(current);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string dir in entries)
            {
                string relative = Path.GetRelativePath(root, dir).Replace(Path.DirectorySeparatorChar, '/');
                if (IsIgnored(ignore, Path.GetFileName(dir), relative))
                {
                    continue;
                }

                count++;
                stack.Push(dir);
            }
        }

        return count;
    }

    private static bool IsIgnored(IReadOnlyList<string> ignore, string fileName, string relativePath)
    {
        foreach (string pattern in ignore)
        {
            try
            {
                if (GlobMatcher.MatchFileOrPath(pattern, fileName, relativePath))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                continue;
            }
        }

        return false;
    }

    private static bool IsMarkerCandidate(string fileName)
    {
        return string.Equals(fileName, "package.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".config.js", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".config.ts", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
    }

    private static string MimeGuess(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" or ".md" or ".log" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
    }

    private sealed class ImportOperation
    {
        public ImportOperation(string id, string repositoryId, string localPath, string state, int done, int total, string? error)
        {
            Id = id;
            RepositoryId = repositoryId;
            LocalPath = localPath;
            State = state;
            Done = done;
            Total = total;
            Error = error;
        }

        public string Id { get; }

        public string RepositoryId { get; }

        public string LocalPath { get; }

        public string State { get; set; }

        public int Done { get; set; }

        public int Total { get; set; }

        public string? Error { get; set; }

        public CancellationTokenSource? Cancel { get; set; }
    }
}
