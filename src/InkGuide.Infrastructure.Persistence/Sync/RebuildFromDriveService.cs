using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Logging;
using InkGuide.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Persistence.Sync;

/// <summary>
/// Rebuilds the disposable local mirror from the provider (Drive is the source of truth).
/// Deleting the database loses nothing except cache and un-synced outbox entries.
/// </summary>
public sealed class RebuildFromDriveService
{
    private readonly INodeRepository _nodes;
    private readonly IOutboxStore _outbox;
    private readonly IMiscStore _misc;
    private readonly PersistenceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<RebuildFromDriveService> _logger;

    public RebuildFromDriveService(INodeRepository nodes, IOutboxStore outbox, IMiscStore misc, PersistenceOptions options, TimeProvider clock, ILogger<RebuildFromDriveService> logger)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(misc);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _nodes = nodes;
        _outbox = outbox;
        _misc = misc;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Verifies that every mirrored node still exists remotely and re-indexes missing subtrees.</summary>
    public async Task<Result<RebuildReport>> RebuildAsync(
        string accountId,
        ISyncSource source,
        ISyncHooks hooks,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(hooks);

        var engine = new SyncEngine(_nodes, _outbox, _misc, _options, _clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncEngine>.Instance);
        PersistenceLog.RebuildStarted(_logger, accountId);
        Result result = await engine.IndexAsync(accountId, source, hooks, progress, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Result.Fail<RebuildReport>(result.Failure!);
        }

        return Result.Ok(new RebuildReport(accountId, _clock.GetUtcNow()));
    }
}

/// <summary>Outcome of a rebuild-from-Drive run.</summary>
public sealed record RebuildReport(string AccountId, DateTimeOffset CompletedAtUtc);
