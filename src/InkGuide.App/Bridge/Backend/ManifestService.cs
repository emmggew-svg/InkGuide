using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.Common;
using InkGuide.Infrastructure.Storage.GoogleDrive;
using Microsoft.Extensions.Logging;

using InkGuide.App.Hosting;
namespace InkGuide.App.Bridge.Backend;

/// <summary>Portable repo manifest DTOs (stored as entries in .inkguide/*.json).</summary>
public sealed record RepoManifest(string Id, string Name, string Kind, string CreatedAt);

/// <summary>Portable tag definition (mirrors TagEntity).</summary>
public sealed record ManifestTagDef(
    string Id,
    string Name,
    string? Namespace,
    string? Description,
    string Origin,
    string? ColorToken,
    bool IsSystem);

/// <summary>Portable project entry (mirrors projects.json rows).</summary>
public sealed record ManifestProject(
    string Id,
    string Path,
    string Name,
    string ProjectType,
    IReadOnlyList<string> Tags,
    bool Detected,
    bool Confirmed);

/// <summary>
/// Typed access to portable manifests (repo.json, tags.json, projects.json,
/// snapshots.json) over any storage provider via debounced DriveManifestStore.
/// One store per provider scope; flush explicitly on shutdown and in tests.
/// </summary>
public sealed class ManifestService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DriveManifestOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ManifestService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, DriveManifestStore> _stores = new(StringComparer.Ordinal);
    private bool _disposed;

    public ManifestService(
        DriveManifestOptions options,
        TimeProvider clock,
        ILogger<ManifestService> logger,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _options = options;
        _clock = clock;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public DriveManifestStore ForProvider(IStorageProvider provider, string scopeKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(scopeKey);
        return _stores.GetOrAdd(scopeKey, _ => new DriveManifestStore(
            provider,
            _options,
            _clock,
            _loggerFactory.CreateLogger<DriveManifestStore>()));
    }

    public async Task<Result> FlushAllAsync(CancellationToken cancellationToken)
    {
        foreach (DriveManifestStore store in _stores.Values.ToArray())
        {
            Result flushed = await store.FlushAllAsync(cancellationToken).ConfigureAwait(false);
            if (!flushed.IsSuccess)
            {
                return flushed;
            }
        }

        return Result.Ok();
    }

    public async Task<Result<RepoManifest?>> GetRepoAsync(IStorageProvider provider, string scopeKey, string repoFolderId, CancellationToken cancellationToken)
    {
        Result<ManifestDocument> read = await ReadAsync(provider, scopeKey, repoFolderId, "repo.json", cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess || read.Value is null)
        {
            return Result.Fail<RepoManifest?>(read.Failure!);
        }

        if (read.Value.Entries.TryGetValue("repo", out ManifestEntry? entry))
        {
            try
            {
                RepoManifest? repo = JsonSerializer.Deserialize<RepoManifest>(entry.Json, JsonOptions);
                return Result.Ok<RepoManifest?>(repo);
            }
            catch (JsonException ex)
            {
                return Result.Fail<RepoManifest?>(OperationError.Provider("repo.json is corrupt: " + ex.Message));
            }
        }

        return Result.Ok<RepoManifest?>(null);
    }

    public async Task<Result> SaveRepoAsync(IStorageProvider provider, string scopeKey, string repoFolderId, RepoManifest repo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repo);
        DriveManifestStore store = ForProvider(provider, scopeKey);
        Result scheduled = store.ScheduleWrite(repoFolderId, "repo.json", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["repo"] = JsonSerializer.Serialize(repo, JsonOptions),
        });
        if (!scheduled.IsSuccess)
        {
            return scheduled;
        }

        return await store.FlushAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<ManifestTagDef>>> ListTagDefsAsync(IStorageProvider provider, string scopeKey, string repoFolderId, CancellationToken cancellationToken)
    {
        Result<ManifestDocument> read = await ReadAsync(provider, scopeKey, repoFolderId, "tags.json", cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess || read.Value is null)
        {
            return Result.Fail<IReadOnlyList<ManifestTagDef>>(read.Failure!);
        }

        var defs = new List<ManifestTagDef>();
        foreach (KeyValuePair<string, ManifestEntry> kv in read.Value.Entries)
        {
            if (!kv.Key.StartsWith("def:", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                ManifestTagDef? def = JsonSerializer.Deserialize<ManifestTagDef>(kv.Value.Json, JsonOptions);
                if (def is not null)
                {
                    defs.Add(def);
                }
            }
            catch (JsonException ex)
            {
                AppLogMessages.BackendNote(_logger, "Skipping corrupt tag definition " + kv.Key + ": " + ex.Message);
            }
        }

        return Result.Ok<IReadOnlyList<ManifestTagDef>>(defs);
    }

    public async Task<Result> SaveTagDefAsync(IStorageProvider provider, string scopeKey, string repoFolderId, ManifestTagDef def, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(def);
        return await WriteEntriesAsync(provider, scopeKey, repoFolderId, "tags.json",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["def:" + def.Id] = JsonSerializer.Serialize(def, JsonOptions),
            },
            Array.Empty<string>(),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> AssignTagAsync(IStorageProvider provider, string scopeKey, string repoFolderId, string nodeId, string tagId, CancellationToken cancellationToken)
    {
        return await WriteEntriesAsync(provider, scopeKey, repoFolderId, "tags.json",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["assign:" + nodeId + ":" + tagId] = "1",
            },
            Array.Empty<string>(),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> UnassignTagAsync(IStorageProvider provider, string scopeKey, string repoFolderId, string nodeId, string tagId, CancellationToken cancellationToken)
    {
        return await WriteEntriesAsync(provider, scopeKey, repoFolderId, "tags.json",
            new Dictionary<string, string>(StringComparer.Ordinal),
            new[] { "assign:" + nodeId + ":" + tagId },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> SuppressTagAsync(IStorageProvider provider, string scopeKey, string repoFolderId, string tagName, string? nodeId, CancellationToken cancellationToken)
    {
        return await WriteEntriesAsync(provider, scopeKey, repoFolderId, "tags.json",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["suppress:" + tagName] = nodeId ?? "*",
            },
            Array.Empty<string>(),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<ManifestProject>>> ListProjectsAsync(IStorageProvider provider, string scopeKey, string repoFolderId, CancellationToken cancellationToken)
    {
        Result<ManifestDocument> read = await ReadAsync(provider, scopeKey, repoFolderId, "projects.json", cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess || read.Value is null)
        {
            return Result.Fail<IReadOnlyList<ManifestProject>>(read.Failure!);
        }

        var projects = new List<ManifestProject>();
        foreach (KeyValuePair<string, ManifestEntry> kv in read.Value.Entries)
        {
            if (!kv.Key.StartsWith("project:", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                ManifestProject? project = JsonSerializer.Deserialize<ManifestProject>(kv.Value.Json, JsonOptions);
                if (project is not null)
                {
                    projects.Add(project);
                }
            }
            catch (JsonException ex)
            {
                AppLogMessages.BackendNote(_logger, "Skipping corrupt project entry " + kv.Key + ": " + ex.Message);
            }
        }

        return Result.Ok<IReadOnlyList<ManifestProject>>(projects);
    }

    public async Task<Result> SaveProjectAsync(IStorageProvider provider, string scopeKey, string repoFolderId, ManifestProject project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        return await WriteEntriesAsync(provider, scopeKey, repoFolderId, "projects.json",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["project:" + project.Id] = JsonSerializer.Serialize(project, JsonOptions),
            },
            Array.Empty<string>(),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> RemoveProjectAsync(IStorageProvider provider, string scopeKey, string repoFolderId, string projectId, CancellationToken cancellationToken)
    {
        return await WriteEntriesAsync(provider, scopeKey, repoFolderId, "projects.json",
            new Dictionary<string, string>(StringComparer.Ordinal),
            new[] { "project:" + projectId },
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (DriveManifestStore store in _stores.Values)
        {
            store.Dispose();
        }

        _stores.Clear();
        GC.SuppressFinalize(this);
    }

    private async Task<Result<ManifestDocument>> ReadAsync(IStorageProvider provider, string scopeKey, string repoFolderId, string fileName, CancellationToken cancellationToken)
    {
        DriveManifestStore store = ForProvider(provider, scopeKey);
        return await store.ReadAsync(repoFolderId, fileName, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> WriteEntriesAsync(
        IStorageProvider provider,
        string scopeKey,
        string repoFolderId,
        string fileName,
        Dictionary<string, string> writes,
        string[] removes,
        CancellationToken cancellationToken)
    {
        DriveManifestStore store = ForProvider(provider, scopeKey);
        if (writes.Count > 0)
        {
            Result scheduled = store.ScheduleWrite(repoFolderId, fileName, writes);
            if (!scheduled.IsSuccess)
            {
                return scheduled;
            }
        }

        if (removes.Length > 0)
        {
            Result removed = store.ScheduleRemove(repoFolderId, fileName, removes);
            if (!removed.IsSuccess)
            {
                return removed;
            }
        }

        // Immediate flush makes unassign/remove deterministic for callers and
        // tests; interactive debouncing still coalesces bursts between flushes.
        return await store.FlushAllAsync(cancellationToken).ConfigureAwait(false);
    }
}
