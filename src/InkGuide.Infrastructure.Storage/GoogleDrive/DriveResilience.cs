using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Storage.Logging;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace InkGuide.Infrastructure.Storage.GoogleDrive;

/// <summary>Options for Drive request resilience.</summary>
public sealed class DriveResilienceOptions
{
    /// <summary>Global request concurrency limiter (default 4).</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Max retry attempts for transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>Circuit-break duration after sustained failure.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Polly retry (429, rate-limit 403s, 5xx) with backoff+jitter and Retry-After,
/// global concurrency limiter, and a circuit-breaker that surfaces OFFLINE state.
/// </summary>
public sealed class DriveResilience : IDisposable
{
    private static readonly string[] RateLimitReasons = new[] { "rateLimitExceeded", "userRateLimitExceeded", "backendError" };

    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly SemaphoreSlim _concurrency;
    private readonly ILogger _logger;
    private bool _disposed;

    public DriveResilience(DriveResilienceOptions options, ILogger<DriveResilience> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _concurrency = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency), Math.Max(1, options.MaxConcurrency));
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = Math.Max(1, options.MaxRetryAttempts),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => IsTransientAsync(args.Outcome),
                DelayGenerator = args => GetRetryAfterAsync(args.Outcome),
                OnRetry = args =>
                {
                    HttpResponseMessage? response = args.Outcome.Result;
                    StorageLog.DriveRequestFailed(_logger, args.AttemptNumber, response?.RequestMessage?.Method.Method ?? "?", response?.RequestMessage?.RequestUri?.AbsolutePath ?? "?", (int)(response?.StatusCode ?? 0));
                    return ValueTask.CompletedTask;
                },
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                MinimumThroughput = 5,
                FailureRatio = 0.8,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = options.BreakDuration,
                ShouldHandle = args => IsTransientAsync(args.Outcome),
                OnOpened = _ =>
                {
                    StorageLog.CircuitOpened(_logger);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    StorageLog.CircuitClosed(_logger);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>Sends one HTTP call through concurrency gate + resilience pipeline.</summary>
    public async Task<HttpResponseMessage> SendAsync(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _pipeline.ExecuteAsync(async token => await send(token).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _concurrency.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async ValueTask<bool> IsTransientAsync(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException || outcome.Exception is TaskCanceledException)
        {
            return true;
        }

        HttpResponseMessage? response = outcome.Result;
        if (response is null)
        {
            return false;
        }

        int status = (int)response.StatusCode;
        if (status == 429 || status == 408 || status >= 500)
        {
            return true;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            try
            {
                string body = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                return RateLimitReasons.Any(r => body.Contains(r, StringComparison.Ordinal));
            }
            catch (HttpRequestException)
            {
                return true;
            }
            catch (TaskCanceledException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private static ValueTask<TimeSpan?> GetRetryAfterAsync(Outcome<HttpResponseMessage> outcome)
    {
        HttpResponseMessage? response = outcome.Result;
        if (response?.Headers.RetryAfter is null)
        {
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        }

        if (response.Headers.RetryAfter.Delta.HasValue)
        {
            return new ValueTask<TimeSpan?>(response.Headers.RetryAfter.Delta);
        }

        if (response.Headers.RetryAfter.Date.HasValue)
        {
            TimeSpan delta = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
            return new ValueTask<TimeSpan?>(delta > TimeSpan.Zero ? delta : TimeSpan.Zero);
        }

        return new ValueTask<TimeSpan?>((TimeSpan?)null);
    }
}
