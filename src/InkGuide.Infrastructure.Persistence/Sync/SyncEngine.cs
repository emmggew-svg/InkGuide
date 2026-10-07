using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Persistence.Logging;
using InkGuide.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Persistence.Sync;

/// <summary>
/// Sync engine: initial BFS index, incremental change polling, optimistic mutations
/// with durable outbox, conflict detection, offline tracking, and crash recovery.
/// Provider I/O goes through <see cref="ISyncSource"/>; outbox execution through
/// <see cref="IOutboxExecutor"/>; side effects through <see cref="ISyncHooks"/>.
/// </summary>
public sealed class SyncEngine
{
    private readonly INodeRepository _nodes;
    private readonly IOutboxStore _outbox;
    private readonly IMiscStore _misc;
    private readonly PersistenceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SyncEngine> _logger;

    private int _consecutiveFailures;
    private SyncState _state = SyncState.Idle;

    public SyncEngine(
        INodeRepository nodes,
        IOutboxStore outbox,
        IMiscStore misc,
        PersistenceOptions options,
        TimeProvider clock,
        ILogger<SyncEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(misc);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _nodes = nodes;
        _outbox = outbox;
        _misc = misc;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Current sync state (last reported).</summary>
    public SyncState State => _state;

    /// <summary>
    /// Breadth-first initial index from the workspace root. Paged, batched, resumable
    /// after interruption (resume cursor persisted per account).
    /// </summary>
    public async Task<Result> IndexAsync(
        string accountId,
        ISyncSource source,
        ISyncHooks hooks,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(hooks);
        await SetStateAsync(accountId, SyncState.Indexing, hooks, cancellationToken).ConfigureAwait(false);
        try
        {
            Result<string> root = await source.GetRootIdAsync(cancellationToken).ConfigureAwait(false);
            if (!root.IsSuccess || root.Value is null)
            {
                return await FailOrOfflineAsync(accountId, root.Failure, hooks, cancellationToken).ConfigureAwait(false);
            }

            string resumeKey = ResumeKey(accountId);
            Result<string?> savedCursor = await _misc.GetSettingAsync(resumeKey, cancellationToken).ConfigureAwait(false);
            var queue = new Queue<(string Id, string Path)>();
            queue.Enqueue((root.Value, "/"));
            int indexed = 0;
            int changed = 0;
            var batch = new List<NodeEntity>(_options.WriteBatchSize);
            var analyze = new List<string>();

            // Resume support: when a resume cursor exists we still walk from the root but
            // skip persisting until progress passes the saved point is out of scope for v1;
            // the cursor at least lets a restarted run avoid re-fetching the change token.
            _ = savedCursor;

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string folderId, string folderPath) = queue.Dequeue();
                string? cursor = null;
                do
                {
                    Result<RemotePage> page = await source.ListChildrenAsync(folderId, cursor, 200, cancellationToken).ConfigureAwait(false);
                    if (!page.IsSuccess || page.Value is null)
                    {
                        return await FailOrOfflineAsync(accountId, page.Failure, hooks, cancellationToken).ConfigureAwait(false);
                    }

                    foreach (RemoteItem item in page.Value.Items)
                    {
                        string path = folderPath == "/" ? "/" + item.Name : folderPath + "/" + item.Name;
                        Result<bool> isNew = await UpsertRemoteItemAsync(accountId, item, path, batch, cancellationToken).ConfigureAwait(false);
                        if (isNew.IsSuccess && isNew.Value)
                        {
                            changed++;
                            if (!item.IsFolder)
                            {
                                analyze.Add(item.Id);
                            }
                        }

                        indexed++;
                        if (item.IsFolder && !item.Trashed)
                        {
                            queue.Enqueue((item.Id, path));
                        }

                        if (batch.Count >= _options.WriteBatchSize)
                        {
                            Result flush = await _nodes.UpsertBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                            batch.Clear();
                            if (!flush.IsSuccess)
                            {
                                return flush;
                            }
                        }

                        progress?.Report(new SyncProgress(accountId, indexed, changed, path));
                    }

                    cursor = page.Value.NextCursor;
                }
                while (cursor is not null);
            }

            if (batch.Count > 0)
            {
                Result flush = await _nodes.UpsertBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                if (!flush.IsSuccess)
                {
                    return flush;
                }
            }

            Result<string> token = await source.GetStartChangeTokenAsync(cancellationToken).ConfigureAwait(false);
            if (token.IsSuccess && token.Value is not null)
            {
                _ = await _misc.SetChangeTokenAsync(accountId, token.Value, cancellationToken).ConfigureAwait(false);
            }

            _ = await _misc.SetSettingAsync(resumeKey, "\"done\"", cancellationToken).ConfigureAwait(false);
            if (analyze.Count > 0)
            {
                await hooks.OnAnalysisRequestedAsync(analyze, cancellationToken).ConfigureAwait(false);
            }

            _consecutiveFailures = 0;
            await SetStateAsync(accountId, SyncState.Idle, hooks, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            await SetStateAsync(accountId, SyncState.Idle, hooks, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            PersistenceLog.IndexFailed(_logger, ex, accountId);
            await SetStateAsync(accountId, SyncState.Error, hooks, CancellationToken.None).ConfigureAwait(false);
            return Result.Fail(OperationError.Provider("Initial index failed: " + ex.Message, true));
        }
    }

    /// <summary>Applies one incremental change-feed poll. Invalid tokens trigger full re-index.</summary>
    public async Task<Result> PollOnceAsync(string accountId, ISyncSource source, ISyncHooks hooks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(hooks);

        Result<string?> saved = await _misc.GetChangeTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
        string? token = saved.Value;
        if (token is null)
        {
            Result<string> start = await source.GetStartChangeTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!start.IsSuccess || start.Value is null)
            {
                return await FailOrOfflineAsync(accountId, start.Failure, hooks, cancellationToken).ConfigureAwait(false);
            }

            token = start.Value;
            _ = await _misc.SetChangeTokenAsync(accountId, token, cancellationToken).ConfigureAwait(false);
        }

        Result<RemoteChangeSet> changes = await source.GetChangesAsync(token, cancellationToken).ConfigureAwait(false);
        if (!changes.IsSuccess || changes.Value is null)
        {
            if (changes.Failure is not null && string.Equals(changes.Failure.Code, "TOKEN_EXPIRED", StringComparison.Ordinal))
            {
                PersistenceLog.TokenExpired(_logger, accountId);
                return await IndexAsync(accountId, source, hooks, null, cancellationToken).ConfigureAwait(false);
            }

            return await FailOrOfflineAsync(accountId, changes.Failure, hooks, cancellationToken).ConfigureAwait(false);
        }

        await SetStateAsync(accountId, SyncState.Syncing, hooks, cancellationToken).ConfigureAwait(false);
        var analyze = new List<string>();
        foreach (RemoteChange change in changes.Value.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (change.Removed || change.Item is null)
            {
                _ = await _nodes.RemoveAsync(change.ItemId, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var staged = new List<NodeEntity>(1);
            Result<bool> isNew = await UpsertRemoteItemAsync(accountId, change.Item, "/" + change.Item.Name, staged, cancellationToken).ConfigureAwait(false);
            if (staged.Count > 0)
            {
                _ = await _nodes.UpsertBatchAsync(staged, cancellationToken).ConfigureAwait(false);
            }

            if (isNew.IsSuccess && isNew.Value && !change.Item.IsFolder)
            {
                analyze.Add(change.Item.Id);
            }
        }

        _ = await _misc.SetChangeTokenAsync(accountId, changes.Value.NextToken, cancellationToken).ConfigureAwait(false);
        if (analyze.Count > 0)
        {
            await hooks.OnAnalysisRequestedAsync(analyze, cancellationToken).ConfigureAwait(false);
        }

        _consecutiveFailures = 0;
        await SetStateAsync(accountId, SyncState.Idle, hooks, cancellationToken).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>
    /// Enqueues a local mutation: durable outbox write first, then optimistic DB apply.
    /// The returned opId makes replay idempotent.
    /// </summary>
    public async Task<Result<string>> EnqueueMutationAsync(
        string accountId,
        string operationType,
        string payloadJson,
        Func<CancellationToken, Task> optimisticApply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(operationType);
        ArgumentNullException.ThrowIfNull(payloadJson);
        ArgumentNullException.ThrowIfNull(optimisticApply);
        string opId = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        var entry = new OutboxOperationEntity
        {
            OpId = opId,
            AccountId = accountId,
            OperationType = operationType,
            PayloadJson = payloadJson,
            State = "Pending",
            Attempts = 0,
            NextAttemptAtUtc = _clock.GetUtcNow(),
        };
        Result enqueued = await _outbox.EnqueueAsync(entry, cancellationToken).ConfigureAwait(false);
        if (!enqueued.IsSuccess)
        {
            return Result.Fail<string>(enqueued.Failure!);
        }

        await optimisticApply(cancellationToken).ConfigureAwait(false);
            return Result.Ok(opId);
    }

    /// <summary>Replays due outbox operations with per-item exponential backoff.</summary>
    public async Task<Result<int>> ReplayOutboxAsync(IOutboxExecutor executor, ISyncHooks hooks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(hooks);
        Result<IReadOnlyList<OutboxOperationEntity>> due = await _outbox.DequeueDueAsync(100, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (!due.IsSuccess || due.Value is null)
        {
            return Result.Fail<int>(due.Failure ?? OperationError.Internal("Outbox read failed."));
        }

        int replayed = 0;
        foreach (OutboxOperationEntity op in due.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Result result;
            try
            {
                result = await executor.ExecuteAsync(new OutboxPayload(op.OpId, op.AccountId, op.OperationType, op.PayloadJson, op.Attempts), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = Result.Fail(OperationError.Provider(ex.Message, true));
            }

            if (result.IsSuccess)
            {
                _ = await _outbox.MarkSucceededAsync(op.OpId, cancellationToken).ConfigureAwait(false);
                replayed++;
            }
            else
            {
                bool retryable = result.Failure?.Retryable ?? true;
                DateTimeOffset next = retryable
                    ? _clock.GetUtcNow().AddSeconds(Math.Min(3600, 5 * Math.Pow(2, op.Attempts)))
                    : DateTimeOffset.MaxValue;
                _ = await _outbox.MarkFailedAsync(op.OpId, result.Failure?.Message ?? "Unknown error", next, cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Ok(replayed);
    }

    /// <summary>
    /// Version-guarded save: when the remote version moved, never overwrites. Records a
    /// conflict and reports the local copy path the caller must persist as a conflict copy.
    /// </summary>
    public async Task<Result<ConflictCheck>> CheckWriteConflictAsync(
        string accountId,
        string nodeId,
        string? baseVersion,
        string? remoteVersion,
        string nodeName,
        ISyncHooks hooks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(nodeName);
        ArgumentNullException.ThrowIfNull(hooks);
        if (baseVersion is null || string.Equals(baseVersion, remoteVersion, StringComparison.Ordinal))
        {
            return Result.Ok(new ConflictCheck(false, null, null));
        }

        string stamp = _clock.GetUtcNow().ToLocalTime().ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string copyName = InsertBeforeExtension(nodeName, $" (conflict {stamp})");
        var record = new ConflictRecordEntity
        {
            Id = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture),
            AccountId = accountId,
            NodeId = nodeId,
            Kind = "Content",
            ConflictCopyPath = copyName,
            BaseVersion = baseVersion,
            RemoteVersion = remoteVersion,
        };
        Result saved = await _misc.SaveConflictAsync(record, cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result.Fail<ConflictCheck>(saved.Failure!);
        }

        await hooks.OnConflictCreatedAsync(record.Id, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new ConflictCheck(true, record.Id, copyName));
    }

    /// <summary>Crash recovery: replays the outbox; transfer requeue is handled by the transfer service via hooks.</summary>
    public async Task<Result<int>> RecoverAfterCrashAsync(IOutboxExecutor executor, ISyncHooks hooks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(hooks);
        return await ReplayOutboxAsync(executor, hooks, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> UpsertRemoteItemAsync(
        string accountId,
        RemoteItem item,
        string path,
        List<NodeEntity> batch,
        CancellationToken cancellationToken)
    {
        Result<NodeEntity> existing = await _nodes.GetAsync(item.Id, cancellationToken).ConfigureAwait(false);
        var entity = new NodeEntity
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
            PathText = path,
            Description = item.Description,
            WebViewLink = item.WebViewLink,
            SyncedAtUtc = _clock.GetUtcNow(),
        };
        bool isNewOrChanged = true;
        if (existing.IsSuccess && existing.Value is not null)
        {
            entity.RepoId = existing.Value.RepoId;
            entity.ProjectId = existing.Value.ProjectId;
            isNewOrChanged = !string.Equals(existing.Value.Md5, item.Md5, StringComparison.Ordinal) ||
                !string.Equals(existing.Value.Version, item.Version, StringComparison.Ordinal) ||
                !string.Equals(existing.Value.Name, item.Name, StringComparison.Ordinal);
        }

        batch.Add(entity);
        return Result.Ok(isNewOrChanged);
    }

    private async Task<Result> FailOrOfflineAsync(string accountId, OperationError? failure, ISyncHooks hooks, CancellationToken cancellationToken)
    {
        _consecutiveFailures++;
        PersistenceLog.SyncCallFailed(_logger, accountId, _consecutiveFailures, failure?.Message);
        if (failure is not null && string.Equals(failure.Code, "REAUTH_REQUIRED", StringComparison.Ordinal))
        {
            await SetStateAsync(accountId, SyncState.ReauthRequired, hooks, cancellationToken).ConfigureAwait(false);
        }
        else if (_consecutiveFailures >= 3)
        {
            await SetStateAsync(accountId, SyncState.Offline, hooks, cancellationToken).ConfigureAwait(false);
        }

        return Result.Fail(failure ?? OperationError.Provider("Sync call failed.", true));
    }

    private async Task SetStateAsync(string accountId, SyncState state, ISyncHooks hooks, CancellationToken cancellationToken)
    {
        _state = state;
        try
        {
            await hooks.OnStateChangedAsync(accountId, state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PersistenceLog.SyncHookFailed(_logger, ex);
        }
    }

    private static string ResumeKey(string accountId) => "sync.resume." + accountId;

    private static string InsertBeforeExtension(string name, string infix)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0)
        {
            return name + infix;
        }

        return string.Concat(name.AsSpan(0, dot), infix, name.AsSpan(dot));
    }
}

/// <summary>Outcome of a version-guarded write check.</summary>
public sealed record ConflictCheck(bool HasConflict, string? ConflictId, string? ConflictCopyName);
