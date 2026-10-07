using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.LocalFolder;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InkGuide.IntegrationTests;

/// <summary>Provider contract tests against LocalFolder (real FS, temp dirs).</summary>
public sealed class LocalFolderProviderTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFolderProvider _provider;
    private bool _disposed;

    public LocalFolderProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "inkguide-lf-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_root);
        var options = new LocalFolderOptions { RootDirectory = _root, Enabled = true, CollisionPolicy = NameCollisionPolicy.Block };
        _provider = new LocalFolderProvider(options, TimeProvider.System, NullLogger<LocalFolderProvider>.Instance);
    }

    [Fact]
    public async Task RoundtripCreateUploadListRenameMoveTrashRestoreAsync()
    {
        CancellationToken ct = CancellationToken.None;

        var caps = await _provider.GetCapabilitiesAsync(ct);
        Assert.True(caps.IsSuccess);
        Assert.True(caps.Value!.SupportsRevisions);

        var root = await _provider.GetRootAsync(ct);
        Assert.True(root.IsSuccess);

        var folder = await _provider.CreateFolderAsync(LocalFolderProvider.RootId, "Docs", ct);
        Assert.True(folder.IsSuccess);
        Assert.False(string.IsNullOrEmpty(folder.Value!.Id));

        byte[] bytes = Encoding.UTF8.GetBytes("hello inkguide");
        await using (var ms = new MemoryStream(bytes, writable: false))
        {
            var up = await _provider.UploadAsync(folder.Value.Id, "note.txt", ms, "text/plain", "op-1", null, ct);
            Assert.True(up.IsSuccess, up.Failure is null ? "upload failed without failure" : up.Failure.Code + ": " + up.Failure.Message);
            Assert.Equal("op-1", up.Value!.AppProperties["inkguide.op"]);

            // Idempotent replay adopts the same node instead of duplicating.
            await using var ms2 = new MemoryStream(bytes, writable: false);
            var replay = await _provider.UploadAsync(folder.Value.Id, "note.txt", ms2, "text/plain", "op-1", null, ct);
            Assert.True(replay.IsSuccess);
            Assert.Equal(up.Value.Id, replay.Value!.Id);
        }

        var page = await _provider.ListChildrenAsync(folder.Value.Id, null, 200, ct);
        Assert.True(page.IsSuccess);
        Assert.Single(page.Value!.Items);

        string fileId = page.Value.Items[0].Id;
        string v0 = page.Value.Items[0].Version ?? "1";

        // Conflict-safe update: wrong base version must not overwrite.
        await using (var bad = new MemoryStream(Encoding.UTF8.GetBytes("evil"), writable: false))
        {
            var conflict = await _provider.UpdateContentAsync(fileId, bad, "9999", null, null, ct);
            Assert.False(conflict.IsSuccess);
        }

        await using (var good = new MemoryStream(Encoding.UTF8.GetBytes("v2"), writable: false))
        {
            var updated = await _provider.UpdateContentAsync(fileId, good, v0, null, null, ct);
            Assert.True(updated.IsSuccess);
        }

        var revs = await _provider.ListRevisionsAsync(fileId, ct);
        Assert.True(revs.IsSuccess);
        Assert.NotEmpty(revs.Value!);

        var renamed = await _provider.RenameAsync(fileId, "renamed.txt", ct);
        Assert.True(renamed.IsSuccess);
        Assert.Equal("renamed.txt", renamed.Value!.Name);

        var trashed = await _provider.TrashAsync(fileId, ct);
        Assert.True(trashed.IsSuccess);
        Assert.True(trashed.Value!.Trashed);

        var restored = await _provider.RestoreAsync(fileId, ct);
        Assert.True(restored.IsSuccess);
        Assert.False(restored.Value!.Trashed);
    }

    [Fact]
    public async Task CollisionBlockPreventsDuplicateNamesAsync()
    {
        CancellationToken ct = CancellationToken.None;
        var a = await _provider.CreateFolderAsync(LocalFolderProvider.RootId, "Dup", ct);
        Assert.True(a.IsSuccess, a.Failure is null ? "first create failed" : a.Failure.Code + ": " + a.Failure.Message);
        var b = await _provider.CreateFolderAsync(LocalFolderProvider.RootId, "dup", ct);
        Assert.False(b.IsSuccess, "duplicate name should be blocked but succeeded with id " + (b.Value is null ? "null" : b.Value.Id));
    }

    [Fact]
    public async Task ChangeFeedTokenRoundtripAndBadTokenReindexesAsync()
    {
        CancellationToken ct = CancellationToken.None;
        var token = await _provider.GetStartChangeTokenAsync(ct);
        Assert.True(token.IsSuccess);

        _ = await _provider.CreateFolderAsync(LocalFolderProvider.RootId, "Feed", ct);
        var changes = await _provider.GetChangesAsync(token.Value!, ct);
        Assert.True(changes.IsSuccess);
        Assert.NotEmpty(changes.Value!.Changes);

        var bad = await _provider.GetChangesAsync("not-a-number", ct);
        Assert.True(bad.IsSuccess);
        Assert.True(bad.Value!.MustReindex);
    }

    [Fact]
    public async Task AdversarialNamesAreSanitizedNotRejectedWithCrashAsync()
    {
        CancellationToken ct = CancellationToken.None;
        var evil = await _provider.CreateFolderAsync(LocalFolderProvider.RootId, "CON ", ct);
        Assert.True(evil.IsSuccess);
        Assert.DoesNotContain(".", evil.Value!.Name.TrimEnd());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _provider.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
