using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Sync;
using InkGuide.Infrastructure.Persistence.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Persistence.Sync;

/// <summary>Poll intervals for incremental sync.</summary>
public sealed class SyncPollOptions
{
    /// <summary>Poll interval while the window is focused (default 30 s).</summary>
    public TimeSpan FocusedInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Poll interval while the window is not focused (default 5 min).</summary>
    public TimeSpan UnfocusedInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Background change-feed poller with focus-aware intervals and burst debounce.</summary>
public sealed class SyncPoller : BackgroundService
{
    private readonly SyncEngine _engine;
    private readonly ISyncSource _source;
    private readonly ISyncHooks _hooks;
    private readonly Func<string> _accountId;
    private readonly Func<bool> _isFocused;
    private readonly Func<bool> _isPaused;
    private readonly SyncPollOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SyncPoller> _logger;
    private DateTimeOffset _lastPoll;

    public SyncPoller(
        SyncEngine engine,
        ISyncSource source,
        ISyncHooks hooks,
        Func<string> accountId,
        Func<bool> isFocused,
        Func<bool> isPaused,
        SyncPollOptions options,
        TimeProvider clock,
        ILogger<SyncPoller> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(isFocused);
        ArgumentNullException.ThrowIfNull(isPaused);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _source = source;
        _hooks = hooks;
        _accountId = accountId;
        _isFocused = isFocused;
        _isPaused = isPaused;
        _options = options;
        _clock = clock;
        _logger = logger;
        _lastPoll = DateTimeOffset.MinValue;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), _clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                if (_isPaused())
                {
                    continue;
                }

                TimeSpan interval = _isFocused() ? _options.FocusedInterval : _options.UnfocusedInterval;
                if (_clock.GetUtcNow() - _lastPoll < interval)
                {
                    continue;
                }

                _lastPoll = _clock.GetUtcNow();
                _ = await _engine.PollOnceAsync(_accountId(), _source, _hooks, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                PersistenceLog.PollTickFailed(_logger, ex);
            }
        }
    }
}
