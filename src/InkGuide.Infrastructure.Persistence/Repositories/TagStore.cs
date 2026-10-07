using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InkGuide.Infrastructure.Persistence.Repositories;

/// <summary>Tag definitions, assignments, and suppressions (local seam).</summary>
public interface ITagStore
{
    Task<Result<TagEntity>> UpsertDefinitionAsync(TagEntity tag, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<TagEntity>>> ListDefinitionsAsync(string accountId, CancellationToken cancellationToken);

    Task<Result> AssignAsync(string accountId, string tagId, string nodeId, CancellationToken cancellationToken);

    Task<Result> UnassignAsync(string tagId, string nodeId, CancellationToken cancellationToken);

    Task<Result> SuppressAsync(string accountId, string tagName, string? nodeId, CancellationToken cancellationToken);

    Task<Result<bool>> IsSuppressedAsync(string accountId, string tagName, string? nodeId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<string>>> GetNodeTagNamesAsync(string nodeId, CancellationToken cancellationToken);
}

/// <summary>EF Core implementation of <see cref="ITagStore"/>.</summary>
public sealed class TagStore : ITagStore
{
    private readonly IDbContextFactory<InkGuideDbContext> _factory;
    private readonly SqliteWriteGate _gate;
    private readonly TimeProvider _clock;

    public TagStore(IDbContextFactory<InkGuideDbContext> factory, SqliteWriteGate gate, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(clock);
        _factory = factory;
        _gate = gate;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<TagEntity>> UpsertDefinitionAsync(TagEntity tag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            List<TagEntity> candidates = await db.Tags
                .Where(t => t.AccountId == tag.AccountId)
                .ToListAsync(ct).ConfigureAwait(false);
            TagEntity? existing = null;
            foreach (TagEntity candidate in candidates)
            {
                if (string.Equals(candidate.Name, tag.Name, StringComparison.OrdinalIgnoreCase))
                {
                    existing = candidate;
                    break;
                }
            }
            if (existing is null)
            {
                tag.UpdatedAtUtc = _clock.GetUtcNow();
                db.Tags.Add(tag);
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result.Ok(tag);
            }

            existing.Namespace = tag.Namespace;
            existing.Description = tag.Description;
            existing.Origin = tag.Origin;
            existing.ColorToken = tag.ColorToken;
            existing.IsSystem = tag.IsSystem;
            existing.UpdatedAtUtc = _clock.GetUtcNow();
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok(existing);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TagEntity>>> ListDefinitionsAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<TagEntity> tags = await db.Tags.AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok<IReadOnlyList<TagEntity>>(tags);
    }

    /// <inheritdoc />
    public async Task<Result> AssignAsync(string accountId, string tagId, string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(tagId);
        ArgumentNullException.ThrowIfNull(nodeId);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            bool exists = await db.NodeTags.AnyAsync(n => n.TagId == tagId && n.NodeId == nodeId, ct).ConfigureAwait(false);
            if (!exists)
            {
                db.NodeTags.Add(new NodeTagEntity { TagId = tagId, NodeId = nodeId, AccountId = accountId, AssignedAtUtc = _clock.GetUtcNow() });
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> UnassignAsync(string tagId, string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tagId);
        ArgumentNullException.ThrowIfNull(nodeId);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            NodeTagEntity? existing = await db.NodeTags.FindAsync(new object[] { tagId, nodeId }, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                db.NodeTags.Remove(existing);
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SuppressAsync(string accountId, string tagName, string? nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(tagName);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            bool exists = await db.TagSuppressions.AnyAsync(
                s => s.AccountId == accountId && s.TagName == tagName && s.NodeId == nodeId, ct).ConfigureAwait(false);
            if (!exists)
            {
                db.TagSuppressions.Add(new TagSuppressionEntity
                {
                    AccountId = accountId,
                    TagName = tagName,
                    NodeId = nodeId,
                    SuppressedAtUtc = _clock.GetUtcNow(),
                });
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> IsSuppressedAsync(string accountId, string tagName, string? nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(tagName);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        bool suppressed = await db.TagSuppressions.AnyAsync(
            s => s.AccountId == accountId && s.TagName == tagName && (s.NodeId == nodeId || s.NodeId == null), cancellationToken).ConfigureAwait(false);
        return Result.Ok(suppressed);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> GetNodeTagNamesAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<string> names = await db.NodeTags.AsNoTracking()
            .Where(n => n.NodeId == nodeId)
            .Join(db.Tags, nt => nt.TagId, t => t.Id, (nt, t) => t.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok<IReadOnlyList<string>>(names);
    }
}
