using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Logging;
using InkGuide.Infrastructure.Persistence.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Persistence.Outbox;

/// <summary>Background worker that replays durable outbox operations with backoff.</summary>
public sealed class OutboxWorker : BackgroundService
{
    private readonly SyncEngine _engine;
    private readonly IOutboxExecutor _executor;
    private readonly ISyncHooks _hooks;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        SyncEngine engine,
        IOutboxExecutor executor,
        ISyncHooks hooks,
        TimeProvider clock,
        ILogger<OutboxWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _executor = executor;
        _hooks = hooks;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), _clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                _ = await _engine.ReplayOutboxAsync(_executor, _hooks, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                PersistenceLog.OutboxTickFailed(_logger, ex);
            }
        }
    }
}
