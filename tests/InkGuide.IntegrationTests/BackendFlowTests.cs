using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.App.Bridge.Backend;
using InkGuide.Bridge.Contracts;
using InkGuide.Bridge.Services;
using InkGuide.Infrastructure.Persistence;
using InkGuide.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace InkGuide.IntegrationTests;

/// <summary>
/// Full acceptance flow against the REAL container on LocalFolder:
/// repo -> upload site -> insight+web-site tag -> search -> edit ->
/// conflict -> snapshots+diff -> trash/restore -> rename/undo.
/// </summary>
public sealed class BackendFlowTests : IDisposable
{
    private const string Marker = "hello-flow-marker-42";

    private readonly string _baseDir;
    private readonly string _localDir;
    private readonly ServiceProvider _provider;
    private readonly IBridgeBackend _backend;
    private readonly ITestOutputHelper _output;
    private bool _disposed;

    public BackendFlowTests(ITestOutputHelper output)
    {
        _output = output;
        _baseDir = Path.Combine(Path.GetTempPath(), "ig-flow-" + Guid.NewGuid().ToString("N"));
        _localDir = Path.Combine(Path.GetTempPath(), "ig-flowlocal-" + Guid.NewGuid().ToString("N"));
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
        _backend = _provider.GetRequiredService<IBridgeBackend>();
    }

