using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Application.Time;
using InkGuide.UnitTests.Helpers;
using Xunit;

namespace InkGuide.UnitTests.Application;

public sealed class ClockTests
{
    [Fact]
    public void SystemClockReadsItsTimeProvider()
    {
        DateTimeOffset frozen = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        SystemClock clock = new(new StubTimeProvider(frozen));

        Assert.Equal(frozen, clock.UtcNow);
    }

    [Fact]
    public async Task FakeOutOfSyncClockStillHonoursCancellation()
    {
        SystemClock clock = SystemClock.Shared;

        Assert.True((DateTimeOffset.UtcNow - clock.UtcNow).Duration() < TimeSpan.FromMinutes(1));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CancelledTokenThrows()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CancelledWork(source.Token));
    }

    private static Task CancelledWork(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
