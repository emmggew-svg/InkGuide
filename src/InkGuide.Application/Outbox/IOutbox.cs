using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;
using InkGuide.Domain.Entities;

namespace InkGuide.Application.Outbox;

/// <summary>
/// Durable queue of local intents awaiting push to the provider.
/// Every local mutation is written here before the UI is acknowledged.
/// </summary>
public interface IOutbox
{
    /// <summary>Enqueues an operation. The op id makes replays idempotent.</summary>
    Task<Result<Unit, Failure>> EnqueueAsync(OutboxOperation operation, CancellationToken ct);

    /// <summary>Gets one operation by id. Returns success with null when absent.</summary>
    Task<Result<OutboxOperation?, Failure>> GetAsync(string opId, CancellationToken ct);

    /// <summary>Lists pending (queued or failed-and-due) operations, oldest first.</summary>
    Task<Result<IReadOnlyList<OutboxOperation>, Failure>> ListPendingAsync(int limit, CancellationToken ct);

    /// <summary>Marks an operation as in progress (claims it for one worker).</summary>
    Task<Result<Unit, Failure>> MarkInProgressAsync(string opId, CancellationToken ct);

    /// <summary>Marks an operation as done.</summary>
    Task<Result<Unit, Failure>> MarkDoneAsync(string opId, CancellationToken ct);

    /// <summary>Records a failed attempt with backoff scheduling.</summary>
    /// <param name="opId">Failed operation id.</param>
    /// <param name="errorMessage">Developer-oriented error text. Never contains secrets.</param>
    /// <param name="nextAttemptAtUtc">Earliest next attempt (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<Unit, Failure>> MarkFailedAsync(string opId, string errorMessage, DateTimeOffset nextAttemptAtUtc, CancellationToken ct);

    /// <summary>Discards an operation without replaying it (guarded upstream).</summary>
    Task<Result<Unit, Failure>> DeleteAsync(string opId, CancellationToken ct);
}
