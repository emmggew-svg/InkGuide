using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;
using InkGuide.Domain.Entities;
using InkGuide.UnitTests.Fakes;
using InkGuide.UnitTests.Helpers;
using Xunit;

namespace InkGuide.UnitTests.Application;

public sealed class OutboxTests
{
    [Fact]
    public async Task EnqueueClaimCompleteCycle()
    {
        InMemoryOutbox outbox = new(TimeProvider.System);
        OutboxOperation operation = Operation("op1");

        Assert.True((await outbox.EnqueueAsync(operation, CancellationToken.None)).IsSuccess);
        Assert.Single((await outbox.ListPendingAsync(10, CancellationToken.None)).Value!);
        Assert.True((await outbox.MarkInProgressAsync("op1", CancellationToken.None)).IsSuccess);
        Assert.Empty((await outbox.ListPendingAsync(10, CancellationToken.None)).Value!);
        Assert.True((await outbox.MarkDoneAsync("op1", CancellationToken.None)).IsSuccess);
        Assert.Equal(OutboxState.Done, (await outbox.GetAsync("op1", CancellationToken.None)).Value!.State);
    }

    [Fact]
    public async Task FailedOperationsRetryAfterBackoff()
    {
        DateTimeOffset now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        InMemoryOutbox outbox = new(new StubTimeProvider(now));
        await outbox.EnqueueAsync(Operation("op1"), CancellationToken.None);

        Assert.True((await outbox.MarkFailedAsync("op1", "boom", now.AddMinutes(5), CancellationToken.None)).IsSuccess);
        Assert.Empty((await outbox.ListPendingAsync(10, CancellationToken.None)).Value!);
    }

    [Fact]
    public async Task FailedOperationBecomesDueAfterBackoffOnSameStore()
    {
        DateTimeOffset now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        StubTimeProvider clock = new(now);
        InMemoryOutbox outbox = new(clock);
        await outbox.EnqueueAsync(Operation("op1"), CancellationToken.None);
        await outbox.MarkFailedAsync("op1", "boom", now.AddMinutes(-1), CancellationToken.None);

        Assert.Single((await outbox.ListPendingAsync(10, CancellationToken.None)).Value!);
        Assert.Equal(1, (await outbox.GetAsync("op1", CancellationToken.None)).Value!.Attempts);
        Assert.Equal("boom", (await outbox.GetAsync("op1", CancellationToken.None)).Value!.LastError);
    }

    [Fact]
    public async Task MissingOperationsReportNotFound()
    {
        InMemoryOutbox outbox = new(TimeProvider.System);

        Assert.Null((await outbox.GetAsync("missing", CancellationToken.None)).Value);
        Assert.Equal(ErrorCodes.NotFound, (await outbox.MarkInProgressAsync("missing", CancellationToken.None)).Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, (await outbox.MarkDoneAsync("missing", CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task DeleteDiscardsOperations()
    {
        InMemoryOutbox outbox = new(TimeProvider.System);
        await outbox.EnqueueAsync(Operation("op1"), CancellationToken.None);

        Assert.True((await outbox.DeleteAsync("op1", CancellationToken.None)).IsSuccess);
        Assert.Empty((await outbox.ListPendingAsync(10, CancellationToken.None)).Value!);
        Assert.True((await outbox.DeleteAsync("op1", CancellationToken.None)).IsSuccess);
    }

    private static OutboxOperation Operation(string opId)
    {
        return new OutboxOperation
        {
            OpId = opId,
            AccountId = "a1",
            OperationType = "node.rename",
            PayloadJson = "{}",
            CreatedAtUtc = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero),
        };
    }
}
