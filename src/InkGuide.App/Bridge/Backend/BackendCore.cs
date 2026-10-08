using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Bridge.Common;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Services;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Persistence.Repositories;
using InkGuide.Infrastructure.Persistence.Search;
using InkGuide.Infrastructure.Persistence.Sync;
using InkGuide.Infrastructure.Platform.Ai;
using InkGuide.Infrastructure.Platform.Cache;
using InkGuide.Infrastructure.Platform.ExternalEdit;
using InkGuide.Infrastructure.Platform.Secrets;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.GoogleDrive;
using InkGuide.Infrastructure.Storage.LocalFolder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PCommon = global::InkGuide.Infrastructure.Persistence.Common;

namespace InkGuide.App.Bridge.Backend;

/// <summary>
/// IBridgeBackend over real services (composition root wiring).
/// Every method is served: real logic where services exist, real typed states
/// (OAUTH_NOT_CONFIGURED, AI_* …) where owner configuration is missing.
/// Never returns "not served".
/// </summary>
public sealed partial class InkGuideBridgeBackend : IBridgeBackend
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string LocalAccountId = "local";
    private const string InboxFolderName = "Inbox";

    private readonly BackendCompositionOptions _options;
    private readonly BackendHost _host;
    private readonly BackendSettingsService _settings;
    private readonly ManifestService _manifests;
    private readonly SyncCoordinator _sync;
    private readonly SyncEngine _engine;
    private readonly INodeRepository _nodes;
    private readonly NodeSearchService _search;
    private readonly IOutboxStore _outbox;
    private readonly ITagStore _tags;
    private readonly IMiscStore _misc;
    private readonly CatalogHolder _holder;
    private readonly GoogleOAuthService _oauth;
    private readonly IProviderResolver _providers;
    private readonly ISecretStore _secrets;
    private readonly IContentCache _cache;
    private readonly ContentCacheOptions _cacheOptions;
    private readonly ExternalEditService _external;
    private readonly AnthropicDescriptionProvider _ai;
    private readonly AnthropicOptions _aiOptions;
    private readonly IDbContextFactory<InkGuideDbContext> _factory;
    private readonly SqliteWriteGate _gate;
    private readonly TimeProvider _clock;
    private readonly ILogger<InkGuideBridgeBackend> _logger;

    public InkGuideBridgeBackend(
        BackendCompositionOptions options,
        BackendHost host,
        BackendSettingsService settings,
        ManifestService manifests,
        SyncCoordinator sync,
        SyncEngine engine,
        INodeRepository nodes,
        NodeSearchService search,
        IOutboxStore outbox,
        ITagStore tags,
        IMiscStore misc,
        CatalogHolder holder,
        GoogleOAuthService oauth,
        IProviderResolver providers,
        ISecretStore secrets,
        IContentCache cache,
        ContentCacheOptions cacheOptions,
        ExternalEditService external,
        AnthropicDescriptionProvider ai,
        AnthropicOptions aiOptions,
        IDbContextFactory<InkGuideDbContext> factory,
        SqliteWriteGate gate,
        TimeProvider clock,
        ILogger<InkGuideBridgeBackend> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(manifests);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(misc);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(oauth);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(cacheOptions);
        ArgumentNullException.ThrowIfNull(external);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _host = host;
        _settings = settings;
        _manifests = manifests;
        _sync = sync;
        _engine = engine;
        _nodes = nodes;
        _search = search;
        _outbox = outbox;
        _tags = tags;
        _misc = misc;
        _holder = holder;
        _oauth = oauth;
        _providers = providers;
        _secrets = secrets;
        _cache = cache;
        _cacheOptions = cacheOptions;
        _external = external;
        _ai = ai;
        _aiOptions = aiOptions;
        _factory = factory;
        _gate = gate;
        _clock = clock;
        _logger = logger;
    }

    private async Task<string> ActiveAccountAsync(CancellationToken cancellationToken)
    {
        string active = await _settings.GetAsync(BackendSettingsService.Keys.ActiveAccountId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(active))
        {
            return active;
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AccountEntity> accounts = await db.Accounts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        AccountEntity? current = accounts.FirstOrDefault(a => a.IsActive) ?? accounts.FirstOrDefault();
        if (current is not null)
        {
            return current.Id;
        }

        return LocalAccountId;
    }

    private async Task<Result<ResolvedProvider>> ResolveProviderAsync(string accountId, CancellationToken cancellationToken)
    {
        ResolvedProvider? resolved = await _providers.ResolveAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return Result.Fail<ResolvedProvider>(BackendResults.Offline("No storage provider is available. Connect an account or enable the LocalFolder provider."));
        }

        return Result.Ok(resolved);
    }

    private static string ManifestScope(ResolvedProvider resolved) => resolved.ScopeKey;

    private async Task<Result<WorkspaceEntity>> EnsureWorkspaceRowAsync(string accountId, string providerFolderId, string name, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        WorkspaceEntity? existing = await db.Workspaces.FindAsync(new object[] { accountId }, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Ok(existing);
        }

        var row = new WorkspaceEntity
        {
            Id = accountId,
            AccountId = accountId,
            ProviderFolderId = providerFolderId,
            Name = name,
            EnsuredAtUtc = _clock.GetUtcNow(),
        };
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.Workspaces.Add(row);
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return Result.Ok(row);
    }

    private async Task JournalAsync(string accountId, string kind, string? nodeId, string summary, bool reversible, string? beforeJson, string? afterJson, CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await _gate.ExecuteWriteAsync(async ct =>
        {
            db.OperationJournal.Add(new OperationJournalEntity
            {
                AccountId = accountId,
                Kind = kind,
                NodeId = nodeId,
                Summary = summary,
                Reversible = reversible,
                Undone = false,
                BeforeJson = beforeJson,
                AfterJson = afterJson,
                OccurredAtUtc = _clock.GetUtcNow(),
            });
            _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<string>> EnqueueOutboxAsync(string accountId, string operationType, object payload, CancellationToken cancellationToken)
    {
        string opId = "op-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        var entity = new OutboxOperationEntity
        {
            OpId = opId,
            AccountId = accountId,
            OperationType = operationType,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            State = "Pending",
            Attempts = 0,
            NextAttemptAtUtc = _clock.GetUtcNow(),
            CreatedAtUtc = _clock.GetUtcNow(),
            UpdatedAtUtc = _clock.GetUtcNow(),
        };
        PCommon.Result enqueued = await _outbox.EnqueueAsync(entity, cancellationToken).ConfigureAwait(false);
        if (!enqueued.IsSuccess)
        {
            return Result.Fail<string>(BackendResults.ToBridge(enqueued.Failure!));
        }

        return Result.Ok(opId);
    }

    private string StageContent(byte[] bytes, string opId)
    {
        string dir = Path.Combine(_options.DataDirectory, "outbox-staging");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, opId + ".bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string UtcNow() => DateTimeOffset.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
}
