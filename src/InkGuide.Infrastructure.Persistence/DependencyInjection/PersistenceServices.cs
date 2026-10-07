using System;
using InkGuide.Infrastructure.Persistence.Outbox;
using InkGuide.Infrastructure.Persistence.Repositories;
using InkGuide.Infrastructure.Persistence.Search;
using InkGuide.Infrastructure.Persistence.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InkGuide.Infrastructure.Persistence.DependencyInjection;

/// <summary>Composition helpers for the persistence layer.</summary>
public static class PersistenceServices
{
    /// <summary>Adds SQLite persistence (WAL, pooled factory, stores, search, sync).</summary>
    public static IServiceCollection AddInkGuidePersistence(this IServiceCollection services, PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            throw new ArgumentException("DatabasePath must be set.", nameof(options));
        }

        string connectionString = "Data Source=" + options.DatabasePath;
        _ = services.AddPooledDbContextFactory<InkGuideDbContext>(builder => builder.UseSqlite(connectionString));
        _ = services.AddSingleton(options);
        _ = services.AddSingleton<SqliteWriteGate>();
        _ = services.AddSingleton<DatabaseBootstrap>();
        _ = services.AddSingleton<INodeRepository, NodeRepository>();
        _ = services.AddSingleton<IOutboxStore, OutboxStore>();
        _ = services.AddSingleton<ITagStore, TagStore>();
        _ = services.AddSingleton<IMiscStore, MiscStore>();
        _ = services.AddSingleton<NodeSearchService>();
        _ = services.AddSingleton<SyncEngine>();
        _ = services.AddSingleton<RebuildFromDriveService>();
        _ = services.AddSingleton(new SyncPollOptions());
        _ = services.AddHostedService<OutboxWorker>();
        return services;
    }
}
