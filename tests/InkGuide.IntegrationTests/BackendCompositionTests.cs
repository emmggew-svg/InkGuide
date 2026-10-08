using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.App.Bridge.Backend;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Handlers;
using InkGuide.Bridge.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace InkGuide.IntegrationTests;

/// <summary>
/// Builds the REAL shipped container and sends every bridge method through the
/// REAL dispatcher. Any method that is not wired (unknown handler, missing
/// dependency, unexpected exception) fails this test by construction.
/// </summary>
public sealed class BackendCompositionTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _baseDir;
    private readonly string _localDir;
    private readonly ServiceProvider _provider;
    private readonly BridgeDispatcher _dispatcher;
    private readonly IBridgeBackend _backend;
    private bool _disposed;

    public BackendCompositionTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "ig-comp-" + Guid.NewGuid().ToString("N"));
        _localDir = Path.Combine(Path.GetTempPath(), "ig-complocal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_localDir);

        var services = new ServiceCollection();
        _ = services.AddLogging();
        string configDir = FindConfigDirectory();
        var options = BackendCompositionOptions.ForDirectories(
            Path.Combine(_baseDir, "data"),
            configDir,
            localProviderEnabled: true,
            localProviderDirectory: _localDir);
        var host = new BackendHost { HostVersion = "0.0.0-test" };
        _ = BackendComposition.AddInkGuideBackend(services, options, host);
        _provider = services.BuildServiceProvider();
        _dispatcher = _provider.GetRequiredService<BridgeDispatcher>();
        _backend = _provider.GetRequiredService<IBridgeBackend>();
    }

    [Fact]
    public async Task ContainerResolvesBackendAndDispatcher()
    {
        Assert.NotNull(_backend);
        Assert.NotNull(_dispatcher);
        bool ready = await BackendStartup.EnsureAsync(_provider, CancellationToken.None);
        Assert.True(ready);
        await _backend.EnsureWorkspaceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EveryBridgeMethodIsServed()
    {
        await BackendStartup.EnsureAsync(_provider, CancellationToken.None);

        // Setup shared fixtures through the real backend.
        var workspace = await _backend.EnsureWorkspaceAsync(CancellationToken.None);
        Assert.True(workspace.IsSuccess);
        var repo = await _backend.CreateRepoAsync(new CreateRepoRequest("method-table", "single", null, null), CancellationToken.None);
        Assert.True(repo.IsSuccess);
        string repoId = repo.Value!.Id;
        var folder = await _backend.CreateFolderAsync(workspace.Value!.ProviderFolderId, "sub", null, CancellationToken.None);
        Assert.True(folder.IsSuccess);
        var file = await _backend.CreateFileAsync(new CreateFileRequest(folder.Value!.Id, "note.txt", "text/plain", "hello", null), CancellationToken.None);
        Assert.True(file.IsSuccess);
        var text = await _backend.ReadTextAsync(file.Value!.Id, CancellationToken.None);
        Assert.True(text.IsSuccess);
        var tag = await _backend.CreateTagAsync(new TagDto(string.Empty, "t-table", null, null, "user", null, false, 0), CancellationToken.None);
        Assert.True(tag.IsSuccess);
        var snapshot = await _backend.CreateSnapshotAsync(new CreateSnapshotRequest(repoId, null, "table", "pin", null), CancellationToken.None);
        Assert.True(snapshot.IsSuccess);

        string version = text.Value!.Version;
        string fileId = file.Value.Id;
        string folderId = folder.Value.Id;
        string tagId = tag.Value!.Id;
        string snapshotId = snapshot.Value!.Id;
        string exportPath = Path.Combine(_baseDir, "export.zip");
        string downloadPath = Path.Combine(_baseDir, "download.bin");
        string fixtureDir = Path.Combine(_baseDir, "fixture");
        Directory.CreateDirectory(fixtureDir);
        await File.WriteAllTextAsync(Path.Combine(fixtureDir, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(fixtureDir, "b.txt"), "b");
        List<(string Method, object? Params)> cases = BuildCases(repoId, folderId, fileId, version, tagId, snapshotId, exportPath, downloadPath, fixtureDir);

        var failures = new List<string>();
        foreach ((string method, object? parameters) in cases)
        {
            string? error = await DispatchAsync(method, parameters);
            if (error is not null)
            {
                failures.Add(method + " -> " + error);
            }
        }

        Assert.True(failures.Count == 0, "Unwired methods:\n" + string.Join("\n", failures));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _provider.Dispose();
        try
        {
            Directory.Delete(_baseDir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            Directory.Delete(_localDir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FindConfigDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "InkGuide.sln")))
            {
                return Path.Combine(dir, "config");
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return Path.Combine(AppContext.BaseDirectory, "config");
    }

    private async Task<string?> DispatchAsync(string method, object? parameters)
    {
        string id = Guid.NewGuid().ToString("N");
        string paramsJson = parameters is null ? "null" : JsonSerializer.Serialize(parameters, JsonOptions);
        string envelope = "{\"v\":1,\"id\":\"" + id + "\",\"type\":\"req\",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
        string? response = await _dispatcher.DispatchAsync(envelope, CancellationToken.None);
        if (response is null)
        {
            return "null response";
        }

        BridgeEnvelopeDto? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BridgeEnvelopeDto>(response, JsonOptions);
        }
        catch (JsonException ex)
        {
            return "unparsable response: " + ex.Message;
        }

        if (parsed is null || !string.Equals(parsed.Type, "res", StringComparison.Ordinal))
        {
            return "not a res envelope";
        }

        if (parsed.Error is null)
        {
            return null;
        }

        // Any served method answers with a domain code. INTERNAL-with-unexpected
        // means the dispatcher caught a bug; UNKNOWN means not wired.
        if (string.Equals(parsed.Error.Code, "INTERNAL", StringComparison.Ordinal) ||
            string.Equals(parsed.Error.Code, "UNKNOWN", StringComparison.Ordinal))
        {
            return parsed.Error.Code + ": " + parsed.Error.Message;
        }

        return null;
    }

    private static List<(string Method, object? Params)> BuildCases(
        string repoId, string folderId, string fileId, string version, string tagId, string snapshotId,
        string exportPath, string downloadPath, string fixtureDir)
    {
        return new List<(string, object?)>
        {
            ("app.hello", new { major = 1, minor = 0 }),
            ("app.getInfo", new { }),
            ("app.getUiStatus", new { }),
            ("app.quit", new { }),
            ("app.restart", new { }),
            ("app.openExternalUrl", new { url = "https://example.com" }),
            ("window.setBackground", new { color = "#112233" }),
            ("window.setTitle", new { title = "t" }),
            ("settings.get", new { }),
            ("settings.update", new { values = new Dictionary<string, string>() }),
            ("settings.reset", new { }),
            ("settings.getUiState", new { key = "k" }),
            ("settings.setUiState", new { key = "k", valueJson = "1" }),
            ("accounts.list", new { }),
            ("accounts.connect", new { fullDrive = false }),
            ("accounts.cancelConnect", new { }),
            ("accounts.disconnect", new { accountId = "local" }),
            ("accounts.setActive", new { accountId = "local" }),
            ("accounts.importOAuthClient", new { clientJson = "{}" }),
            ("accounts.getOAuthStatus", new { }),
            ("workspace.get", new { }),
            ("workspace.ensure", new { }),
            ("workspace.getStats", new { }),
            ("repos.list", new { }),
            ("repos.create", new { name = "r-table-2", kind = "single" }),
            ("repos.rename", new { id = repoId, name = "r-table-renamed", idempotencyKey = (string?)null }),
            ("repos.delete", new { id = repoId }),
            ("repos.get", new { id = repoId }),
            ("repos.getOverview", new { id = repoId }),
            ("repos.exportZip", new { id = repoId, destinationPath = exportPath }),
            ("repos.adoptExistingFolder", new { providerFolderId = "x", name = "y" }),
            ("projects.list", new { repositoryId = repoId }),
            ("projects.get", new { id = "missing" }),
            ("projects.confirm", new { id = "missing" }),
            ("projects.update", new { id = "missing", idempotencyKey = (string?)null }),
            ("projects.remove", new { id = "missing" }),
            ("projects.move", new { id = "missing", newPath = "a", idempotencyKey = (string?)null }),
            ("nodes.list", new { parentId = folderId, limit = 10 }),
            ("nodes.get", new { id = "missing" }),
            ("nodes.getPath", new { id = "missing" }),
            ("nodes.createFolder", new { parentId = folderId, name = "sub2" }),
            ("nodes.createFile", new { parentId = folderId, name = "b.txt", text = "hi" }),
            ("nodes.rename", new { id = fileId, newName = "note2.txt", idempotencyKey = (string?)null }),
            ("nodes.move", new { id = fileId, newParentId = folderId, idempotencyKey = (string?)null }),
            ("nodes.copy", new { id = fileId, newParentId = folderId, idempotencyKey = (string?)null }),
            ("nodes.trash", new { id = fileId }),
            ("nodes.restore", new { id = fileId }),
            ("nodes.prepareDelete", new { id = fileId }),
            ("nodes.deletePermanent", new { id = fileId, confirmationToken = "bogus" }),
            ("nodes.readText", new { id = fileId }),
            ("nodes.writeText", new { id = fileId, text = "hello2", baseVersion = version }),
            ("nodes.getPreview", new { id = fileId }),
            ("nodes.openExternal", new { id = "missing" }),
            ("nodes.openInBrowser", new { id = "missing" }),
            ("nodes.revisions", new { id = fileId }),
            ("nodes.restoreRevision", new { id = fileId, revisionId = "r1" }),
            ("nodes.setFavorite", new { id = fileId, favorite = true }),
            ("nodes.listFavorites", new { }),
            ("nodes.listTrash", new { cursor = (string?)null, limit = 10 }),
            ("nodes.emptyTrash", new { confirmationToken = "bogus" }),
            ("transfers.uploadPaths", new { parentId = folderId, localPaths = new[] { Path.Combine(fixtureDir, "a.txt") } }),
            ("transfers.download", new { nodeId = fileId, destinationPath = downloadPath }),
            ("transfers.list", new { }),
            ("transfers.pause", new { id = "missing" }),
            ("transfers.resume", new { id = "missing" }),
            ("transfers.cancel", new { id = "missing" }),
            ("transfers.retry", new { id = "missing" }),
            ("transfers.clearFinished", new { }),
            ("import.plan", new { localPath = fixtureDir, repositoryId = repoId }),
            ("import.execute", new { repositoryId = repoId, localPath = fixtureDir }),
            ("import.status", new { id = "missing" }),
            ("insight.get", new { id = "missing" }),
            ("insight.reanalyze", new { id = "missing" }),
            ("insight.setSummary", new { id = "missing", summary = "s" }),
            ("insight.setKind", new { id = "missing", kind = "document-text" }),
            ("insight.bulkReanalyze", new { nodeIds = Array.Empty<string>() }),
            ("insight.getQueueStatus", new { }),
            ("tags.list", new { }),
            ("tags.create", new { id = "", name = "t2", @namespace = (string?)null, description = (string?)null, origin = "user", colorToken = (string?)null, isSystem = false, usageCount = 0 }),
            ("tags.update", new { id = tagId, tag = new { id = tagId, name = "t-table", @namespace = (string?)null, description = (string?)null, origin = "user", colorToken = (string?)null, isSystem = false, usageCount = 0 } }),
            ("tags.merge", new { id = tagId, intoTagId = tagId }),
            ("tags.delete", new { id = "missing" }),
            ("tags.assign", new { tagId = "missing", nodeId = "missing" }),
            ("tags.unassign", new { tagId = "missing", nodeId = "missing" }),
            ("tags.bulkAssign", new { tagId = tagId, nodeIds = Array.Empty<string>() }),
            ("tags.getUsage", new { id = tagId }),
            ("tags.listSuggestions", new { }),
            ("tags.acceptSuggestion", new { id = "bogus" }),
            ("tags.dismissSuggestion", new { id = "bogus" }),
            ("search.query", new { text = "hello", limit = 5 }),
            ("search.suggest", new { prefix = "he", limit = 5 }),
            ("sync.getStatus", new { }),
            ("sync.syncNow", new { }),
            ("sync.pause", new { }),
            ("sync.resume", new { }),
            ("sync.listOutbox", new { }),
            ("sync.retryOutbox", new { id = "missing" }),
            ("sync.discardOutbox", new { opId = "missing" }),
            ("sync.listConflicts", new { }),
            ("sync.getConflict", new { id = "missing" }),
            ("sync.resolveConflict", new { id = "missing", resolution = "keepRemote" }),
            ("snapshots.create", new { repositoryId = repoId, message = "s2", mode = "pin" }),
            ("snapshots.list", new { repositoryId = repoId }),
            ("snapshots.get", new { id = snapshotId }),
            ("snapshots.diff", new { id = snapshotId, otherId = snapshotId }),
            ("snapshots.textDiff", new { snapshotA = snapshotId, snapshotB = snapshotId, path = "note.txt" }),
            ("snapshots.restore", new { id = snapshotId }),
            ("snapshots.delete", new { id = snapshotId }),
            ("activity.list", new { cursor = (string?)null, limit = 10 }),
            ("activity.undo", new { journalId = 999999 }),
            ("organize.listInbox", new { cursor = (string?)null, limit = 10 }),
            ("organize.suggest", new { nodeIds = Array.Empty<string>() }),
            ("organize.apply", new { moves = Array.Empty<object>() }),
            ("organize.getSettings", new { }),
            ("duplicates.scan", new { }),
            ("duplicates.list", new { }),
            ("duplicates.resolve", new { keepNodeId = "missing", trashNodeIds = Array.Empty<string>() }),
            ("external.listSessions", new { }),
            ("external.endSession", new { id = "missing" }),
            ("ai.getStatus", new { }),
            ("ai.setKey", new { apiKey = "test-key" }),
            ("ai.clearKey", new { }),
            ("ai.acknowledgeDataSharing", new { acknowledged = true }),
            ("ai.test", new { }),
            ("ai.getUsage", new { }),
            ("storage.getUsage", new { }),
            ("storage.getCacheStats", new { }),
            ("storage.clearCache", new { }),
            ("rules.getCatalogInfo", new { }),
            ("rules.reload", new { }),
            ("rules.validate", new { catalogJson = "{\"map\":{},\"mimeFallback\":{}}" }),
            ("diagnostics.exportBundle", new { }),
            ("diagnostics.getHealth", new { }),
            ("diagnostics.log", new { level = "info", message = "t" }),
        };
    }

    private sealed record BridgeEnvelopeDto(int V, string Id, string Type, string? Method, JsonElement? Params, JsonElement? Result, BridgeErrorDto? Error);

    private sealed record BridgeErrorDto(string Code, string Message, bool Retryable, JsonElement? Details);
}
