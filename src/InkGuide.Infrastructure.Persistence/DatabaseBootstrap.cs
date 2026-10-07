using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Entities;
using InkGuide.Infrastructure.Persistence.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Persistence;

/// <summary>Creates/migrates the database, enables WAL, and provisions the FTS5 table.</summary>
public sealed class DatabaseBootstrap
{
    public const string FtsTableName = "node_fts";

    private readonly IDbContextFactory<InkGuideDbContext> _factory;
    private readonly PersistenceOptions _options;
    private readonly ILogger<DatabaseBootstrap> _logger;

    public DatabaseBootstrap(IDbContextFactory<InkGuideDbContext> factory, PersistenceOptions options, ILogger<DatabaseBootstrap> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    /// <summary>Ensures schema, WAL mode, FTS table; drops and recreates on version mismatch.</summary>
    /// <returns>True when FTS5 search is available.</returns>
    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string? connectionString = db.Database.GetConnectionString();
        if (!string.IsNullOrEmpty(connectionString))
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var wal = connection.CreateCommand())
            {
                wal.CommandText = "PRAGMA journal_mode=WAL;";
                _ = await wal.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        SchemaVersionEntity? marker = await db.SchemaVersions.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (marker is not null && marker.Version != _options.SchemaVersion)
        {
            PersistenceLog.SchemaMismatch(_logger, marker.Version, _options.SchemaVersion);
            await db.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            marker = null;
        }

        if (marker is null)
        {
            db.SchemaVersions.Add(new SchemaVersionEntity { Version = _options.SchemaVersion, AppliedAtUtc = DateTimeOffset.UtcNow });
            _ = await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_options.ForceLikeFallback)
        {
            PersistenceLog.FtsDisabled(_logger);
            return false;
        }

        try
        {
            string sql = $"CREATE VIRTUAL TABLE IF NOT EXISTS {FtsTableName} USING fts5(nodeId UNINDEXED, name, summary, tags, path, content);";
            _ = await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException ex)
        {
            PersistenceLog.FtsUnavailable(_logger, ex);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            PersistenceLog.FtsUnavailable(_logger, ex);
            return false;
        }
    }
}
