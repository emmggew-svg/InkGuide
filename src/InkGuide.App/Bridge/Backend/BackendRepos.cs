using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Analysis.Catalogs;
using InkGuide.Analysis.Projects;
using InkGuide.Analysis.Tags;
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
    private static readonly Dictionary<string, string[]> RepoTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["empty"] = Array.Empty<string>(),
        ["web-site"] = new[] { "assets", "styles", "scripts" },
        ["documents"] = new[] { "scans", "notes" },
    };

    public async Task<Result<IReadOnlyList<RepoDto>>> ListReposAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RepositoryEntity> rows = await db.Repositories.AsNoTracking().Where(r => r.AccountId == accountId).OrderBy(r => r.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<RepoDto>();
        foreach (RepositoryEntity row in rows)
        {
            list.Add(new RepoDto(row.Id, row.Name, row.Kind));
        }

        return Result.Ok<IReadOnlyList<RepoDto>>(list);
    }

    public async Task<Result<RepoDto>> CreateRepoAsync(CreateRepoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Fail<RepoDto>(BackendResults.InvalidParams("Repository name is empty."));
        }

        string kind = string.Equals(request.Kind, "mono", StringComparison.OrdinalIgnoreCase) ? "mono" : "single";
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<WorkspaceDto> workspace = await EnsureWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        if (!workspace.IsSuccess || workspace.Value is null)
        {
            return Result.Fail<RepoDto>(workspace.Failure!);
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<RepoDto>(resolved.Failure!);
        }

        var folder = await resolved.Value.Provider.CreateFolderAsync(workspace.Value.ProviderFolderId, request.Name.Trim(), cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess || folder.Value is null)
        {
            return Result.Fail<RepoDto>(BackendResults.ToBridge(folder.Failure!));
        }

        string repoId = "repo-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12];
        var manifest = new RepoManifest(repoId, folder.Value.Name, kind, UtcNow());
        var saved = await _manifests.SaveRepoAsync(resolved.Value.Provider, ManifestScope(resolved.Value), folder.Value.Id, manifest, cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result.Fail<RepoDto>(BackendResults.ToBridge(saved.Failure!));
        }

        if (request.Template is not null && RepoTemplates.TryGetValue(request.Template, out string[]? skeleton))
        {
            foreach (string dir in skeleton)
            {
                _ = await resolved.Value.Provider.CreateFolderAsync(folder.Value.Id, dir, cancellationToken).ConfigureAwait(false);
            }
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.Repositories.Add(new RepositoryEntity
            {
                Id = repoId,
                AccountId = accountId,
                Name = folder.Value.Name,
                Kind = kind,
                ProviderFolderId = folder.Value.Id,
                ManifestVersion = "1",
                CreatedAtUtc = _clock.GetUtcNow(),
                UpdatedAtUtc = _clock.GetUtcNow(),
            });
            db.Nodes.Add(new NodeEntity
            {
                Id = folder.Value.Id,
                AccountId = accountId,
                ParentId = workspace.Value.ProviderFolderId,
                Name = folder.Value.Name,
                IsFolder = true,
                MimeType = folder.Value.MimeType,
                Size = 0,
                Version = folder.Value.Version,
                ModifiedTimeUtc = folder.Value.ModifiedTimeUtc,
                Trashed = false,
                RepoId = repoId,
                PathText = "/" + folder.Value.Name,
                SyncedAtUtc = _clock.GetUtcNow(),
            });
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "repos.create", repoId, "Repository '" + folder.Value.Name + "' created.", true, null, JsonOf(new { folderId = folder.Value.Id }), cancellationToken).ConfigureAwait(false);
        return Result.Ok(new RepoDto(repoId, folder.Value.Name, kind));
    }

    public async Task<Result<RepoDto>> RenameRepoAsync(string id, RenameRepoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Fail<RepoDto>(BackendResults.InvalidParams("Repository name is empty."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        RepositoryEntity? repo = await FindRepoAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<RepoDto>(BackendResults.NotFound("Repository '" + id + "' was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<RepoDto>(resolved.Failure!);
        }

        var renamed = await resolved.Value.Provider.RenameAsync(repo.ProviderFolderId, request.Name.Trim(), cancellationToken).ConfigureAwait(false);
        if (!renamed.IsSuccess || renamed.Value is null)
        {
            return Result.Fail<RepoDto>(BackendResults.ToBridge(renamed.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            RepositoryEntity? row = await db.Repositories.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (row is not null)
            {
                row.Name = renamed.Value.Name;
                row.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "repos.rename", id, "Repository renamed to '" + renamed.Value.Name + "'.", true, JsonOf(new { oldName = repo.Name }), null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new RepoDto(id, renamed.Value.Name, repo.Kind));
    }

    public async Task<Result<EmptyResult>> DeleteRepoAsync(string id, string? confirmationToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        _ = confirmationToken;
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        RepositoryEntity? repo = await FindRepoAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Repository '" + id + "' was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<EmptyResult>(resolved.Failure!);
        }

        // Reversible by default: trash first; permanent deletion is a separate guarded call.
        var trashed = await resolved.Value.Provider.TrashAsync(repo.ProviderFolderId, cancellationToken).ConfigureAwait(false);
        if (!trashed.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(trashed.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            RepositoryEntity? row = await db.Repositories.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (row is not null)
            {
                db.Repositories.Remove(row);
            }

            List<NodeEntity> nodes = await db.Nodes.Where(n => n.AccountId == accountId && n.RepoId == id).ToListAsync(ct).ConfigureAwait(false);
            foreach (NodeEntity node in nodes)
            {
                node.Trashed = true;
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "repos.delete", id, "Repository '" + repo.Name + "' trashed.", true, null, JsonOf(new { folderId = repo.ProviderFolderId }), cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<RepoDto>> GetRepoAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        RepositoryEntity? repo = await FindRepoAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<RepoDto>(BackendResults.NotFound("Repository '" + id + "' was not found."));
        }

        return Result.Ok(new RepoDto(repo.Id, repo.Name, repo.Kind));
    }

    public async Task<Result<RepoOverviewDto>> GetRepoOverviewAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        RepositoryEntity? repo = await FindRepoAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<RepoOverviewDto>(BackendResults.NotFound("Repository '" + id + "' was not found."));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<NodeEntity> nodes = await db.Nodes.AsNoTracking().Where(n => n.AccountId == accountId && n.RepoId == id && !n.Trashed).ToListAsync(cancellationToken).ConfigureAwait(false);
        var bytesByKind = new Dictionary<string, long>(StringComparer.Ordinal);
        long total = 0;
        int files = 0;
        int folders = 0;
        foreach (NodeEntity node in nodes)
        {
            if (node.IsFolder)
            {
                folders++;
                continue;
            }

            files++;
            total += node.Size;
            string kind = await KindOfAsync(node, cancellationToken).ConfigureAwait(false);
            bytesByKind[kind] = bytesByKind.TryGetValue(kind, out long current) ? current + node.Size : node.Size;
        }

        List<ProjectEntity> projects = await db.Projects.AsNoTracking().Where(p => p.RepositoryId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var dtos = new List<ProjectDto>();
        foreach (ProjectEntity p in projects)
        {
            dtos.Add(new ProjectDto(p.Id, p.RepositoryId, p.Path, p.Name, p.ProjectType, Array.Empty<string>(), p.Detected, p.Confirmed));
        }

        string? readme = null;
        NodeEntity? readmeNode = nodes.FirstOrDefault(n => !n.IsFolder && n.Name.StartsWith("README", StringComparison.OrdinalIgnoreCase));
        if (readmeNode is not null)
        {
            readme = readmeNode.Description;
        }

        return Result.Ok(new RepoOverviewDto(id, total, files, folders, bytesByKind, dtos, readme));
    }

    public async Task<Result<ExportZipResponse>> ExportZipAsync(string id, ExportZipRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return Result.Fail<ExportZipResponse>(BackendResults.InvalidParams("DestinationPath is empty."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        RepositoryEntity? repo = await FindRepoAsync(accountId, id, cancellationToken).ConfigureAwait(false);
        if (repo is null)
        {
            return Result.Fail<ExportZipResponse>(BackendResults.NotFound("Repository '" + id + "' was not found."));
        }

        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<ExportZipResponse>(resolved.Failure!);
        }

        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(request.DestinationPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using FileStream zipFile = new(request.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            using var zip = new ZipArchive(zipFile, ZipArchiveMode.Create, leaveOpen: true);
            var queue = new Queue<(string FolderId, string Prefix)>();
            queue.Enqueue((repo.ProviderFolderId, string.Empty));
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string folderId, string prefix) = queue.Dequeue();
                string? cursor = null;
                do
                {
                    var page = await resolved.Value.Provider.ListChildrenAsync(folderId, cursor, 200, cancellationToken).ConfigureAwait(false);
                    if (!page.IsSuccess || page.Value is null)
                    {
                        return Result.Fail<ExportZipResponse>(BackendResults.ToBridge(page.Failure!));
                    }

                    foreach (var item in page.Value.Items)
                    {
                        if (item.Trashed || item.Name == ".inkguide")
                        {
                            continue;
                        }

                        string entryName = string.IsNullOrEmpty(prefix) ? item.Name : prefix + "/" + item.Name;
                        if (item.IsFolder)
                        {
                            queue.Enqueue((item.Id, entryName));
                        }
                        else
                        {
                            await using var buffer = new MemoryStream();
                            var downloaded = await resolved.Value.Provider.DownloadAsync(item.Id, buffer, null, cancellationToken).ConfigureAwait(false);
                            if (!downloaded.IsSuccess)
                            {
                                return Result.Fail<ExportZipResponse>(BackendResults.ToBridge(downloaded.Failure!));
                            }

                            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                            await using Stream entryStream = entry.Open();
                            buffer.Position = 0;
                            await buffer.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    cursor = page.Value.NextCursor;
                }
                while (cursor is not null);
            }
        }
        catch (IOException ex)
        {
            return Result.Fail<ExportZipResponse>(BackendResults.Internal("Export failed: " + ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<ExportZipResponse>(BackendResults.Internal("Export failed: " + ex.Message));
        }

        return Result.Ok(new ExportZipResponse(request.DestinationPath));
    }

    public async Task<Result<RepoDto>> AdoptExistingFolderAsync(AdoptFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string scope = await _settings.GetAsync(BackendSettingsService.Keys.DriveScopeMode, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(scope, "FullDrive", StringComparison.Ordinal))
        {
            return Result.Fail<RepoDto>(new OperationError("UNSUPPORTED_OPERATION", "Adopting existing folders needs FullDrive scope.", false, null));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<RepoDto>(resolved.Failure!);
        }

        var folder = await resolved.Value.Provider.GetAsync(request.ProviderFolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess || folder.Value is null || !folder.Value.IsFolder)
        {
            return Result.Fail<RepoDto>(BackendResults.NotFound("Drive folder was not found."));
        }

        string repoId = "repo-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12];
        string name = string.IsNullOrWhiteSpace(request.Name) ? folder.Value.Name : request.Name.Trim();
        var saved = await _manifests.SaveRepoAsync(resolved.Value.Provider, ManifestScope(resolved.Value), folder.Value.Id, new RepoManifest(repoId, name, "single", UtcNow()), cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result.Fail<RepoDto>(BackendResults.ToBridge(saved.Failure!));
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.Repositories.Add(new RepositoryEntity
            {
                Id = repoId,
                AccountId = accountId,
                Name = name,
                Kind = "single",
                ProviderFolderId = folder.Value.Id,
                ManifestVersion = "1",
                CreatedAtUtc = _clock.GetUtcNow(),
                UpdatedAtUtc = _clock.GetUtcNow(),
            });
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await JournalAsync(accountId, "repos.adopt", folder.Value.Id, "Existing folder adopted as '" + name + "'.", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new RepoDto(repoId, name, "single"));
    }

    private async Task<RepositoryEntity?> FindRepoAsync(string accountId, string repoId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        RepositoryEntity? row = await db.Repositories.FindAsync(new object[] { repoId }, cancellationToken).ConfigureAwait(false);
        if (row is null || !string.Equals(row.AccountId, accountId, StringComparison.Ordinal))
        {
            return null;
        }

        return row;
    }

    private async Task<string> KindOfAsync(NodeEntity node, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeInsightEntity? insight = await db.NodeInsights.FindAsync(new object[] { node.Id }, cancellationToken).ConfigureAwait(false);
        return insight?.Kind ?? "unknown";
    }
}
