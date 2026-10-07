using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InkGuide.Infrastructure.Persistence.Repositories;

/// <summary>Durable outbox port (local seam).</summary>
public interface IOutboxStore
{
    Task<Result> EnqueueAsync(OutboxOperationEntity operation, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OutboxOperationEntity>>> DequeueDueAsync(int maxItems, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<Result> MarkSucceededAsync(string opId, CancellationToken cancellationToken);

    Task<Result> MarkFailedAsync(string opId, string errorMessage, DateTimeOffset nextAttemptAtUtc, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OutboxOperationEntity>>> ListAsync(string accountId, CancellationToken cancellationToken);

    Task<Result> DiscardAsync(string opId, CancellationToken cancellationToken);
}

/// <summary>EF Core implementation of <see cref="IOutboxStore"/> with idempotent enqueue.</summary>
public sealed class OutboxStore : IOutboxStore
{
    private readonly IDbContextFactory<InkGuideDbContext> _factory;
    private readonly SqliteWriteGate _gate;
    private readonly TimeProvider _clock;

    public OutboxStore(IDbContextFactory<InkGuideDbContext> factory, SqliteWriteGate gate, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(clock);
        _factory = factory;
        _gate = gate;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> EnqueueAsync(OutboxOperationEntity operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            OutboxOperationEntity? existing = await db.OutboxOperations.FindAsync(new object[] { operation.OpId }, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return Result.Ok();
            }

            operation.CreatedAtUtc = _clock.GetUtcNow();
            operation.UpdatedAtUtc = operation.CreatedAtUtc;
            db.OutboxOperations.Add(operation);
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<OutboxOperationEntity>>> DequeueDueAsync(int maxItems, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite provider cannot translate DateTimeOffset comparisons; filter in memory.
        // Pending sets are small (outbox), so client evaluation is acceptable and documented.
        List<OutboxOperationEntity> pending = await db.OutboxOperations.AsNoTracking()
            .Where(o => o.State == "Pending")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<OutboxOperationEntity> items = pending
            .Where(o => o.NextAttemptAtUtc <= nowUtc)
            .OrderBy(o => o.NextAttemptAtUtc)
            .Take(Math.Clamp(maxItems, 1, 500))
            .ToList();
        return Result.Ok<IReadOnlyList<OutboxOperationEntity>>(items);
    }

    /// <inheritdoc />
    public async Task<Result> MarkSucceededAsync(string opId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opId);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            OutboxOperationEntity? existing = await db.OutboxOperations.FindAsync(new object[] { opId }, ct).ConfigureAwait(false);
            if (existing is null)
            {
                return Result.Fail(OperationError.NotFound($"Outbox op '{opId}' was not found."));
            }

            db.OutboxOperations.Remove(existing);
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> MarkFailedAsync(string opId, string errorMessage, DateTimeOffset nextAttemptAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opId);
        ArgumentNullException.ThrowIfNull(errorMessage);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            OutboxOperationEntity? existing = await db.OutboxOperations.FindAsync(new object[] { opId }, ct).ConfigureAwait(false);
            if (existing is null)
            {
                return Result.Fail(OperationError.NotFound($"Outbox op '{opId}' was not found."));
            }

            existing.Attempts += 1;
            existing.LastError = errorMessage;
            existing.NextAttemptAtUtc = nextAttemptAtUtc;
            existing.UpdatedAtUtc = _clock.GetUtcNow();
            existing.State = existing.Attempts >= 10 ? "Dead" : "Pending";
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<OutboxOperationEntity>>> ListAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot ORDER BY DateTimeOffset; sort in memory (outbox lists are capped at 500).
        List<OutboxOperationEntity> items = await db.OutboxOperations.AsNoTracking()
            .Where(o => o.AccountId == accountId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<OutboxOperationEntity> ordered = items
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(500)
            .ToList();
        return Result.Ok<IReadOnlyList<OutboxOperationEntity>>(ordered);
    }

    /// <inheritdoc />
    public async Task<Result> DiscardAsync(string opId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opId);
        return await MarkSucceededAsync(opId, cancellationToken).ConfigureAwait(false);
    }
}
