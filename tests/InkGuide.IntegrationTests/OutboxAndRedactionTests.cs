using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Persistence.Repositories;
using InkGuide.Infrastructure.Platform.Secrets;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InkGuide.IntegrationTests;

/// <summary>Outbox durability (crash survival) + log redaction.</summary>
public sealed class OutboxAndRedactionTests
{
    [Fact]
    public async Task OutboxSurvivesReopenIdempotentEnqueueAsync()
    {
        CancellationToken ct = CancellationToken.None;
        string dbPath = Path.Combine(Path.GetTempPath(), "inkguide-test-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture) + ".db");
        try
        {
            var factory = new TestFactory(dbPath);
            var gate = new SqliteWriteGate();
            var store = new OutboxStore(factory, gate, TimeProvider.System);
            var op = new OutboxOperationEntity { OpId = "op-1", AccountId = "a1", OperationType = "upload", PayloadJson = "{}", State = "Pending", Attempts = 0, NextAttemptAtUtc = DateTimeOffset.UtcNow };
            var enq = await store.EnqueueAsync(op, ct);
            Assert.True(enq.IsSuccess);

            // Simulate crash: new factory over the same file, entry must still be there.
            var factory2 = new TestFactory(dbPath);
            var store2 = new OutboxStore(factory2, new SqliteWriteGate(), TimeProvider.System);
            var due = await store2.DequeueDueAsync(10, DateTimeOffset.UtcNow.AddMinutes(1), ct);
            Assert.True(due.IsSuccess);
            Assert.Single(due.Value!);

            // Idempotent re-enqueue of the same op id is a no-op.
            var again = await store2.EnqueueAsync(new OutboxOperationEntity { OpId = "op-1", AccountId = "a1", OperationType = "upload", PayloadJson = "{}", State = "Pending", Attempts = 0, NextAttemptAtUtc = DateTimeOffset.UtcNow }, ct);
            Assert.True(again.IsSuccess);
            var list = await store2.ListAsync("a1", ct);
            Assert.Single(list.Value!);
        }
        finally
        {
            try { File.Delete(dbPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void RedactorStripsTokensAndSecrets()
    {
        string msg = "token ya29.abc123 and 1//xyz plus Bearer SECRET sk-ant-foo client_secret: hunter2";
        string redacted = LogRedactor.Redact(msg);
        Assert.DoesNotContain("ya29.abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant-foo", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
    }

    private sealed class TestFactory : IDbContextFactory<InkGuideDbContext>
    {
        private readonly DbContextOptions<InkGuideDbContext> _options;

        public TestFactory(string dbPath)
        {
            var builder = new DbContextOptionsBuilder<InkGuideDbContext>();
            _ = builder.UseSqlite("Data Source=" + dbPath);
            _options = builder.Options;
            using var db = new InkGuideDbContext(_options);
            _ = db.Database.EnsureCreated();
        }

        public InkGuideDbContext CreateDbContext() => new(_options);

        public Task<InkGuideDbContext> CreateDbContextAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(CreateDbContext());
        }
    }
}
