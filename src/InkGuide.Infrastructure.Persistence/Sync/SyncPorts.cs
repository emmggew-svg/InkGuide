using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Common;

namespace InkGuide.Infrastructure.Persistence.Sync;

/// <summary>
/// Provider-agnostic source for the sync engine (local seam).
/// Implemented by an adapter over IStorageProvider in the composition root.
/// </summary>
public interface ISyncSource
{
    Task<Result<string>> GetRootIdAsync(CancellationToken cancellationToken);

    Task<Result<RemotePage>> ListChildrenAsync(string parentId, string? cursor, int pageSize, CancellationToken cancellationToken);

    Task<Result<RemoteItem>> GetItemAsync(string itemId, CancellationToken cancellationToken);

    Task<Result<string>> GetStartChangeTokenAsync(CancellationToken cancellationToken);

    Task<Result<RemoteChangeSet>> GetChangesAsync(string token, CancellationToken cancellationToken);
}

/// <summary>Callbacks the host wires to analysis, conflict surfacing, and state events.</summary>
public interface ISyncHooks
{
    Task OnAnalysisRequestedAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken);

    Task OnConflictCreatedAsync(string conflictId, CancellationToken cancellationToken);

    Task OnStateChangedAsync(string accountId, SyncState state, CancellationToken cancellationToken);
}

/// <summary>Executes a single outbox operation against the provider. Returns failure to retry later.</summary>
public interface IOutboxExecutor
{
    Task<Result> ExecuteAsync(Common.OutboxPayload payload, CancellationToken cancellationToken);
}
