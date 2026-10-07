using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InkGuide.Infrastructure.Persistence.Repositories;

/// <summary>Journal, snapshots, settings, change tokens, conflicts (local seam).</summary>
public interface IMiscStore
{
    Task<Result> AppendJournalAsync(OperationJournalEntity entry, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OperationJournalEntity>>> ListJournalAsync(string accountId, int limit, CancellationToken cancellationToken);

    Task<Result> MarkUndoneAsync(long journalId, CancellationToken cancellationToken);

    Task<Result> SaveSnapshotAsync(SnapshotEntity snapshot, IReadOnlyList<SnapshotEntryEntity> entries, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SnapshotEntity>>> ListSnapshotsAsync(string repositoryId, CancellationToken cancellationToken);

    Task<Result<string?>> GetSettingAsync(string key, CancellationToken cancellationToken);

    Task<Result> SetSettingAsync(string key, string valueJson, CancellationToken cancellationToken);

    Task<Result<string?>> GetChangeTokenAsync(string accountId, CancellationToken cancellationToken);

    Task<Result> SetChangeTokenAsync(string accountId, string token, CancellationToken cancellationToken);

    Task<Result> SaveConflictAsync(ConflictRecordEntity conflict, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ConflictRecordEntity>>> ListOpenConflictsAsync(string accountId, CancellationToken cancellationToken);

    Task<Result> ResolveConflictAsync(string conflictId, string resolution, CancellationToken cancellationToken);
}

/// <summary>EF Core implementation of <see cref="IMiscStore"/>.</summary>
public sealed class MiscStore : IMiscStore
{
    private readonly IDbContextFactory<InkGuideDbContext> _factory;
    private readonly SqliteWriteGate _gate;
    private readonly TimeProvider _clock;

    public MiscStore(IDbContextFactory<InkGuideDbContext> factory, SqliteWriteGate gate, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(clock);
        _factory = factory;
        _gate = gate;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> AppendJournalAsync(OperationJournalEntity entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            entry.OccurredAtUtc = _clock.GetUtcNow();
            db.OperationJournal.Add(entry);
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<OperationJournalEntity>>> ListJournalAsync(string accountId, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot ORDER BY DateTimeOffset; sort in memory.
        List<OperationJournalEntity> rows = await db.OperationJournal.AsNoTracking()
            .Where(j => j.AccountId == accountId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<OperationJournalEntity> items = rows
            .OrderByDescending(j => j.OccurredAtUtc)
            .Take(Math.Clamp(limit <= 0 ? 200 : limit, 1, 1000))
            .ToList();
        return Result.Ok<IReadOnlyList<OperationJournalEntity>>(items);
    }

    /// <inheritdoc />
    public async Task<Result> MarkUndoneAsync(long journalId, CancellationToken cancellationToken)
    {
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            OperationJournalEntity? entry = await db.OperationJournal.FindAsync(new object[] { journalId }, ct).ConfigureAwait(false);
            if (entry is null)
            {
                return Result.Fail(OperationError.NotFound($"Journal entry '{journalId}' was not found."));
            }

            entry.Undone = true;
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SaveSnapshotAsync(SnapshotEntity snapshot, IReadOnlyList<SnapshotEntryEntity> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(entries);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            snapshot.CreatedAtUtc = _clock.GetUtcNow();
            db.Snapshots.Add(snapshot);
            foreach (SnapshotEntryEntity entry in entries)
            {
                entry.SnapshotId = snapshot.Id;
                db.SnapshotEntries.Add(entry);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SnapshotEntity>>> ListSnapshotsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoryId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot ORDER BY DateTimeOffset; sort in memory.
        List<SnapshotEntity> rows = await db.Snapshots.AsNoTracking()
            .Where(s => s.RepositoryId == repositoryId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<SnapshotEntity> items = rows.OrderByDescending(s => s.CreatedAtUtc).ToList();
        return Result.Ok<IReadOnlyList<SnapshotEntity>>(items);
    }

    /// <inheritdoc />
    public async Task<Result<string?>> GetSettingAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        SettingsKvEntity? row = await db.SettingsKv.FindAsync(new object[] { key }, cancellationToken).ConfigureAwait(false);
        return Result.Ok<string?>(row?.ValueJson);
    }

    /// <inheritdoc />
    public async Task<Result> SetSettingAsync(string key, string valueJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(valueJson);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            SettingsKvEntity? row = await db.SettingsKv.FindAsync(new object[] { key }, ct).ConfigureAwait(false);
            if (row is null)
            {
                db.SettingsKv.Add(new SettingsKvEntity { Key = key, ValueJson = valueJson, UpdatedAtUtc = _clock.GetUtcNow() });
            }
            else
            {
                row.ValueJson = valueJson;
                row.UpdatedAtUtc = _clock.GetUtcNow();
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<string?>> GetChangeTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ChangeTokenEntity? row = await db.ChangeTokens.FindAsync(new object[] { accountId }, cancellationToken).ConfigureAwait(false);
        return Result.Ok<string?>(row?.Token);
    }

    /// <inheritdoc />
    public async Task<Result> SetChangeTokenAsync(string accountId, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(token);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            ChangeTokenEntity? row = await db.ChangeTokens.FindAsync(new object[] { accountId }, ct).ConfigureAwait(false);
            if (row is null)
            {
                db.ChangeTokens.Add(new ChangeTokenEntity { AccountId = accountId, Token = token, UpdatedAtUtc = _clock.GetUtcNow() });
            }
            else
            {
                row.Token = token;
                row.UpdatedAtUtc = _clock.GetUtcNow();
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SaveConflictAsync(ConflictRecordEntity conflict, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            conflict.CreatedAtUtc = _clock.GetUtcNow();
            db.ConflictRecords.Add(conflict);
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ConflictRecordEntity>>> ListOpenConflictsAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot ORDER BY DateTimeOffset; sort in memory.
        List<ConflictRecordEntity> rows = await db.ConflictRecords.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.Resolution == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ConflictRecordEntity> items = rows.OrderByDescending(c => c.CreatedAtUtc).ToList();
        return Result.Ok<IReadOnlyList<ConflictRecordEntity>>(items);
    }

    /// <inheritdoc />
    public async Task<Result> ResolveConflictAsync(string conflictId, string resolution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conflictId);
        ArgumentNullException.ThrowIfNull(resolution);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            ConflictRecordEntity? conflict = await db.ConflictRecords.FindAsync(new object[] { conflictId }, ct).ConfigureAwait(false);
            if (conflict is null)
            {
                return Result.Fail(OperationError.NotFound($"Conflict '{conflictId}' was not found."));
            }

            conflict.Resolution = resolution;
            conflict.ResolvedAtUtc = _clock.GetUtcNow();
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }
}
