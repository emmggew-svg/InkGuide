using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Persistence.Sync;
using InkGuide.Infrastructure.Storage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using InkGuide.App.Hosting;
namespace InkGuide.App.Bridge.Backend;

public sealed partial class InkGuideBridgeBackend
{
    public async Task<Result<SyncStatusDto>> GetSyncStatusAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var status = await _sync.ForAccount(accountId).GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsSuccess || status.Value is null)
        {
            return Result.Fail<SyncStatusDto>(BackendResults.ToBridge(status.Failure!));
        }

        return Result.Ok(new SyncStatusDto(status.Value.State, status.Value.LastError, status.Value.LastSyncAt));
    }

    public async Task<Result<SyncStatusDto>> SyncNowAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var progress = new SyncProgressReporter(this);
        var status = await _sync.ForAccount(accountId).SyncNowAsync(progress, cancellationToken).ConfigureAwait(false);
        if (!status.IsSuccess || status.Value is null)
        {
            return Result.Fail<SyncStatusDto>(BackendResults.ToBridge(status.Failure!));
        }

        await BackfillRepoIdsAsync(accountId, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new SyncStatusDto(status.Value.State, status.Value.LastError, status.Value.LastSyncAt));
    }

    public async Task<Result<EmptyResult>> PauseSyncAsync(CancellationToken cancellationToken)
    {
        await _settings.SetAsync(BackendSettingsService.Keys.SyncPaused, "true", cancellationToken).ConfigureAwait(false);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await _host.EmitEventAsync("sync.stateChanged", accountId + ":paused").ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> ResumeSyncAsync(CancellationToken cancellationToken)
    {
        await _settings.SetAsync(BackendSettingsService.Keys.SyncPaused, "false", cancellationToken).ConfigureAwait(false);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        await _host.EmitEventAsync("sync.stateChanged", accountId + ":idle").ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<IReadOnlyList<OutboxDto>>> ListOutboxAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var list = await _outbox.ListAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess || list.Value is null)
        {
            return Result.Fail<IReadOnlyList<OutboxDto>>(BackendResults.ToBridge(list.Failure!));
        }

        var dtos = new List<OutboxDto>();
        foreach (OutboxOperationEntity op in list.Value)
        {
            dtos.Add(new OutboxDto(op.OpId, op.OperationType, op.State, op.Attempts, op.LastError, op.CreatedAtUtc.ToString("o")));
        }

        return Result.Ok<IReadOnlyList<OutboxDto>>(dtos);
    }

    public async Task<Result<EmptyResult>> RetryOutboxAsync(string opId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opId);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var marked = await _outbox.MarkFailedAsync(opId, string.Empty, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (!marked.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(marked.Failure!));
        }

        // Immediate replay attempt; the background worker covers the rest.
        var replayed = await _engine.ReplayOutboxAsync(_sync.ForAccount(accountId), _sync.ForAccount(accountId), cancellationToken).ConfigureAwait(false);
        if (!replayed.IsSuccess)
        {
            AppLogMessages.BackendNote(_logger, "Outbox retry replay reported: " + replayed.Failure?.Message);
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<EmptyResult>> DiscardOutboxAsync(string opId, string? confirmationToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opId);
        _ = confirmationToken;
        var discarded = await _outbox.DiscardAsync(opId, cancellationToken).ConfigureAwait(false);
        if (!discarded.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(discarded.Failure!));
        }

        return Result.Ok(new EmptyResult());
    }

    public async Task<Result<IReadOnlyList<ConflictDto>>> ListConflictsAsync(CancellationToken cancellationToken)
    {
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var conflicts = await _misc.ListOpenConflictsAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!conflicts.IsSuccess || conflicts.Value is null)
        {
            return Result.Fail<IReadOnlyList<ConflictDto>>(BackendResults.ToBridge(conflicts.Failure!));
        }

        var list = new List<ConflictDto>();
        foreach (ConflictRecordEntity record in conflicts.Value)
        {
            list.Add(ToConflictDto(record));
        }

        return Result.Ok<IReadOnlyList<ConflictDto>>(list);
    }

    public async Task<Result<ConflictDto>> GetConflictAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var conflicts = await _misc.ListOpenConflictsAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!conflicts.IsSuccess || conflicts.Value is null)
        {
            return Result.Fail<ConflictDto>(BackendResults.ToBridge(conflicts.Failure!));
        }

        ConflictRecordEntity? record = conflicts.Value.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        if (record is null)
        {
            return Result.Fail<ConflictDto>(BackendResults.NotFound("Conflict '" + id + "' was not found."));
        }

        return Result.Ok(ToConflictDto(record));
    }

    public async Task<Result<EmptyResult>> ResolveConflictAsync(string id, ResolveConflictRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(request);
        string accountId = await ActiveAccountAsync(cancellationToken).ConfigureAwait(false);
        var conflicts = await _misc.ListOpenConflictsAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (!conflicts.IsSuccess || conflicts.Value is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(conflicts.Failure!));
        }

        ConflictRecordEntity? record = conflicts.Value.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        if (record is null)
        {
            return Result.Fail<EmptyResult>(BackendResults.NotFound("Conflict '" + id + "' was not found."));
        }

        string resolution = (request.Resolution ?? string.Empty).ToLowerInvariant();
        if (resolution == "keeplocal")
        {
            string text = request.MergedText ?? record.LocalText ?? string.Empty;
            Result<ResolvedProvider> resolved = await ResolveProviderAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (!resolved.IsSuccess || resolved.Value is null)
            {
                return Result.Fail<EmptyResult>(resolved.Failure!);
            }

            var remote = await resolved.Value.Provider.GetAsync(record.NodeId, cancellationToken).ConfigureAwait(false);
            if (!remote.IsSuccess || remote.Value is null)
            {
                return Result.Fail<EmptyResult>(BackendResults.ToBridge(remote.Failure!));
            }

            await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text), writable: false);
            var updated = await resolved.Value.Provider.UpdateContentAsync(record.NodeId, stream, remote.Value.Version, null, null, cancellationToken).ConfigureAwait(false);
            if (!updated.IsSuccess || updated.Value is null)
            {
                return Result.Fail<EmptyResult>(BackendResults.ToBridge(updated.Failure!));
            }

            await MirrorNodeAsync(accountId, updated.Value, cancellationToken).ConfigureAwait(false);
        }
        else if (resolution != "keepremote" && resolution != "keepboth")
        {
            return Result.Fail<EmptyResult>(BackendResults.InvalidParams("Resolution must be keepLocal, keepRemote or keepBoth."));
        }

        // keepRemote drops the local text; keepBoth keeps the conflict copy file as-is.
        var done = await _misc.ResolveConflictAsync(id, request.Resolution ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (!done.IsSuccess)
        {
            return Result.Fail<EmptyResult>(BackendResults.ToBridge(done.Failure!));
        }

        await JournalAsync(accountId, "conflict.resolve", record.NodeId, "Conflict resolved (" + request.Resolution + ").", false, null, null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(new EmptyResult());
    }

    private static ConflictDto ToConflictDto(ConflictRecordEntity record)
    {
        return new ConflictDto(
            record.Id, record.NodeId, record.Kind, record.ConflictCopyPath,
            record.BaseVersion, record.RemoteVersion, record.Resolution,
            record.CreatedAtUtc.ToString("o"));
    }

    private sealed class SyncProgressReporter : IProgress<SyncProgress>
    {
        private readonly InkGuideBridgeBackend _backend;

        public SyncProgressReporter(InkGuideBridgeBackend backend)
        {
            _backend = backend;
        }

        public void Report(SyncProgress value)
        {
            _ = _backend._host.EmitEventAsync("sync.progress", value.Indexed + "/" + value.Changed);
        }
    }
}
