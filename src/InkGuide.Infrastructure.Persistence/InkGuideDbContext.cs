using System;
using InkGuide.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InkGuide.Infrastructure.Persistence;

/// <summary>EF Core SQLite database for the disposable local mirror.</summary>
public sealed class InkGuideDbContext : DbContext
{
    public InkGuideDbContext(DbContextOptions<InkGuideDbContext> options)
        : base(options)
    {
    }

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    public DbSet<WorkspaceEntity> Workspaces => Set<WorkspaceEntity>();

    public DbSet<RepositoryEntity> Repositories => Set<RepositoryEntity>();

    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();

    public DbSet<NodeEntity> Nodes => Set<NodeEntity>();

    public DbSet<NodeInsightEntity> NodeInsights => Set<NodeInsightEntity>();

    public DbSet<TagEntity> Tags => Set<TagEntity>();

    public DbSet<NodeTagEntity> NodeTags => Set<NodeTagEntity>();

    public DbSet<TagSuppressionEntity> TagSuppressions => Set<TagSuppressionEntity>();

    public DbSet<FavoriteEntity> Favorites => Set<FavoriteEntity>();

    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();

    public DbSet<SnapshotEntryEntity> SnapshotEntries => Set<SnapshotEntryEntity>();

    public DbSet<OperationJournalEntity> OperationJournal => Set<OperationJournalEntity>();

    public DbSet<OutboxOperationEntity> OutboxOperations => Set<OutboxOperationEntity>();

    public DbSet<ConflictRecordEntity> ConflictRecords => Set<ConflictRecordEntity>();

    public DbSet<TransferJobEntity> TransferJobs => Set<TransferJobEntity>();

    public DbSet<AnalysisJobEntity> AnalysisJobs => Set<AnalysisJobEntity>();

    public DbSet<ChangeTokenEntity> ChangeTokens => Set<ChangeTokenEntity>();

    public DbSet<SettingsKvEntity> SettingsKv => Set<SettingsKvEntity>();

    public DbSet<SchemaVersionEntity> SchemaVersions => Set<SchemaVersionEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AccountEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
        });
        modelBuilder.Entity<WorkspaceEntity>(e =>
        {
            e.HasKey(x => x.Id);
        });
        modelBuilder.Entity<RepositoryEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.Name });
        });
        modelBuilder.Entity<ProjectEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.RepositoryId });
        });
        modelBuilder.Entity<NodeEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(256);
            e.HasIndex(x => new { x.AccountId, x.ParentId, x.IsFolder, x.Name });
            e.HasIndex(x => new { x.AccountId, x.RepoId, x.ProjectId });
            e.HasIndex(x => new { x.AccountId, x.Md5, x.Size });
            e.HasIndex(x => new { x.AccountId, x.Trashed });
        });
        modelBuilder.Entity<NodeInsightEntity>(e =>
        {
            e.HasKey(x => x.NodeId);
        });
        modelBuilder.Entity<TagEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.Name }).IsUnique();
        });
        modelBuilder.Entity<NodeTagEntity>(e =>
        {
            e.HasKey(x => new { x.TagId, x.NodeId });
            e.HasIndex(x => new { x.TagId, x.NodeId });
            e.HasIndex(x => new { x.NodeId, x.TagId });
        });
        modelBuilder.Entity<TagSuppressionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.TagName });
        });
        modelBuilder.Entity<FavoriteEntity>(e =>
        {
            e.HasKey(x => x.NodeId);
        });
        modelBuilder.Entity<SnapshotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.RepositoryId });
        });
        modelBuilder.Entity<SnapshotEntryEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SnapshotId);
        });
        modelBuilder.Entity<OperationJournalEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.OccurredAtUtc });
        });
        modelBuilder.Entity<OutboxOperationEntity>(e =>
        {
            e.HasKey(x => x.OpId);
            e.HasIndex(x => new { x.State, x.NextAttemptAtUtc });
            e.HasIndex(x => x.AccountId);
        });
        modelBuilder.Entity<ConflictRecordEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.Resolution });
        });
        modelBuilder.Entity<TransferJobEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.State });
        });
        modelBuilder.Entity<AnalysisJobEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.State, x.Priority });
        });
        modelBuilder.Entity<ChangeTokenEntity>(e =>
        {
            e.HasKey(x => x.AccountId);
        });
        modelBuilder.Entity<SettingsKvEntity>(e =>
        {
            e.HasKey(x => x.Key);
        });
        modelBuilder.Entity<SchemaVersionEntity>(e =>
        {
            e.HasKey(x => x.Id);
        });
    }
}
