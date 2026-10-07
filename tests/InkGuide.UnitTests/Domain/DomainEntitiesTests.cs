using System;
using InkGuide.Domain.Entities;
using Xunit;

namespace InkGuide.UnitTests.Domain;

public sealed class DomainEntitiesTests
{
    [Fact]
    public void AccountDefaultsToActiveAppFolder()
    {
        Account account = new() { Id = "a1", Email = "user@example.com", DisplayName = "User" };

        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(DriveScopeMode.AppFolderOnly, account.ScopeMode);
        Assert.Null(account.LastSyncAtUtc);
    }

    [Fact]
    public void RepositoryDefaultsToSingleProject()
    {
        Repository repository = new() { Id = "r1", Name = "Site" };

        Assert.Equal(RepositoryKind.SingleProject, repository.Kind);
    }

    [Fact]
    public void ProjectTracksDetectionState()
    {
        Project project = new()
        {
            Id = "p1",
            RepositoryId = "r1",
            RelativePath = "apps/web",
            Name = "web",
            TypeId = "web-nextjs",
            Tags = new[] { "type:web-site" },
            Detected = true,
            Confirmed = false,
        };

        Assert.True(project.Detected);
        Assert.False(project.Confirmed);
        Assert.Single(project.Tags);
    }

    [Fact]
    public void NodeInsightDefaultsToRuleDriven()
    {
        NodeInsight insight = new() { NodeId = "n1", AccountId = "a1", Kind = "pdf", Summary = "A PDF." };

        Assert.Equal(InsightSource.Rule, insight.Source);
        Assert.Equal(SensitivityLevel.None, insight.Sensitivity);
        Assert.Null(insight.Role);
    }

    [Fact]
    public void TagDefaultsToUserOrigin()
    {
        Tag tag = new() { Id = "t1", WorkspaceId = "w1", Name = "review" };

        Assert.Equal(TagOrigin.User, tag.Origin);
        Assert.False(tag.IsSystem);
        Assert.Null(tag.ColorToken);
    }

    [Fact]
    public void OutboxOperationDefaultsToQueued()
    {
        OutboxOperation operation = new() { OpId = "op1", AccountId = "a1", OperationType = "node.rename", PayloadJson = "{}" };

        Assert.Equal(OutboxState.Queued, operation.State);
        Assert.Equal(0, operation.Attempts);
        Assert.Null(operation.NextAttemptAtUtc);
    }

    [Fact]
    public void SnapshotDefaultsToPinMode()
    {
        Snapshot snapshot = new() { Id = "s1", RepositoryId = "r1", Message = "checkpoint" };

        Assert.Equal(SnapshotMode.Pin, snapshot.Mode);
    }

    [Fact]
    public void TransferJobDefaultsToQueuedUpload()
    {
        TransferJob job = new() { Id = "j1", AccountId = "a1" };

        Assert.Equal(TransferDirection.Upload, job.Direction);
        Assert.Equal(TransferState.Queued, job.State);
        Assert.Equal(0, job.BytesTransferred);
    }

    [Fact]
    public void ConflictRecordStartsOpen()
    {
        ConflictRecord conflict = new() { Id = "c1", AccountId = "a1", NodeId = "n1", NodeName = "notes.txt" };

        Assert.Equal(ConflictType.Content, conflict.ConflictType);
        Assert.Null(conflict.ResolvedAtUtc);
        Assert.Null(conflict.Resolution);
    }

    [Fact]
    public void RemainingEntitiesHoldTheirData()
    {
        Workspace workspace = new() { Id = "w1", AccountId = "a1", DriveFolderId = "d1", Name = "InkGuide Workspace" };
        NodeTag nodeTag = new() { TagId = "t1", NodeId = "n1", Origin = TagOrigin.Auto };
        TagSuppression suppression = new() { WorkspaceId = "w1", NormalizedTagName = "TYPE:ADS" };
        Favorite favorite = new() { AccountId = "a1", NodeId = "n1" };
        SnapshotEntry entry = new() { SnapshotId = "s1", NodeId = "n1", Path = "a.txt" };
        JournalEntry journal = new() { Id = "op1", AccountId = "a1", Category = "rename", Description = "renamed", IsUndoable = true };
        AnalysisJob analysis = new() { Id = "j1", AccountId = "a1", NodeId = "n1", Priority = 10 };
        ChangeToken token = new() { AccountId = "a1", Token = "tok" };
        SettingEntry setting = new() { Key = "k", Value = "v" };
        SchemaVersion schema = new() { Version = 3 };

        Assert.Equal("d1", workspace.DriveFolderId);
        Assert.Equal(TagOrigin.Auto, nodeTag.Origin);
        Assert.Null(suppression.NodeId);
        Assert.Equal("n1", favorite.NodeId);
        Assert.False(entry.IsFolder);
        Assert.True(journal.IsUndoable);
        Assert.Equal(AnalysisJobState.Queued, analysis.State);
        Assert.Equal("tok", token.Token);
        Assert.Equal("v", setting.Value);
        Assert.Equal(3, schema.Version);
    }

    [Fact]
    public void NodeHoldsProviderState()
    {
        Node node = new()
        {
            Id = "n1",
            AccountId = "a1",
            Name = "notes.txt",
            SizeBytes = 12,
            ModifiedTimeUtc = DateTimeOffset.UtcNow,
            CreatedTimeUtc = DateTimeOffset.UtcNow,
            PathText = "/notes.txt",
        };

        Assert.False(node.IsFolder);
        Assert.False(node.Trashed);
        Assert.Null(node.ParentId);
        Assert.Equal(12, node.SizeBytes);
    }
}
