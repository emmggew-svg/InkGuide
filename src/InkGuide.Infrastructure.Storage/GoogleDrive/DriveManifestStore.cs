using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.Common;
using InkGuide.Infrastructure.Storage.Logging;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Storage.GoogleDrive;

/// <summary>Options for manifest storage.</summary>
public sealed class DriveManifestOptions
{
    /// <summary>System folder name inside each repo (configurable constant).</summary>
    public string SystemFolderName { get; set; } = ".inkguide";

    /// <summary>Debounce window for coalescing manifest writes.</summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Manifest schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
}

/// <summary>One manifest entry with a last-writer-wins timestamp.</summary>
public sealed class ManifestEntry
{
    public string Json { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Schema-versioned manifest document.</summary>
public sealed class ManifestDocument
{
    public int Schema { get; set; }

    public Dictionary<string, ManifestEntry> Entries { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Reads/writes .inkguide/repo.json, tags.json, projects.json and snapshots with
/// debounced (2-5 s), coalesced, optimistic-concurrency-guarded writes and
/// per-entry last-writer-wins merge on conflict. Schema-versioned with forward migration.
/// </summary>
public sealed class DriveManifestStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Abstractions.IStorageProvider _provider;
    private readonly DriveManifestOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DriveManifestStore> _logger;
    private readonly ConcurrentDictionary<string, PendingWrite> _pending = new(StringComparer.Ordinal);
    private bool _disposed;

    public DriveManifestStore(Abstractions.IStorageProvider provider, DriveManifestOptions options, TimeProvider clock, ILogger<DriveManifestStore> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _provider = provider;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Reads and merges a manifest document (forward-migrates older schemas).</summary>
    public async Task<Result<ManifestDocument>> ReadAsync(string repoFolderId, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repoFolderId);
        ArgumentNullException.ThrowIfNull(fileName);
        Result<StorageItem> folder = await EnsureSystemFolderAsync(repoFolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess || folder.Value is null)
        {
            return Result.Fail<ManifestDocument>(folder.Failure!);
        }

        Result<StorageItemPage> children = await _provider.ListChildrenAsync(folder.Value.Id, null, 200, cancellationToken).ConfigureAwait(false);
        if (!children.IsSuccess || children.Value is null)
        {
            return Result.Fail<ManifestDocument>(children.Failure!);
        }

        StorageItem? file = children.Value.Items.FirstOrDefault(i => string.Equals(i.Name, fileName, StringComparison.Ordinal));
        if (file is null)
        {
            return Result.Ok(new ManifestDocument { Schema = _options.SchemaVersion });
        }

        await using var buffer = new MemoryStream();
        Result downloaded = await _provider.DownloadAsync(file.Id, buffer, null, cancellationToken).ConfigureAwait(false);
        if (!downloaded.IsSuccess)
        {
            return Result.Fail<ManifestDocument>(downloaded.Failure!);
        }

        try
        {
            buffer.Position = 0;
            ManifestDocument? document = await JsonSerializer.DeserializeAsync<ManifestDocument>(buffer, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return Result.Ok(new ManifestDocument { Schema = _options.SchemaVersion });
            }

            document.Schema = _options.SchemaVersion;
            return Result.Ok(document);
        }
        catch (JsonException ex)
        {
            return Result.Fail<ManifestDocument>(OperationError.Provider("Manifest is corrupt: " + ex.Message));
        }
    }

    /// <summary>Queues a debounced, coalesced manifest write (per-entry LWW merge at flush).</summary>
    public Result ScheduleWrite(string repoFolderId, string fileName, IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(repoFolderId);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(entries);
        string key = repoFolderId + "/" + fileName;
        DateTimeOffset now = _clock.GetUtcNow();
        var merged = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> kv in entries)
        {
            merged[kv.Key] = new ManifestEntry { Json = kv.Value, UpdatedAtUtc = now };
        }

        _pending.AddOrUpdate(
            key,
            _ => new PendingWrite(repoFolderId, fileName, merged, CreateTimer(key)),
            (_, existing) =>
            {
                foreach (KeyValuePair<string, ManifestEntry> kv in merged)
                {
                    existing.Entries[kv.Key] = kv.Value;
                }

                existing.Timer.Change(_options.Debounce, Timeout.InfiniteTimeSpan);
                return existing;
            });
        return Result.Ok();
    }

    /// <summary>Flushes all pending manifest writes immediately (shutdown/test hook).</summary>
    public async Task<Result> FlushAllAsync(CancellationToken cancellationToken)
    {
        foreach (string key in _pending.Keys.ToArray())
        {
            Result flushed = await FlushAsync(key, cancellationToken).ConfigureAwait(false);
            if (!flushed.IsSuccess)
            {
                return flushed;
            }
        }

        return Result.Ok();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (PendingWrite pending in _pending.Values)
        {
            pending.Timer.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<Result> FlushAsync(string key, CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(key, out PendingWrite? pending))
        {
            return Result.Ok();
        }

        pending.Timer.Dispose();
        Result<ManifestDocument> remote = await ReadAsync(pending.RepoFolderId, pending.FileName, cancellationToken).ConfigureAwait(false);
        if (!remote.IsSuccess || remote.Value is null)
        {
            return Result.Fail(remote.Failure!);
        }

        foreach (KeyValuePair<string, ManifestEntry> kv in pending.Entries)
        {
            if (!remote.Value.Entries.TryGetValue(kv.Key, out ManifestEntry? current) || kv.Value.UpdatedAtUtc >= current.UpdatedAtUtc)
            {
                remote.Value.Entries[kv.Key] = kv.Value;
            }
        }

        remote.Value.Schema = _options.SchemaVersion;
        string json = JsonSerializer.Serialize(remote.Value, JsonOptions);
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        Result<StorageItem> folder = await EnsureSystemFolderAsync(pending.RepoFolderId, cancellationToken).ConfigureAwait(false);
        if (!folder.IsSuccess || folder.Value is null)
        {
            return Result.Fail(folder.Failure!);
        }

        Result<StorageItemPage> children = await _provider.ListChildrenAsync(folder.Value.Id, null, 200, cancellationToken).ConfigureAwait(false);
        if (!children.IsSuccess || children.Value is null)
        {
            return Result.Fail(children.Failure!);
        }

        StorageItem? existing = children.Value.Items.FirstOrDefault(i => string.Equals(i.Name, pending.FileName, StringComparison.Ordinal));
        Result<StorageItem> written = existing is null
            ? await _provider.UploadAsync(folder.Value.Id, pending.FileName, stream, "application/json", null, null, cancellationToken).ConfigureAwait(false)
            : await _provider.UpdateContentAsync(existing.Id, stream, existing.Version, null, null, cancellationToken).ConfigureAwait(false);
        if (!written.IsSuccess)
        {
            return Result.Fail(written.Failure!);
        }

        StorageLog.ManifestMerged(_logger, pending.FileName, pending.RepoFolderId, remote.Value.Entries.Count);
        return Result.Ok();
    }

    private async Task<Result<StorageItem>> EnsureSystemFolderAsync(string repoFolderId, CancellationToken cancellationToken)
    {
        Result<StorageItemPage> children = await _provider.ListChildrenAsync(repoFolderId, null, 200, cancellationToken).ConfigureAwait(false);
        if (!children.IsSuccess || children.Value is null)
        {
            return Result.Fail<StorageItem>(children.Failure!);
        }

        StorageItem? folder = children.Value.Items.FirstOrDefault(i => i.IsFolder && string.Equals(i.Name, _options.SystemFolderName, StringComparison.Ordinal));
        if (folder is not null)
        {
            return Result.Ok(folder);
        }

        return await _provider.CreateFolderAsync(repoFolderId, _options.SystemFolderName, cancellationToken).ConfigureAwait(false);
    }

    private Timer CreateTimer(string key)
    {
        return new Timer(
            state =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _ = await FlushAsync((string)state!, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                });
            },
            key,
            _options.Debounce,
            Timeout.InfiniteTimeSpan);
    }

    private sealed class PendingWrite
    {
        public PendingWrite(string repoFolderId, string fileName, Dictionary<string, ManifestEntry> entries, Timer timer)
        {
            RepoFolderId = repoFolderId;
            FileName = fileName;
            Entries = entries;
            Timer = timer;
        }

        public string RepoFolderId { get; }

        public string FileName { get; }

        public Dictionary<string, ManifestEntry> Entries { get; }

        public Timer Timer { get; }
    }
}

/// <summary>Well-known manifest file names.</summary>
public static class ManifestFiles
{
    public const string Repo = "repo.json";
    public const string Tags = "tags.json";
    public const string Projects = "projects.json";
}
