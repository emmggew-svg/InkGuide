using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Application.Outbox;
using InkGuide.Domain.Common;
using InkGuide.Domain.Entities;

namespace InkGuide.UnitTests.Fakes;

/// <summary>In-memory outbox queue for port-level tests.</summary>
public sealed class InMemoryOutbox : IOutbox
{
    private readonly Dictionary<string, OutboxOperation> _operations = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates an outbox over a time provider.</summary>
    public InMemoryOutbox(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> EnqueueAsync(OutboxOperation operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operation);
        _operations[operation.OpId] = operation;
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<OutboxOperation?, Failure>> GetAsync(string opId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _operations.TryGetValue(opId, out OutboxOperation? operation);
        return Task.FromResult(Result.Success<OutboxOperation?>(operation));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<OutboxOperation>, Failure>> ListPendingAsync(int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<OutboxOperation> pending = new();
        foreach (OutboxOperation operation in _operations.Values)
        {
            bool due = operation.State == OutboxState.Queued ||
                (operation.State == OutboxState.Failed &&
                 (operation.NextAttemptAtUtc is null || operation.NextAttemptAtUtc <= _timeProvider.GetUtcNow()));
            if (due)
            {
                pending.Add(operation);
            }
        }

        pending.Sort(static (left, right) => left.CreatedAtUtc.CompareTo(right.CreatedAtUtc));
        if (pending.Count > limit)
        {
            pending = pending.GetRange(0, limit);
        }

        return Task.FromResult(Result.Success<IReadOnlyList<OutboxOperation>>(pending));
    }

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> MarkInProgressAsync(string opId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_operations.TryGetValue(opId, out OutboxOperation? operation))
        {
            return Task.FromResult(Result.Failure(Errors.NotFound("outbox operation", opId)));
        }

        operation.State = OutboxState.InProgress;
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> MarkDoneAsync(string opId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_operations.TryGetValue(opId, out OutboxOperation? operation))
        {
            return Task.FromResult(Result.Failure(Errors.NotFound("outbox operation", opId)));
        }

        operation.State = OutboxState.Done;
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> MarkFailedAsync(string opId, string errorMessage, DateTimeOffset nextAttemptAtUtc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_operations.TryGetValue(opId, out OutboxOperation? operation))
        {
            return Task.FromResult(Result.Failure(Errors.NotFound("outbox operation", opId)));
        }

        operation.State = OutboxState.Failed;
        operation.Attempts++;
        operation.LastError = errorMessage;
        operation.NextAttemptAtUtc = nextAttemptAtUtc;
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> DeleteAsync(string opId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _operations.Remove(opId);
        return Task.FromResult(Result.Success());
    }
}
