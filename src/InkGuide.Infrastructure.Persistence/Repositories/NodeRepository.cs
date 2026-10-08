using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InkGuide.Infrastructure.Persistence.Repositories;

// Local ports (temporary seams): canonical INodeRepository/IOutbox/etc. are owned by
// Application (see D-006). These mirror the needed subsets and will be adapted once available.

/// <summary>One page of child nodes with an opaque offset cursor.</summary>
public sealed record NodePage(IReadOnlyList<NodeEntity> Items, string? NextCursor);

/// <summary>Node persistence port (local seam mirroring the future Application port).</summary>
public interface INodeRepository
{
    Task<Result<NodeEntity>> GetAsync(string nodeId, CancellationToken cancellationToken);

    Task<Result<NodePage>> ListChildrenAsync(string accountId, string? parentId, int limit, string? cursor, CancellationToken cancellationToken);

    Task<Result> UpsertAsync(NodeEntity node, CancellationToken cancellationToken);

    Task<Result> UpsertBatchAsync(IReadOnlyList<NodeEntity> nodes, CancellationToken cancellationToken);

    Task<Result> RemoveAsync(string nodeId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<NodeEntity>>> FindDuplicatesAsync(string accountId, CancellationToken cancellationToken);

    Task<Result<int>> CountChildrenAsync(string accountId, string? parentId, CancellationToken cancellationToken);
}

/// <summary>EF Core implementation of <see cref="INodeRepository"/>.</summary>
public sealed class NodeRepository : INodeRepository
{
    private readonly IDbContextFactory<InkGuideDbContext> _factory;
    private readonly SqliteWriteGate _gate;

    public NodeRepository(IDbContextFactory<InkGuideDbContext> factory, SqliteWriteGate gate)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(gate);
        _factory = factory;
        _gate = gate;
    }

    /// <inheritdoc />
    public async Task<Result<NodeEntity>> GetAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? node = await db.Nodes.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
        return node is null
            ? Result.Fail<NodeEntity>(OperationError.NotFound($"Node '{nodeId}' was not found."))
            : Result.Ok(node);
    }

    /// <inheritdoc />
    public async Task<Result<NodePage>> ListChildrenAsync(string accountId, string? parentId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        int take = Math.Clamp(limit <= 0 ? 200 : limit, 1, 1000);
        int offset = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            offset = Math.Max(0, parsed);
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<NodeEntity> baseQuery = db.Nodes.AsNoTracking()
            .Where(n => n.AccountId == accountId && n.ParentId == parentId && !n.Trashed);
        int total = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        List<NodeEntity> items = await baseQuery
            .OrderBy(n => n.Name).ThenBy(n => n.Id)
            .Skip(offset).Take(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        string? nextCursor = offset + items.Count < total
            ? (offset + items.Count).ToString(CultureInfo.InvariantCulture)
            : null;
        return Result.Ok(new NodePage(items, nextCursor));
    }

    /// <inheritdoc />
    public async Task<Result> UpsertAsync(NodeEntity node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            NodeEntity? existing = await db.Nodes.FindAsync(new object[] { node.Id }, ct).ConfigureAwait(false);
            if (existing is null)
            {
                db.Nodes.Add(node);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(node);
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> UpsertBatchAsync(IReadOnlyList<NodeEntity> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            foreach (NodeEntity node in nodes)
            {
                NodeEntity? existing = await db.Nodes.FindAsync(new object[] { node.Id }, ct).ConfigureAwait(false);
                if (existing is null)
                {
                    db.Nodes.Add(node);
                }
                else
                {
                    db.Entry(existing).CurrentValues.SetValues(node);
                }
            }

            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        return await _gate.ExecuteWriteAsync(async ct =>
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            NodeEntity? existing = await db.Nodes.FindAsync(new object[] { nodeId }, ct).ConfigureAwait(false);
            if (existing is null)
            {
                return Result.Fail(OperationError.NotFound($"Node '{nodeId}' was not found."));
            }

            db.Nodes.Remove(existing);
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<NodeEntity>>> FindDuplicatesAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot translate composite GroupBy over nullable keys: group in memory.
        List<NodeEntity> candidates = await db.Nodes.AsNoTracking()
            .Where(n => n.AccountId == accountId && !n.Trashed && !n.IsFolder && n.Md5 != null && n.Size > 0)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<NodeEntity> dupes = candidates
            .GroupBy(n => n.Md5 + ":" + n.Size, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.OrderBy(n => n.PathText, StringComparer.Ordinal))
            .ToList();
        return Result.Ok<IReadOnlyList<NodeEntity>>(dupes);
    }

    /// <inheritdoc />
    public async Task<Result<int>> CountChildrenAsync(string accountId, string? parentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int count = await db.Nodes.CountAsync(n => n.AccountId == accountId && n.ParentId == parentId && !n.Trashed, cancellationToken).ConfigureAwait(false);
        return Result.Ok(count);
    }
}
