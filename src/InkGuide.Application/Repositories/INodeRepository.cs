using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Application.Common;
using InkGuide.Domain.Common;
using InkGuide.Domain.Entities;

namespace InkGuide.Application.Repositories;

/// <summary>Filter for cached node listings.</summary>
/// <param name="AccountId">Owning account id.</param>
/// <param name="ParentId">Parent node id. Null lists roots.</param>
/// <param name="RepositoryId">Repository scope. Null for unscoped listings.</param>
/// <param name="IsFolder">When set, restricts to folders or files.</param>
/// <param name="IncludeTrashed">Whether trashed nodes are included.</param>
public sealed record NodeFilter(
    string AccountId,
    string? ParentId = null,
    string? RepositoryId = null,
    bool? IsFolder = null,
    bool IncludeTrashed = false);

/// <summary>
/// Cached node persistence. Implementations are rebuildable mirrors of Drive:
/// dropping and re-indexing must lose nothing except un-synced outbox entries.
/// </summary>
public interface INodeRepository
{
    /// <summary>Gets one cached node. Returns success with null when absent.</summary>
    Task<Result<Node?, Failure>> GetAsync(string accountId, string nodeId, CancellationToken ct);

    /// <summary>Lists one page of cached children matching a filter.</summary>
    Task<Result<PagedResult<Node>, Failure>> ListAsync(NodeFilter filter, string? cursor, int limit, CancellationToken ct);

    /// <summary>Inserts or updates a cached node.</summary>
    Task<Result<Unit, Failure>> UpsertAsync(Node node, CancellationToken ct);

    /// <summary>Removes a cached node.</summary>
    Task<Result<Unit, Failure>> DeleteAsync(string accountId, string nodeId, CancellationToken ct);

    /// <summary>Finds cached files by content hash and size (duplicate finder input). Never downloads bytes.</summary>
    Task<Result<IReadOnlyList<Node>, Failure>> FindByHashAsync(string accountId, string md5, long sizeBytes, CancellationToken ct);
}
