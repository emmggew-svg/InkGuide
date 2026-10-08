using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Persistence.Sync;
using Microsoft.EntityFrameworkCore;

namespace InkGuide.App.Bridge.Backend;

/// <summary>
/// Account-routing outbox executor and sync hooks for the background workers.
/// Payloads carry their account id; analysis requests are grouped per account
/// by looking up the nodes.
/// </summary>
internal sealed class BackendOutboxRouter : IOutboxExecutor, ISyncHooks
{
    private readonly SyncCoordinator _sync;
    private readonly BackendHost _host;
    private readonly BackendSettingsService _settings;
    private readonly IDbContextFactory<InkGuideDbContext> _factory;

    public BackendOutboxRouter(
        SyncCoordinator sync,
        BackendHost host,
        BackendSettingsService settings,
        IDbContextFactory<InkGuideDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(factory);
        _sync = sync;
        _host = host;
        _settings = settings;
        _factory = factory;
    }

    public Task<Result> ExecuteAsync(OutboxPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return _sync.ForAccount(payload.AccountId).ExecuteAsync(payload, cancellationToken);
    }

    public async Task OnAnalysisRequestedAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
    {
        var byAccount = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        foreach (string nodeId in nodeIds)
        {
            NodeEntity? node = await db.Nodes.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
            if (node is null)
            {
                continue;
            }

            if (!byAccount.TryGetValue(node.AccountId, out List<string>? list))
            {
                list = new List<string>();
                byAccount[node.AccountId] = list;
            }

            list.Add(nodeId);
        }

        foreach (KeyValuePair<string, List<string>> kv in byAccount)
        {
            await _sync.EnqueueAnalysisAsync(kv.Key, kv.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task OnConflictCreatedAsync(string conflictId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return _host.EmitEventAsync("conflict.created", conflictId);
    }

    public Task OnStateChangedAsync(string accountId, SyncState state, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        string text = state switch
        {
            SyncState.Idle => "idle",
            SyncState.Indexing => "indexing",
            SyncState.Syncing => "syncing",
            SyncState.Offline => "offline",
            SyncState.Paused => "paused",
            SyncState.Error => "error",
            SyncState.ReauthRequired => "reauthRequired",
            _ => "idle",
        };
        return _host.EmitEventAsync("sync.stateChanged", accountId + ":" + text);
    }
}