    [Fact]
    public async Task FullFlow()
    {
        await BackendStartup.EnsureAsync(_provider, CancellationToken.None);

        // 1. Repo.
        var workspace = await _backend.EnsureWorkspaceAsync(CancellationToken.None);
        Assert.True(workspace.IsSuccess);
        var repo = await _backend.CreateRepoAsync(new CreateRepoRequest("flow-site", "single", null, null), CancellationToken.None);
        Assert.True(repo.IsSuccess);
        string repoId = repo.Value!.Id;
        string repoFolderId = await RepoFolderIdAsync(repoId);

        // 2. Static site fixture + upload.
        string siteDir = Path.Combine(_baseDir, "site");
        Directory.CreateDirectory(Path.Combine(siteDir, "styles"));
        await File.WriteAllTextAsync(Path.Combine(siteDir, "index.html"), "<html><head><title>Flow</title><link rel=\"stylesheet\" href=\"style.css\"></head><body>" + Marker + "</body></html>");
        await File.WriteAllTextAsync(Path.Combine(siteDir, "style.css"), "body { color: black; }");
        await File.WriteAllTextAsync(Path.Combine(siteDir, "styles", "main.css"), "p { margin: 0; }");
        var uploads = await _backend.UploadPathsAsync(new UploadPathsRequest(repoFolderId, new[] { siteDir }, null), CancellationToken.None);
        Assert.True(uploads.IsSuccess);
        await WaitForTransfersAsync();

        var root = await _backend.ListNodesAsync(new ListNodesRequest(repoFolderId, null, 200, null, null, false), CancellationToken.None);
        Assert.True(root.IsSuccess);
        NodeDto? siteDirNode = root.Value!.Items.FirstOrDefault(n => n.IsFolder);
        Assert.NotNull(siteDirNode);
        var files = await _backend.ListNodesAsync(new ListNodesRequest(siteDirNode.Id, null, 200, null, null, false), CancellationToken.None);
        Assert.True(files.IsSuccess);
        NodeDto? index = files.Value!.Items.FirstOrDefault(n => n.Name == "index.html");
        Assert.NotNull(index);

        // 3. Insight summary exists; the site folder carries type:web-site.
        InsightDto? insight = await WaitForInsightAsync(index.Id);
        Assert.NotNull(insight);
        Assert.False(string.IsNullOrWhiteSpace(insight.Summary));
        List<string> folderTags = await WaitForFolderTagAsync(siteDirNode.Id, "type:web-site");
        Assert.Contains("type:web-site", folderTags);

        // 4. Search finds the marker text.
        await DumpFtsAsync("pre-search");
        var search = await _backend.SearchAsync(new SearchQueryRequest(Marker, null, null, null, null, null, null, null, null, false, null, 50), CancellationToken.None);
        Assert.True(search.IsSuccess);
        Assert.Contains(search.Value!.Items, h => h.NodeId == index.Id);

        // 5. Edit round-trip + stale-write conflict.
        var read1 = await _backend.ReadTextAsync(index.Id, CancellationToken.None);
        Assert.True(read1.IsSuccess);
        string v1 = read1.Value!.Version;
        string updated = read1.Value.Text + "\n<!-- v2 -->";
        var write2 = await _backend.WriteTextAsync(index.Id, new WriteTextRequest(updated, v1, null), CancellationToken.None);
        Assert.True(write2.IsSuccess);
        var read2 = await _backend.ReadTextAsync(index.Id, CancellationToken.None);
        Assert.True(read2.IsSuccess);
        Assert.Contains("v2", read2.Value!.Text);
        var stale = await _backend.WriteTextAsync(index.Id, new WriteTextRequest("stale", v1, null), CancellationToken.None);
        Assert.False(stale.IsSuccess);
        Assert.Equal("CONFLICT_VERSION", stale.Failure!.Code);
        var conflicts = await _backend.ListConflictsAsync(CancellationToken.None);
        Assert.True(conflicts.IsSuccess);
        Assert.NotEmpty(conflicts.Value!);
        var afterConflict = await _backend.ListNodesAsync(new ListNodesRequest(siteDirNode.Id, null, 200, null, null, false), CancellationToken.None);
        Assert.True(afterConflict.IsSuccess);
        Assert.Contains(afterConflict.Value!.Items, n => n.Name.Contains("(conflict", StringComparison.Ordinal));

        // 6. Snapshots + diff + text diff.
        var snap1 = await _backend.CreateSnapshotAsync(new CreateSnapshotRequest(repoId, null, "s1", "pin", null), CancellationToken.None);
        Assert.True(snap1.IsSuccess);
        var write3 = await _backend.WriteTextAsync(index.Id, new WriteTextRequest(updated + "\n<!-- v3 -->", read2.Value.Version, null), CancellationToken.None);
        Assert.True(write3.IsSuccess);
        var snap2 = await _backend.CreateSnapshotAsync(new CreateSnapshotRequest(repoId, null, "s2", "pin", null), CancellationToken.None);
        Assert.True(snap2.IsSuccess);
        var diff = await _backend.DiffSnapshotsAsync(snap1.Value!.Id, snap2.Value!.Id, CancellationToken.None);
        Assert.True(diff.IsSuccess);
        Assert.Contains(diff.Value!.Modified, p => p.EndsWith("index.html", StringComparison.Ordinal));
        var textDiff = await _backend.TextDiffAsync(new TextDiffRequest(snap1.Value.Id, snap2.Value.Id, "site/index.html"), CancellationToken.None);
        Assert.True(textDiff.IsSuccess);
        Assert.Contains(textDiff.Value!.Lines, l => l.StartsWith('+') && l.Contains("v3", StringComparison.Ordinal));

        // 7. Trash + restore.
        var trashed = await _backend.TrashNodeAsync(index.Id, CancellationToken.None);
        Assert.True(trashed.IsSuccess);
        var trash = await _backend.ListTrashAsync(new PagedQuery(null, 200), CancellationToken.None);
        Assert.True(trash.IsSuccess);
        Assert.Contains(trash.Value!.Items, n => n.Id == index.Id);
        var restored = await _backend.RestoreNodeAsync(index.Id, CancellationToken.None);
        Assert.True(restored.IsSuccess);
        var trashAfter = await _backend.ListTrashAsync(new PagedQuery(null, 200), CancellationToken.None);
        Assert.True(trashAfter.IsSuccess);
        Assert.DoesNotContain(trashAfter.Value!.Items, n => n.Id == index.Id);

        // 8. Rename + undo via activity.
        var renamed = await _backend.RenameNodeAsync(index.Id, new RenameNodeRequest("home.html", null), CancellationToken.None);
        Assert.True(renamed.IsSuccess);
        var activity = await _backend.ListActivityAsync(new PagedQuery(null, 50), CancellationToken.None);
        Assert.True(activity.IsSuccess);
        ActivityDto? renameEntry = activity.Value!.Items.FirstOrDefault(a => a.Kind == "nodes.rename");
        Assert.NotNull(renameEntry);
        var undone = await _backend.UndoAsync(new UndoRequest(renameEntry.Id), CancellationToken.None);
        _output.WriteLine("UNDO ok=" + undone.IsSuccess + " code=" + undone.Failure?.Code + " msg=" + undone.Failure?.Message);
        Assert.True(undone.IsSuccess);
        var back = await _backend.GetNodeAsync(index.Id, CancellationToken.None);
        Assert.True(back.IsSuccess);
        Assert.Equal("index.html", back.Value!.Name);
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

    private async Task<string> RepoFolderIdAsync(string repoId)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<InkGuideDbContext>>();
        await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
        RepositoryEntity? repo = await db.Repositories.FindAsync(new object[] { repoId }, CancellationToken.None);
        Assert.NotNull(repo);
        return repo.ProviderFolderId;
    }

