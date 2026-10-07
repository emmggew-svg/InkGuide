using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Application.Common;
using InkGuide.Application.Repositories;
using InkGuide.Domain.Common;
using InkGuide.Domain.Entities;
using InkGuide.UnitTests.Fakes;
using Xunit;

namespace InkGuide.UnitTests.Application;

public sealed class NodeRepositoryTests
{
    [Fact]
    public async Task UpsertAndGetRoundTrip()
    {
        InMemoryNodeRepository repository = new();
        Node node = Node("a1", "n1", "notes.txt", parentId: "root");

        Result<Unit, Failure> stored = await repository.UpsertAsync(node, CancellationToken.None);
        Result<Node?, Failure> loaded = await repository.GetAsync("a1", "n1", CancellationToken.None);

        Assert.True(stored.IsSuccess);
        Assert.True(loaded.IsSuccess);
        Assert.Same(node, loaded.Value!);
    }

    [Fact]
    public async Task GetReturnsNullWhenAbsent()
    {
        InMemoryNodeRepository repository = new();

        Result<Node?, Failure> loaded = await repository.GetAsync("a1", "missing", CancellationToken.None);

        Assert.True(loaded.IsSuccess);
        Assert.Null(loaded.Value!);
    }

    [Fact]
    public async Task ListFiltersSortsAndPages()
    {
        InMemoryNodeRepository repository = new();
        await repository.UpsertAsync(Node("a1", "f1", "docs", isFolder: true, parentId: "root"), CancellationToken.None);
        await repository.UpsertAsync(Node("a1", "n1", "b.txt", parentId: "f1"), CancellationToken.None);
        await repository.UpsertAsync(Node("a1", "n2", "a.txt", parentId: "f1"), CancellationToken.None);
        await repository.UpsertAsync(Node("a1", "n3", "trashed.txt", parentId: "f1", trashed: true), CancellationToken.None);
        await repository.UpsertAsync(Node("a2", "n9", "other.txt", parentId: "root"), CancellationToken.None);

        Result<PagedResult<Node>, Failure> page = await repository.ListAsync(
            new NodeFilter("a1", "f1"),
            null,
            1,
            CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Equal(2, page.Value!.TotalCount);
        Assert.Single(page.Value!.Items);
        Assert.Equal("a.txt", page.Value!.Items[0].Name);
        Assert.NotNull(page.Value!.NextCursor);

        Result<PagedResult<Node>, Failure> second = await repository.ListAsync(
            new NodeFilter("a1", "f1"),
            page.Value!.NextCursor,
            10,
            CancellationToken.None);

        Assert.Equal("b.txt", second.Value!.Items[0].Name);
        Assert.Null(second.Value!.NextCursor);
    }

    [Fact]
    public async Task ListHonoursFolderAndTrashFilters()
    {
        InMemoryNodeRepository repository = new();
        await repository.UpsertAsync(Node("a1", "f1", "docs", isFolder: true, parentId: "root"), CancellationToken.None);
        await repository.UpsertAsync(Node("a1", "n1", "gone.txt", parentId: "root", trashed: true), CancellationToken.None);

        Result<PagedResult<Node>, Failure> folders = await repository.ListAsync(
            new NodeFilter("a1", "root", IsFolder: true),
            null,
            10,
            CancellationToken.None);
        Result<PagedResult<Node>, Failure> withTrash = await repository.ListAsync(
            new NodeFilter("a1", "root", IncludeTrashed: true),
            null,
            10,
            CancellationToken.None);

        Assert.Single(folders.Value!.Items);
        Assert.Equal(2, withTrash.Value!.Items.Count);
    }

    [Fact]
    public async Task FindByHashMatchesDuplicatesWithoutDownloads()
    {
        InMemoryNodeRepository repository = new();
        await repository.UpsertAsync(Node("a1", "n1", "a.bin", parentId: "root", md5: "ABC", size: 10), CancellationToken.None);
        await repository.UpsertAsync(Node("a1", "n2", "b.bin", parentId: "root", md5: "abc", size: 10), CancellationToken.None);
        await repository.UpsertAsync(Node("a1", "n3", "c.bin", parentId: "root", md5: "abc", size: 11), CancellationToken.None);

        Result<IReadOnlyList<Node>, Failure> found = await repository.FindByHashAsync("a1", "abc", 10, CancellationToken.None);

        Assert.Equal(2, found.Value!.Count);
    }

    [Fact]
    public async Task DeleteRemovesCachedNodes()
    {
        InMemoryNodeRepository repository = new();
        await repository.UpsertAsync(Node("a1", "n1", "a.txt", parentId: "root"), CancellationToken.None);

        Result<Unit, Failure> deleted = await repository.DeleteAsync("a1", "n1", CancellationToken.None);
        Result<Node?, Failure> loaded = await repository.GetAsync("a1", "n1", CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.Null(loaded.Value!);
    }

    private static Node Node(
        string accountId,
        string id,
        string name,
        string? parentId = null,
        bool isFolder = false,
        bool trashed = false,
        string? md5 = null,
        long size = 0)
    {
        return new Node
        {
            Id = id,
            AccountId = accountId,
            ParentId = parentId,
            Name = name,
            IsFolder = isFolder,
            Trashed = trashed,
            Md5Checksum = md5,
            SizeBytes = size,
            ModifiedTimeUtc = DateTimeOffset.UtcNow,
            CreatedTimeUtc = DateTimeOffset.UtcNow,
            PathText = "/" + name,
        };
    }
}
