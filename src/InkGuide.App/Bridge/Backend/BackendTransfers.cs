using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.App.Hosting;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Storage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SCommon = global::InkGuide.Infrastructure.Storage.Common;

namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _transferCts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _dirtyRepos = new(StringComparer.Ordinal);
    private int _transferPumpRunning;

    public async Task<Result<IReadOnlyList<TransferDto>>> UploadPathsAsync(UploadPathsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.LocalPaths is null || request.LocalPaths.Count == 0)
        {
            return Result.Fail<IReadOnlyList<TransferDto>>(BackendResults.InvalidParams("LocalPaths is empty."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return Result.Fail<IReadOnlyList<TransferDto>>(resolved.Failure!);
        }

        string parentId = request.ParentId;
        if (string.IsNullOrEmpty(parentId))
        {
            string? inbox = await InboxFolderIdAsync(accountId, resolved.Value, cancellationToken).ConfigureAwait(false);
            if (inbox is null)
            {
                return Result.Fail<IReadOnlyList<TransferDto>>(BackendResults.Internal("Inbox folder is unavailable."));
            }

            parentId = inbox;
        }

        var jobs = new List<TransferDto>();
        foreach (string local in request.LocalPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string full;
            try
            {
                full = Path.GetFullPath(local);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                continue;
            }

            if (File.Exists(full))
            {
                TransferJobEntity job = await AddTransferJobAsync(accountId, "Upload", null, parentId, Path.GetFileName(full), full, cancellationToken).ConfigureAwait(false);
                jobs.Add(ToTransferDto(job));
            }
            else if (Directory.Exists(full))
            {
                string? repoId = await FindRepoIdByFolderAsync(accountId, parentId, cancellationToken).ConfigureAwait(false);
                if (repoId is not null)
                {
                    _dirtyRepos[repoId] = 0;
                }

                // Preserve the top-level folder itself, then mirror every level.
                string topName = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string? topId = await MirrorRemotePathAsync(accountId, resolved.Value.Provider, parentId, topName, cancellationToken).ConfigureAwait(false);
                if (topId is null)
                {
                    continue;
                }

                foreach (string file in EnumerateUploadableFiles(full))
                {
                    string relative = Path.GetRelativePath(full, file);
                    string? dir = Path.GetDirectoryName(relative);
                    string remoteParent = string.IsNullOrEmpty(dir)
                        ? topId
                        : await MirrorRemotePathAsync(accountId, resolved.Value.Provider, topId, dir.Replace(Path.DirectorySeparatorChar, '/'), cancellationToken).ConfigureAwait(false)
                            ?? topId;
                    TransferJobEntity job = await AddTransferJobAsync(accountId, "Upload", null, remoteParent, Path.GetFileName(file), file, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        FileInfo info = new(file);
                        job.TotalBytes = info.Length;
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    jobs.Add(ToTransferDto(job));
                }
            }
        }

        KickTransferPump();
        return Result.Ok<IReadOnlyList<TransferDto>>(jobs);
    }

    public async Task<Result<TransferDto>> DownloadAsync(string nodeId, DownloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return Result.Fail<TransferDto>(BackendResults.InvalidParams("DestinationPath is empty."));
        }

        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? row = await FindNodeAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<TransferDto>(BackendResults.NotFound("Node '" + nodeId + "' was not found."));
        }

        TransferJobEntity job = await AddTransferJobAsync(accountId, "Download", nodeId, row.ParentId, row.Name, request.DestinationPath, cancellationToken).ConfigureAwait(false);
        KickTransferPump();
        return Result.Ok(ToTransferDto(job));
    }

    public async Task<Result<IReadOnlyList<TransferDto>>> ListTransfersAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot ORDER BY DateTimeOffset: sort the capped set in memory.
        List<TransferJobEntity> rows = (await db.TransferJobs.AsNoTracking().Where(t => t.AccountId == accountId).ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(200)
            .ToList();
        var list = new List<TransferDto>();
        foreach (TransferJobEntity row in rows)
        {
            list.Add(ToTransferDto(row));
        }

        return Result.Ok<IReadOnlyList<TransferDto>>(list);
    }

    public async Task<Result<EmptyResult>> PauseTransferAsync(string id, CancellationToken cancellationToken)
    {
        TransferJobEntity? row = await SetTransferStateAsync(id, "Queued", "Paused", cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Transfer '" + id + "' was not found."));
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> ResumeTransferAsync(string id, CancellationToken cancellationToken)
    {
        TransferJobEntity? row = await SetTransferStateAsync(id, "Paused", "Queued", cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = await SetTransferStateAsync(id, "Failed", "Queued", cancellationToken).ConfigureAwait(false);
        }

        if (row is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Transfer '" + id + "' was not found."));
        }

        KickTransferPump();
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> CancelTransferAsync(string id, CancellationToken cancellationToken)
    {
        if (_transferCts.TryGetValue(id, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        TransferJobEntity? row = await SetTransferStateAsync(id, null, "Cancelled", cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Transfer '" + id + "' was not found."));
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> RetryTransferAsync(string id, CancellationToken cancellationToken)
    {
        TransferJobEntity? row = await SetTransferStateAsync(id, "Failed", "Queued", cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = await SetTransferStateAsync(id, "Cancelled", "Queued", cancellationToken).ConfigureAwait(false);
        }

        if (row is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Transfer '" + id + "' was not found."));
        }

        KickTransferPump();
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> ClearFinishedAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            List<TransferJobEntity> rows = await db.TransferJobs
                .Where(t => t.AccountId == accountId && (t.State == "Done" || t.State == "Failed" || t.State == "Cancelled"))
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (TransferJobEntity row in rows)
            {
                db.TransferJobs.Remove(row);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    private async Task<string?> InboxFolderIdAsync(string accountId, ResolvedProvider resolved, CancellationToken cancellationToken)
    {
        Result<WorkspaceDto> workspace = await EnsureWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        if (!workspace.IsSuccess || workspace.Value is null)
        {
            return null;
        }

        StorageItem? inbox = await EnsureFolderAsync(resolved.Provider, workspace.Value.ProviderFolderId, InboxFolderName, cancellationToken).ConfigureAwait(false);
        _ = accountId;
        return inbox?.Id;
    }

    private async Task<TransferJobEntity> AddTransferJobAsync(
        string accountId, string direction, string? nodeId, string? parentId, string fileName, string localPath, CancellationToken cancellationToken)
    {
        var job = new TransferJobEntity
        {
            Id = "tr-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..12],
            AccountId = accountId,
            Direction = direction,
            NodeId = nodeId,
            ParentId = parentId,
            FileName = fileName,
            LocalPath = localPath,
            TotalBytes = 0,
            TransferredBytes = 0,
            State = "Queued",
            CreatedAtUtc = _clock.GetUtcNow(),
            UpdatedAtUtc = _clock.GetUtcNow(),
        };
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.TransferJobs.Add(job);
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return job;
    }

    private async Task<TransferJobEntity?> SetTransferStateAsync(string id, string? fromState, string toState, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        TransferJobEntity? row = null;
        await _gate.ExecuteWriteAsync(async ct =>
        {
            row = await db.TransferJobs.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (row is not null && (fromState is null || string.Equals(row.State, fromState, StringComparison.Ordinal)))
            {
                row.State = toState;
                row.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            else
            {
                row = null;
            }
        }, cancellationToken).ConfigureAwait(false);
        return row;
    }

    private static TransferDto ToTransferDto(TransferJobEntity row)
    {
        return new TransferDto(row.Id, row.Direction, row.NodeId, row.FileName, row.TotalBytes, row.TransferredBytes, row.State, row.LastError);
    }

    private static IEnumerable<string> EnumerateUploadableFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string entry in entries)
            {
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
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    stack.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private void KickTransferPump()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _transferPumpRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(PumpTransfersAsync);
    }

    private async Task PumpTransfersAsync()
    {
        try
        {
            while (true)
            {
                TransferJobEntity? next = await TakeQueuedTransferAsync(CancellationToken.None).ConfigureAwait(false);
                if (next is null)
                {
                    return;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                _transferCts[next.Id] = cts;
                try
                {
                    await RunTransferAsync(next, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await SetTransferStateAsync(next.Id, null, "Cancelled", CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await FailTransferAsync(next.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _transferCts.TryRemove(next.Id, out _);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogMessages.BackendNote(_logger, "Transfer pump failed: " + ex.Message);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _transferPumpRunning, 0);
            // A job may have been queued while the last item was finishing.
            if (await HasQueuedTransfersAsync().ConfigureAwait(false))
            {
                KickTransferPump();
            }
            else
            {
                await ScanDirtyReposAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ScanDirtyReposAsync()
    {
        if (_dirtyRepos.IsEmpty)
        {
            return;
        }

        List<string> repoIds = _dirtyRepos.Keys.ToList();
        _dirtyRepos.Clear();
        string accountId = await ActiveAccountAsync(CancellationToken.None).ConfigureAwait(false);
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, CancellationToken.None).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return;
        }

        foreach (string repoId in repoIds)
        {
            RepositoryEntity? repo = await FindRepoAsync(accountId, repoId, CancellationToken.None).ConfigureAwait(false);
            if (repo is null)
            {
                continue;
            }

            try
            {
                _ = await DetectAndTagTreeAsync(accountId, resolved.Value, repo, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                AppLogMessages.BackendNote(_logger, "Post-upload project scan failed: " + ex.Message);
            }
        }
    }

    private async Task<TransferJobEntity?> TakeQueuedTransferAsync(CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        TransferJobEntity? row = null;
        await _gate.ExecuteWriteAsync(async ct =>
        {
            // SQLite cannot ORDER BY DateTimeOffset: fetch queued, pick oldest in memory.
            List<TransferJobEntity> queued = await db.TransferJobs
                .Where(t => t.State == "Queued")
                .ToListAsync(ct).ConfigureAwait(false);
            row = queued.OrderBy(t => t.CreatedAtUtc).FirstOrDefault();
            if (row is not null)
            {
                row.State = "Running";
                row.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
        return row;
    }

    private async Task<bool> HasQueuedTransfersAsync()
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        return await db.TransferJobs.AsNoTracking().AnyAsync(t => t.State == "Queued").ConfigureAwait(false);
    }

    private async Task RunTransferAsync(TransferJobEntity job, CancellationToken cancellationToken)
    {
        Result<ResolvedProvider> resolved = await ResolveProviderAsync(job.AccountId, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            await FailTransferAsync(job.Id, resolved.Failure?.Message ?? "No provider.", CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (string.Equals(job.Direction, "Upload", StringComparison.Ordinal))
        {
            await RunUploadAsync(job, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunDownloadAsync(job, resolved.Value.Provider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunUploadAsync(TransferJobEntity job, IStorageProvider provider, CancellationToken cancellationToken)
    {
        if (job.LocalPath is null || !File.Exists(job.LocalPath))
        {
            await FailTransferAsync(job.Id, "Local file is missing: " + (job.LocalPath ?? "?"), CancellationToken.None).ConfigureAwait(false);
            return;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(job.LocalPath);
        }
        catch (IOException ex)
        {
            await FailTransferAsync(job.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            await FailTransferAsync(job.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        await UpdateTransferAsync(job.Id, info.Length, 0, "Running", null, CancellationToken.None).ConfigureAwait(false);
        string opId = "op-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        await using FileStream stream = new(job.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var progress = new TransferProgress(job.Id, info.Length, this);
        SCommon.Result<StorageItem> uploaded;
        try
        {
            uploaded = await provider.UploadAsync(job.ParentId ?? string.Empty, job.FileName, stream, MimeGuess(job.FileName), opId, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            await FailTransferAsync(job.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (!uploaded.IsSuccess || uploaded.Value is null)
        {
            await FailTransferAsync(job.Id, uploaded.Failure?.Message ?? "Upload failed.", CancellationToken.None).ConfigureAwait(false);
            return;
        }

        NodeEntity row = await MirrorNodeAsync(job.AccountId, uploaded.Value, CancellationToken.None).ConfigureAwait(false);
        await UpdateTransferAsync(job.Id, info.Length, info.Length, "Done", null, CancellationToken.None).ConfigureAwait(false);
        await SetTransferNodeAsync(job.Id, row.Id, CancellationToken.None).ConfigureAwait(false);
        Result<ResolvedProvider> again = await ResolveProviderAsync(job.AccountId, CancellationToken.None).ConfigureAwait(false);
        if (again.IsSuccess && again.Value is not null)
        {
            await TriggerAnalysisAsync(job.AccountId, again.Value, row.Id, CancellationToken.None).ConfigureAwait(false);
        }

        await _host.EmitEventAsync("transfer.progress", job.Id + ":done").ConfigureAwait(false);
    }

    private async Task RunDownloadAsync(TransferJobEntity job, IStorageProvider provider, CancellationToken cancellationToken)
    {
        if (job.NodeId is null || job.LocalPath is null)
        {
            await FailTransferAsync(job.Id, "Download job is missing its node or destination.", CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var remote = await provider.GetAsync(job.NodeId, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            await FailTransferAsync(job.Id, remote.Failure?.Message ?? "Node was not found.", CancellationToken.None).ConfigureAwait(false);
            return;
        }

        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(job.LocalPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch (IOException ex)
        {
            await FailTransferAsync(job.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            await FailTransferAsync(job.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        await UpdateTransferAsync(job.Id, remote.Value.Size, 0, "Running", null, CancellationToken.None).ConfigureAwait(false);
        try
        {
            await using FileStream file = new(job.LocalPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var downloaded = await provider.DownloadAsync(job.NodeId, file, null, cancellationToken).ConfigureAwait(false);
            if (!downloaded.IsSuccess)
            {
                await FailTransferAsync(job.Id, downloaded.Failure?.Message ?? "Download failed.", CancellationToken.None).ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            await FailTransferAsync(job.Id, ex.Message, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        await UpdateTransferAsync(job.Id, remote.Value.Size, remote.Value.Size, "Done", null, CancellationToken.None).ConfigureAwait(false);
        await _host.EmitEventAsync("transfer.progress", job.Id + ":done").ConfigureAwait(false);
    }

    private async Task UpdateTransferAsync(string id, long total, long transferred, string state, string? error, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            TransferJobEntity? row = await db.TransferJobs.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (row is not null)
            {
                row.TotalBytes = total;
                row.TransferredBytes = transferred;
                row.State = state;
                row.LastError = error;
                row.UpdatedAtUtc = _clock.GetUtcNow();
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetTransferNodeAsync(string id, string nodeId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            TransferJobEntity? row = await db.TransferJobs.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (row is not null)
            {
                row.NodeId = nodeId;
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task FailTransferAsync(string id, string error, CancellationToken cancellationToken)
    {
        await UpdateTransferAsync(id, 0, 0, "Failed", error, cancellationToken).ConfigureAwait(false);
    }

    private sealed class TransferProgress : IProgress<long>
    {
        private readonly string _jobId;
        private readonly long _total;
        private readonly InkGuideBridgeBackend _backend;
        private long _lastReported;

        public TransferProgress(string jobId, long total, InkGuideBridgeBackend backend)
        {
            _jobId = jobId;
            _total = total;
            _backend = backend;
        }

        public void Report(long value)
        {
            // Persist at most every 1 MB to avoid choking the writer gate.
            if (value - _lastReported < 1024 * 1024 && value < _total)
            {
                return;
            }

            _lastReported = value;
            _ = Task.Run(() => _backend.UpdateTransferAsync(_jobId, _total, value, "Running", null, CancellationToken.None));
        }
    }
}