    private async Task DumpFtsAsync(string stage)
    {
        try
        {
            var factory = _provider.GetRequiredService<IDbContextFactory<InkGuideDbContext>>();
            await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync(CancellationToken.None);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM node_fts), (SELECT sql FROM sqlite_master WHERE name='node_fts'), (SELECT COUNT(*) FROM Nodes)";
            await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None);
            if (await reader.ReadAsync(CancellationToken.None))
            {
                _output.WriteLine("FTS[" + stage + "] ftsRows=" + reader.GetInt64(0) + " table=" + (reader.IsDBNull(1) ? "MISSING" : "present") + " nodes=" + reader.GetInt64(2));
                if (!reader.IsDBNull(1))
                {
                    _output.WriteLine("FTS-ddl: " + reader.GetString(1));
                }
            }

            await using var matchCmd = conn.CreateCommand();
            matchCmd.CommandText = "SELECT nodeId, name FROM node_fts WHERE node_fts MATCH '\"hello\"* AND \"flow\"* AND \"marker\"* AND \"42\"*'";
            await using var matchReader = await matchCmd.ExecuteReaderAsync(CancellationToken.None);
            while (await matchReader.ReadAsync(CancellationToken.None))
            {
                _output.WriteLine("FTS-hit: " + matchReader.GetString(0) + " " + matchReader.GetString(1));
            }

            _output.WriteLine("FTS-match-done");

            try
            {
                await using var svcCmd = conn.CreateCommand();
                svcCmd.CommandText = "SELECT n.Id FROM Nodes n JOIN node_fts ON node_fts.nodeId = n.Id WHERE n.AccountId = 'local' AND node_fts MATCH '\"hello\"* AND \"flow\"* AND \"marker\"* AND \"42\"*' ORDER BY rank LIMIT 200;";
                await using var svcReader = await svcCmd.ExecuteReaderAsync(CancellationToken.None);
                int svcHits = 0;
                while (await svcReader.ReadAsync(CancellationToken.None))
                {
                    svcHits++;
                    _output.WriteLine("FTS-svc-hit: " + svcReader.GetString(0));
                }

                _output.WriteLine("FTS-svc-done hits=" + svcHits);
            }
            catch (Exception ex)
            {
                _output.WriteLine("FTS-svc FAILED: " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                var searchService = _provider.GetRequiredService<InkGuide.Infrastructure.Persistence.Search.NodeSearchService>();
                var svcResult = await searchService.QueryAsync("local", "hello-flow-marker-42", 50, CancellationToken.None);
                _output.WriteLine("SVC-service ok=" + svcResult.IsSuccess + " hits=" + (svcResult.Value?.Count ?? -1) + " err=" + svcResult.Failure?.Code);
            }
            catch (Exception ex)
            {
                _output.WriteLine("SVC-service THREW: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine("FTS[" + stage + "] probe failed: " + ex.GetType().Name + ": " + ex.Message);
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

    private async Task WaitForTransfersAsync()
    {
        for (int i = 0; i < 240; i++)
        {
            var list = await _backend.ListTransfersAsync(CancellationToken.None);
            Assert.True(list.IsSuccess);
            bool pending = false;
            foreach (var job in list.Value!)
            {
                if (job.State == "Queued" || job.State == "Running")
                {
                    pending = true;
                    break;
                }

                Assert.True(job.State == "Done", "Transfer failed: " + job.Error);
            }

            if (!pending)
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail("Transfers did not finish in time.");
    }

    private async Task<InsightDto?> WaitForInsightAsync(string nodeId)
    {
        for (int i = 0; i < 40; i++)
        {
            var insight = await _backend.GetInsightAsync(nodeId, CancellationToken.None);
            if (insight.IsSuccess && insight.Value is not null && !string.IsNullOrWhiteSpace(insight.Value.Summary))
            {
                return insight.Value;
            }

            await Task.Delay(250);
        }

        return null;
    }

    private async Task<List<string>> WaitForFolderTagAsync(string folderId, string tag)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<InkGuideDbContext>>();
        for (int i = 0; i < 120; i++)
        {
            await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
            List<NodeTagEntity> assigns = await db.NodeTags.AsNoTracking().Where(t => t.NodeId == folderId).ToListAsync(CancellationToken.None);
            var names = new List<string>();
            foreach (NodeTagEntity assign in assigns)
            {
                TagEntity? def = await db.Tags.FindAsync(new object[] { assign.TagId }, CancellationToken.None);
                if (def is not null)
                {
                    names.Add(def.Name);
                }
            }

            if (names.Contains(tag))
            {
                return names;
            }

            await Task.Delay(250);
        }

        return new List<string>();
    }
}
