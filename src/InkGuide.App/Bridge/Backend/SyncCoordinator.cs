using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Analysis.Engine;
using InkGuide.Domain.Entities;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Persistence.Repositories;
using InkGuide.Infrastructure.Persistence.Search;
using InkGuide.Infrastructure.Persistence.Sync;
using InkGuide.Infrastructure.Storage.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PCommon = global::InkGuide.Infrastructure.Persistence.Common;
using SCommon = global::InkGuide.Infrastructure.Storage.Common;

using InkGuide.App.Hosting;
namespace InkGuide.App.Bridge.Backend;

/// <summary>Provider bound to an account for one sync scope.</summary>
public sealed record ResolvedProvider(IStorageProvider Provider, string ScopeKey, string AccountId);

/// <summary>
/// Owns sync state per account and adapts providers to the sync engine
/// (ISyncSource), outbox replay (IOutboxExecutor) and side effects (ISyncHooks).
/// Account-scoped work happens through <see cref="AccountSync"/>.
/// </summary>
public sealed class SyncCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SyncEngine _engine;
    private readonly IOutboxStore _outbox;
    private readonly IMiscStore _misc;
    private readonly INodeRepository _nodes;
    private readonly CatalogHolder _holder;
    private readonly ILoggerFactory _loggerFactory;
    private readonly BackendSettingsService _settings;
    private readonly ITagStore _tags;
    private readonly NodeSearchService _search;
    private readonly IDbContextFactory<InkGuideDbContext> _factory;
    private readonly SqliteWriteGate _gate;
    private readonly TimeProvider _clock;
    private readonly ILogger<SyncCoordinator> _logger;
    private readonly IProviderResolver _providers;
    private readonly BackendHost _host;
    private readonly ConcurrentDictionary<string, AccountSyncState> _states = new(StringComparer.Ordinal);

    public SyncCoordinator(
        SyncEngine engine,
        IOutboxStore outbox,
        IMiscStore misc,
        INodeRepository nodes,
        CatalogHolder holder,
        ILoggerFactory loggerFactory,
        BackendSettingsService settings,
        ITagStore tags,
        NodeSearchService search,
        IDbContextFactory<InkGuideDbContext> factory,
        SqliteWriteGate gate,
        TimeProvider clock,
        ILogger<SyncCoordinator> logger,
        IProviderResolver providers,
        BackendHost host)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(misc);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(host);
        _engine = engine;
        _outbox = outbox;
        _misc = misc;
        _nodes = nodes;
        _holder = holder;
        _loggerFactory = loggerFactory;
        _settings = settings;
        _tags = tags;
        _search = search;
        _factory = factory;
        _gate = gate;
        _clock = clock;
        _logger = logger;
        _providers = providers;
        _host = host;
    }

    public AccountSync ForAccount(string accountId)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        return new AccountSync(this, accountId);
    }

    public string GetState(string accountId)
    {
        return _states.TryGetValue(accountId, out AccountSyncState? state) ? state.State : "idle";
    }

    public string? GetLastError(string accountId)
    {
        return _states.TryGetValue(accountId, out AccountSyncState? state) ? state.LastError : null;
    }

    private void SetState(string accountId, string state, string? error = null)
    {
        _states.AddOrUpdate(
            accountId,
            _ => new AccountSyncState(state, error),
            (_, _) => new AccountSyncState(state, error));
    }

    private sealed record AccountSyncState(string State, string? LastError);

    /// <summary>Account-scoped sync: source adapter, outbox executor, hooks, and operations.</summary>
    public sealed class AccountSync : ISyncSource, IOutboxExecutor, ISyncHooks
    {
        private readonly SyncCoordinator _parent;
        private readonly string _accountId;

        public AccountSync(SyncCoordinator parent, string accountId)
        {
            _parent = parent;
            _accountId = accountId;
        }

        public async Task<PCommon.Result<string>> GetRootIdAsync(CancellationToken cancellationToken)
        {
            ResolvedProvider? resolved = await _parent._providers.ResolveAsync(_accountId, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return PCommon.Result.Fail<string>(PCommon.OperationError.Provider("No storage provider is available for this account."));
            }

            SCommon.Result<StorageItem> root = await resolved.Provider.GetRootAsync(cancellationToken).ConfigureAwait(false);
            if (!root.IsSuccess || root.Value is null)
            {
                return PCommon.Result.Fail<string>(ToPersistence(root.Failure!));
            }

            return PCommon.Result.Ok(root.Value.Id);
        }

        public async Task<PCommon.Result<RemotePage>> ListChildrenAsync(string parentId, string? cursor, int pageSize, CancellationToken cancellationToken)
        {
            ResolvedProvider? resolved = await _parent._providers.ResolveAsync(_accountId, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return PCommon.Result.Fail<RemotePage>(PCommon.OperationError.Provider("No storage provider is available for this account."));
            }

            SCommon.Result<StorageItemPage> page = await resolved.Provider.ListChildrenAsync(parentId, cursor, pageSize, cancellationToken).ConfigureAwait(false);
            if (!page.IsSuccess || page.Value is null)
            {
                return PCommon.Result.Fail<RemotePage>(ToPersistence(page.Failure!));
            }

            var items = new List<RemoteItem>();
            foreach (StorageItem item in page.Value.Items)
            {
                items.Add(ToRemote(item));
            }

            return PCommon.Result.Ok(new RemotePage(items, page.Value.NextCursor));
        }

        public async Task<PCommon.Result<RemoteItem>> GetItemAsync(string itemId, CancellationToken cancellationToken)
        {
            ResolvedProvider? resolved = await _parent._providers.ResolveAsync(_accountId, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return PCommon.Result.Fail<RemoteItem>(PCommon.OperationError.Provider("No storage provider is available for this account."));
            }

            SCommon.Result<StorageItem> item = await resolved.Provider.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
            if (!item.IsSuccess || item.Value is null)
            {
                return PCommon.Result.Fail<RemoteItem>(ToPersistence(item.Failure!));
            }

            return PCommon.Result.Ok(ToRemote(item.Value));
        }

        public async Task<PCommon.Result<string>> GetStartChangeTokenAsync(CancellationToken cancellationToken)
        {
            ResolvedProvider? resolved = await _parent._providers.ResolveAsync(_accountId, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return PCommon.Result.Fail<string>(PCommon.OperationError.Provider("No storage provider is available for this account."));
            }

            SCommon.Result<string> token = await resolved.Provider.GetStartChangeTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!token.IsSuccess)
            {
                return PCommon.Result.Fail<string>(ToPersistence(token.Failure!));
            }

            return PCommon.Result.Ok(token.Value!);
        }

        public async Task<PCommon.Result<RemoteChangeSet>> GetChangesAsync(string token, CancellationToken cancellationToken)
        {
            ResolvedProvider? resolved = await _parent._providers.ResolveAsync(_accountId, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return PCommon.Result.Fail<RemoteChangeSet>(PCommon.OperationError.Provider("No storage provider is available for this account."));
            }

            SCommon.Result<StorageChangeSet> changes = await resolved.Provider.GetChangesAsync(token, cancellationToken).ConfigureAwait(false);
            if (!changes.IsSuccess || changes.Value is null)
            {
                return PCommon.Result.Fail<RemoteChangeSet>(ToPersistence(changes.Failure!));
            }

            var remote = new List<RemoteChange>();
            foreach (StorageChange change in changes.Value.Changes)
            {
                remote.Add(new RemoteChange(change.Item is null ? null : ToRemote(change.Item), change.ItemId, change.Removed));
            }

            return PCommon.Result.Ok(new RemoteChangeSet(remote, changes.Value.NextToken, changes.Value.MustReindex));
        }

        public async Task<PCommon.Result> ExecuteAsync(OutboxPayload payload, CancellationToken cancellationToken)
        {
            try
            {
                return await _parent.ExecuteOutboxAsync(_accountId, payload, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return PCommon.Result.Fail(PCommon.OperationError.Provider("Outbox replay failed: " + ex.GetType().Name));
            }
        }

        public Task OnAnalysisRequestedAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
        {
            return _parent.EnqueueAnalysisAsync(_accountId, nodeIds, cancellationToken);
        }

        public Task OnConflictCreatedAsync(string conflictId, CancellationToken cancellationToken)
        {
            return _parent._host.EmitEventAsync("conflict.created", conflictId);
        }

        public Task OnStateChangedAsync(string accountId, SyncState state, CancellationToken cancellationToken)
        {
            _parent.SetState(accountId, ToBridgeState(state));
            return _parent._host.EmitEventAsync("sync.stateChanged", accountId + ":" + ToBridgeState(state));
        }

        public async Task<SCommon.Result<SyncStatus>> GetStatusAsync(CancellationToken cancellationToken)
        {
            string paused = await _parent._settings.GetAsync(BackendSettingsService.Keys.SyncPaused, cancellationToken).ConfigureAwait(false);
            string state = _parent.GetState(_accountId);
            if (string.Equals(paused, "true", StringComparison.OrdinalIgnoreCase) && state == "idle")
            {
                state = "paused";
            }

            return SCommon.Result.Ok(new SyncStatus(state, _parent.GetLastError(_accountId), null));
        }

        public async Task<SCommon.Result<SyncStatus>> SyncNowAsync(IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
        {
            string paused = await _parent._settings.GetAsync(BackendSettingsService.Keys.SyncPaused, cancellationToken).ConfigureAwait(false);
            if (string.Equals(paused, "true", StringComparison.OrdinalIgnoreCase))
            {
                return SCommon.Result.Ok(new SyncStatus("paused", null, null));
            }

            ResolvedProvider? resolved = await _parent._providers.ResolveAsync(_accountId, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                _parent.SetState(_accountId, "offline", "No storage provider is available for this account.");
                return SCommon.Result.Ok(new SyncStatus("offline", "No storage provider is available for this account.", null));
            }

            _parent.SetState(_accountId, "syncing");
            try
            {
                PCommon.Result<string?> token = await _parent._misc.GetChangeTokenAsync(_accountId, cancellationToken).ConfigureAwait(false);
                if (!token.IsSuccess)
                {
                    return SCommon.Result.Fail<SyncStatus>(ToStorage(token.Failure!));
                }

                if (string.IsNullOrEmpty(token.Value))
                {
                    PCommon.Result indexed = await _parent._engine.IndexAsync(_accountId, this, this, progress, cancellationToken).ConfigureAwait(false);
                    if (!indexed.IsSuccess)
                    {
                        _parent.SetState(_accountId, "error", indexed.Failure?.Message);
                        return SCommon.Result.Fail<SyncStatus>(ToStorage(indexed.Failure!));
                    }
                }
                else
                {
                    PCommon.Result polled = await _parent._engine.PollOnceAsync(_accountId, this, this, cancellationToken).ConfigureAwait(false);
                    if (!polled.IsSuccess)
                    {
                        _parent.SetState(_accountId, "error", polled.Failure?.Message);
                        return SCommon.Result.Fail<SyncStatus>(ToStorage(polled.Failure!));
                    }
                }

                PCommon.Result<int> replayed = await _parent._engine.ReplayOutboxAsync(this, this, cancellationToken).ConfigureAwait(false);
                if (!replayed.IsSuccess)
                {
                    _parent.SetState(_accountId, "error", replayed.Failure?.Message);
                    return SCommon.Result.Fail<SyncStatus>(ToStorage(replayed.Failure!));
                }

                await _parent.DrainAnalysisAsync(_accountId, cancellationToken).ConfigureAwait(false);
                _parent.SetState(_accountId, "idle");
                return SCommon.Result.Ok(new SyncStatus("idle", null, _parent._clock.GetUtcNow().ToString("o")));
            }
            catch (OperationCanceledException)
            {
                _parent.SetState(_accountId, "idle");
                throw;
            }
            catch (Exception ex)
            {
                _parent.SetState(_accountId, "error", ex.Message);
                return SCommon.Result.Fail<SyncStatus>(SCommon.OperationError.Provider("Sync failed: " + ex.GetType().Name));
            }
        }

        private static string ToBridgeState(SyncState state)
        {
            return state switch
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
        }

        private static RemoteItem ToRemote(StorageItem item)
        {
            return new RemoteItem(
                item.Id, item.ParentId, item.Name, item.IsFolder, item.MimeType, item.Size,
                item.Md5, item.Version, item.HeadRevisionId, item.ModifiedTimeUtc, item.Trashed,
                item.Description, item.AppProperties, item.WebViewLink);
        }
    }

    /// <summary>Sync status view for bridge DTO mapping.</summary>
    public sealed record SyncStatus(string State, string? LastError, string? LastSyncAt);

    private static PCommon.OperationError ToPersistence(SCommon.OperationError error)
    {
        return new PCommon.OperationError(error.Code, error.Message, error.Retryable, error.Details);
    }

    private static SCommon.OperationError ToStorage(PCommon.OperationError error)
    {
        return new SCommon.OperationError(error.Code, error.Message, error.Retryable, error.Details);
    }

    internal async Task<PCommon.Result> ExecuteOutboxAsync(string accountId, OutboxPayload payload, CancellationToken cancellationToken)
    {
        ResolvedProvider? resolved = await _providers.ResolveAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return PCommon.Result.Fail(PCommon.OperationError.Provider("No storage provider is available for this account."));
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload.PayloadJson);
            JsonElement root = doc.RootElement;
            return payload.OperationType switch
            {
                OutboxTypes.CreateFolder => await ReplayCreateFolderAsync(resolved.Provider, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.Upload => await ReplayUploadAsync(resolved.Provider, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.UpdateContent => await ReplayUpdateContentAsync(resolved.Provider, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.Rename => await ReplayRenameAsync(resolved.Provider, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.Move => await ReplayMoveAsync(resolved.Provider, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.Copy => await ReplayCopyAsync(resolved.Provider, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.Trash => await ReplayTrashAsync(resolved.Provider, true, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.Restore => await ReplayTrashAsync(resolved.Provider, false, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.DeletePermanent => await ReplayDeleteAsync(resolved.Provider, root, cancellationToken).ConfigureAwait(false),
                OutboxTypes.SetMetadata => await ReplayMetadataAsync(resolved.Provider, root, cancellationToken).ConfigureAwait(false),
                _ => PCommon.Result.Fail(PCommon.OperationError.Provider("Unknown outbox operation '" + payload.OperationType + "'.")),
            };
        }
        catch (JsonException ex)
        {
            return PCommon.Result.Fail(PCommon.OperationError.Provider("Outbox payload is corrupt: " + ex.Message));
        }
    }

    internal Task EnqueueAnalysisAsync(string accountId, IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
    {
        return DrainAnalysisForAsync(accountId, nodeIds, cancellationToken);
    }

    internal async Task DrainAnalysisAsync(string accountId, CancellationToken cancellationToken)
    {
        List<string> pending = await TakeAnalysisQueueAsync(accountId, 200, cancellationToken).ConfigureAwait(false);
        if (pending.Count > 0)
        {
            await DrainAnalysisForAsync(accountId, pending, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<string>> TakeAnalysisQueueAsync(string accountId, int max, CancellationToken cancellationToken)
    {
        // SQLite cannot ORDER BY DateTimeOffset: fetch the small pending set, sort in memory.
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AnalysisJobEntity> jobs = await db.AnalysisJobs.AsNoTracking()
            .Where(j => j.AccountId == accountId && j.State == "Queued")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<string>();
        foreach (AnalysisJobEntity job in jobs.OrderBy(j => j.CreatedAtUtc))
        {
            if (ids.Count >= max)
            {
                break;
            }

            if (!ids.Contains(job.NodeId))
            {
                ids.Add(job.NodeId);
            }
        }

        return ids;
    }

    private async Task DrainAnalysisForAsync(string accountId, IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
    {
        ResolvedProvider? resolved = await _providers.ResolveAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return;
        }

        foreach (string nodeId in nodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await AnalyzeNodeAsync(accountId, resolved.Provider, nodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogMessages.BackendNote(_logger, "Analysis failed for " + nodeId + ": " + ex.Message);
            }
        }
    }

    /// <summary>Analyzes one node immediately (used after local mutations).</summary>
    public async Task AnalyzeOneAsync(string accountId, IStorageProvider provider, string nodeId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.AnalysisJobs.Add(new AnalysisJobEntity
            {
                AccountId = accountId,
                NodeId = nodeId,
                Priority = "User",
                State = "Queued",
                CreatedAtUtc = _clock.GetUtcNow(),
            });
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await AnalyzeNodeAsync(accountId, provider, nodeId, cancellationToken).ConfigureAwait(false);
        await MarkAnalysisDoneAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
    }

    internal async Task AnalyzeNodeAsync(string accountId, IStorageProvider provider, string nodeId, CancellationToken cancellationToken)
        => await AnalyzeNodeCoreAsync(accountId, provider, nodeId, cancellationToken).ConfigureAwait(false);

    private async Task AnalyzeNodeCoreAsync(string accountId, IStorageProvider provider, string nodeId, CancellationToken cancellationToken)
    {
        PCommon.Result<NodeEntity> cached = await _nodes.GetAsync(nodeId, cancellationToken).ConfigureAwait(false);
        NodeEntity? node = cached.IsSuccess ? cached.Value : null;
        SCommon.Result<StorageItem> remote = await provider.GetAsync(nodeId, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            await MarkAnalysisDoneAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
            return;
        }

        StorageItem item = remote.Value;
        byte[]? header = null;
        byte[]? content = null;
        if (!item.IsFolder && item.Size <= 262144)
        {
            await using var buffer = new MemoryStream();
            SCommon.Result downloaded = await provider.DownloadAsync(item.Id, buffer, item.Size > 8192 ? new ContentRange(0, 8191) : null, cancellationToken).ConfigureAwait(false);
            if (downloaded.IsSuccess)
            {
                header = buffer.ToArray();
                if (item.Size <= 262144)
                {
                    buffer.SetLength(0);
                    buffer.Position = 0;
                    SCommon.Result full = await provider.DownloadAsync(item.Id, buffer, null, cancellationToken).ConfigureAwait(false);
                    if (full.IsSuccess)
                    {
                        content = buffer.ToArray();
                    }
                }
            }
        }

        var input = new AnalysisInput(
            accountId, item.Id, item.Name, node?.PathText, item.MimeType, item.IsFolder, 0, null, null, header, content);
        var engine = new AnalysisEngine(
            _holder.Current.KindMap,
            _holder.Current.FileRoles,
            _clock,
            _loggerFactory.CreateLogger<AnalysisEngine>());
        Domain.Common.Result<NodeInsight, Domain.Common.Failure> insight =
            await engine.AnalyzeAsync(input, cancellationToken).ConfigureAwait(false);
        if (!insight.IsSuccess || insight.Value is null)
        {
            await MarkAnalysisDoneAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await PersistInsightAsync(accountId, item, insight.Value, cancellationToken).ConfigureAwait(false);
        await MarkAnalysisDoneAsync(accountId, nodeId, cancellationToken).ConfigureAwait(false);
        await IndexForSearchAsync(accountId, nodeId, content, cancellationToken).ConfigureAwait(false);
    }

    internal async Task IndexForSearchAsync(string accountId, string nodeId, byte[]? contentExcerpt, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        NodeEntity? node = await db.Nodes.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
        if (node is null || !string.Equals(node.AccountId, accountId, StringComparison.Ordinal))
        {
            return;
        }

        NodeInsightEntity? insight = await db.NodeInsights.FindAsync(new object[] { nodeId }, cancellationToken).ConfigureAwait(false);
        List<NodeTagEntity> assigns = await db.NodeTags.AsNoTracking().Where(t => t.NodeId == nodeId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var tagNames = new List<string>();
        foreach (NodeTagEntity assign in assigns)
        {
            TagEntity? tag = await db.Tags.FindAsync(new object[] { assign.TagId }, cancellationToken).ConfigureAwait(false);
            if (tag is not null)
            {
                tagNames.Add(tag.Name);
            }
        }

        string? content = null;
        if (contentExcerpt is not null && contentExcerpt.Length > 0 && (insight is null || !insight.Sensitive))
        {
            try
            {
                content = Encoding.UTF8.GetString(contentExcerpt, 0, Math.Min(contentExcerpt.Length, 65536));
            }
            catch (ArgumentException)
            {
                content = null;
            }
        }

        await _search.RemoveNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
        await _search.IndexNodeAsync(nodeId, node.Name, insight?.Summary, string.Join(" ", tagNames), node.PathText, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistInsightAsync(string accountId, StorageItem item, NodeInsight insight, CancellationToken cancellationToken)
    {
        bool sensitive = insight.Sensitivity == SensitivityLevel.Sensitive;
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            NodeInsightEntity? existing = await db.NodeInsights.FindAsync(new object[] { item.Id }, ct).ConfigureAwait(false);
            if (existing is null)
            {
                existing = new NodeInsightEntity { NodeId = item.Id, AccountId = accountId };
                db.NodeInsights.Add(existing);
            }

            existing.AccountId = accountId;
            existing.Kind = insight.Kind;
            existing.Role = insight.Role;
            existing.Summary = insight.Summary;
            existing.Confidence = insight.Confidence;
            existing.Source = insight.Source.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture);
            existing.Sensitive = sensitive;
            existing.ContentHash = insight.ContentHash;
            existing.AnalyzedAtUtc = _clock.GetUtcNow();
            existing.RulesetVersion = _holder.Current.RulesetVersion;
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    internal async Task EnsureTagAsync(string accountId, string name, string origin, CancellationToken cancellationToken)
    {
        string id = "tag-" + name.ToLowerInvariant().Replace(":", "-");
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            TagEntity? existing = await db.Tags.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
            if (existing is null)
            {
                db.Tags.Add(new TagEntity
                {
                    Id = id,
                    AccountId = accountId,
                    Name = name,
                    Origin = origin,
                    UpdatedAtUtc = _clock.GetUtcNow(),
                });
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkAnalysisDoneAsync(string accountId, string nodeId, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            List<AnalysisJobEntity> jobs = await db.AnalysisJobs
                .Where(j => j.AccountId == accountId && j.NodeId == nodeId && j.State == "Queued")
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (AnalysisJobEntity job in jobs)
            {
                job.State = "Done";
            }

            if (jobs.Count > 0)
            {
                _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string GetString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static async Task<PCommon.Result> ReplayCreateFolderAsync(IStorageProvider provider, JsonElement root, CancellationToken cancellationToken)
    {
        SCommon.Result<StorageItem> created = await provider.CreateFolderAsync(GetString(root, "parentId"), GetString(root, "name"), cancellationToken).ConfigureAwait(false);
        return created.IsSuccess
            ? PCommon.Result.Ok()
            : PCommon.Result.Fail(ToPersistence(created.Failure!));
    }

    private static async Task<PCommon.Result> ReplayUploadAsync(IStorageProvider provider, JsonElement root, CancellationToken cancellationToken)
    {
        string staging = GetString(root, "stagingPath");
        if (!File.Exists(staging))
        {
            return PCommon.Result.Fail(PCommon.OperationError.Provider("Upload staging file is missing; the operation cannot be replayed."));
        }

        await using FileStream stream = new(staging, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        SCommon.Result<StorageItem> uploaded = await provider.UploadAsync(GetString(root, "parentId"), GetString(root, "name"), stream, GetString(root, "mime"), GetString(root, "opId"), null, cancellationToken).ConfigureAwait(false);
        if (!uploaded.IsSuccess)
        {
            return PCommon.Result.Fail(ToPersistence(uploaded.Failure!));
        }

        try
        {
            File.Delete(staging);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return PCommon.Result.Ok();
    }

    private static async Task<PCommon.Result> ReplayUpdateContentAsync(IStorageProvider provider, JsonElement root, CancellationToken cancellationToken)
    {
        string staging = GetString(root, "stagingPath");
        if (!File.Exists(staging))
        {
            return PCommon.Result.Fail(PCommon.OperationError.Provider("Update staging file is missing; the operation cannot be replayed."));
        }

        await using FileStream stream = new(staging, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        SCommon.Result<StorageItem> updated = await provider.UpdateContentAsync(GetString(root, "nodeId"), stream, GetString(root, "expectedVersion"), GetString(root, "opId"), null, cancellationToken).ConfigureAwait(false);
        if (!updated.IsSuccess)
        {
            return PCommon.Result.Fail(ToPersistence(updated.Failure!));
        }

        try
        {
            File.Delete(staging);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return PCommon.Result.Ok();
    }

    private static async Task<PCommon.Result> ReplayRenameAsync(IStorageProvider provider, JsonElement root, CancellationToken cancellationToken)
    {
        SCommon.Result<StorageItem> renamed = await provider.RenameAsync(GetString(root, "nodeId"), GetString(root, "newName"), cancellationToken).ConfigureAwait(false);
        return renamed.IsSuccess
            ? PCommon.Result.Ok()
            : PCommon.Result.Fail(ToPersistence(renamed.Failure!));
    }

    private static async Task<PCommon.Result> ReplayMoveAsync(IStorageProvider provider, JsonElement root, CancellationToken cancellationToken)
    {
        SCommon.Result<StorageItem> moved = await provider.MoveAsync(GetString(root, "nodeId"), GetString(root, "newParentId"), cancellationToken).ConfigureAwait(false);
        return moved.IsSuccess
            ? PCommon.Result.Ok()
            : PCommon.Result.Fail(ToPersistence(moved.Failure!));
    }

    private static async Task<PCommon.Result> ReplayCopyAsync(IStorageProvider provider, JsonElement root, CancellationToken cancellationToken)
    {
        string name = GetString(root, "newName");
        SCommon.Result<StorageItem> copied = await provider.CopyAsync(GetString(root, "nodeId"), GetString(root, "newParentId"), string.IsNullOrEmpty(name) ? null : name, cancellationToken).ConfigureAwait(false);
        return copied.IsSuccess
            ? PCommon.Result.Ok()
            : PCommon.Result.Fail(ToPersistence(copied.Failure!));
    }

    private static async Task<PCommon.Result> ReplayTrashAsync(IStorageProvider provider, bool trash, JsonElement root, CancellationToken cancellationToken)
    {
        string nodeId = GetString(root, "nodeId");
        SCommon.Result<StorageItem> done = trash
            ? await provider.TrashAsync(nodeId, cancellationToken).ConfigureAwait(false)
            : await provider.RestoreAsync(nodeId, cancellationToken).ConfigureAwait(false);
        return done.IsSuccess
            ? PCommon.Result.Ok()
            : PCommon.Result.Fail(ToPersistence(done.Failure!));
    }

    private static async Task<PCommon.Result> ReplayDeleteAsync(IStorageProvider provider, JsonElement root, CancellationToken cancellationToken)
    {
        SCommon.Result deleted = await provider.DeletePermanentAsync(GetString(root, "nodeId"), cancellationToken).ConfigureAwait(false);
        return deleted.IsSuccess
            ? PCommon.Result.Ok()
            : PCommon.Result.Fail(ToPersistence(deleted.Failure!));
    }

    private static async Task<PCommon.Result> ReplayMetadataAsync(IStorageProvider provider, JsonElement root, CancellationToken cancellationToken)
    {
        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("appProperties", out JsonElement element) && element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in element.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    props[p.Name] = p.Value.GetString() ?? string.Empty;
                }
            }
        }

        string description = GetString(root, "description");
        SCommon.Result<StorageItem> set = await provider.SetMetadataAsync(
            GetString(root, "nodeId"),
            string.IsNullOrEmpty(description) ? null : description,
            props.Count == 0 ? null : props,
            cancellationToken).ConfigureAwait(false);
        return set.IsSuccess
            ? PCommon.Result.Ok()
            : PCommon.Result.Fail(ToPersistence(set.Failure!));
    }
}
