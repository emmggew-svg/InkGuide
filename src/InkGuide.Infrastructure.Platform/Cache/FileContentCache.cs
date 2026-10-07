using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Platform.Common;
using InkGuide.Infrastructure.Platform.Logging;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Platform.Cache;

// Local seam: the canonical IContentCache port is owned by Application (see D-006).

/// <summary>Cache usage statistics.</summary>
public sealed record CacheStats(int FileCount, long TotalBytes, long MaxBytes);

/// <summary>LRU content cache (default 2 GB cap).</summary>
public interface IContentCache
{
    Task<Result> StoreAsync(string key, Stream content, CancellationToken cancellationToken);

    Task<Result<Stream>> OpenReadAsync(string key, CancellationToken cancellationToken);

    bool TryGetLocalPath(string key, out string path);

    Task<Result<CacheStats>> GetStatsAsync(CancellationToken cancellationToken);

    Task<Result> ClearAsync(CancellationToken cancellationToken);
}

/// <summary>Options for the file content cache.</summary>
public sealed class ContentCacheOptions
{
    public string RootDirectory { get; set; } = string.Empty;

    public long MaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;
}

/// <summary>File-system LRU cache with atomic writes and size tracking.</summary>
public sealed class FileContentCache : IContentCache, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ContentCacheOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<FileContentCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public FileContentCache(ContentCacheOptions options, TimeProvider clock, ILogger<FileContentCache> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> StoreAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        string fileName = FileNameFor(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_options.RootDirectory);
            string temp = Path.Combine(_options.RootDirectory, fileName + ".tmp");
            long size;
            await using (FileStream file = new(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                size = file.Length;
            }

            File.Move(temp, Path.Combine(_options.RootDirectory, fileName), overwrite: true);
            Dictionary<string, CacheEntry> index = await LoadIndexInternalAsync(cancellationToken).ConfigureAwait(false);
            index[key] = new CacheEntry { FileName = fileName, Size = size, LastAccessUtc = _clock.GetUtcNow() };
            EvictInternal(index);
            await SaveIndexInternalAsync(index, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Cache store failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Cache store failed: " + ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<Stream>> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, CacheEntry> index = await LoadIndexInternalAsync(cancellationToken).ConfigureAwait(false);
            if (!index.TryGetValue(key, out CacheEntry? entry))
            {
                return Result.Fail<Stream>(OperationError.NotFound($"Cache entry '{key}' was not found."));
            }

            string path = Path.Combine(_options.RootDirectory, entry.FileName);
            if (!File.Exists(path))
            {
                _ = index.Remove(key);
                await SaveIndexInternalAsync(index, cancellationToken).ConfigureAwait(false);
                return Result.Fail<Stream>(OperationError.NotFound($"Cache entry '{key}' was not found."));
            }

            entry.LastAccessUtc = _clock.GetUtcNow();
            await SaveIndexInternalAsync(index, cancellationToken).ConfigureAwait(false);
            FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            return Result.Ok<Stream>(stream);
        }
        catch (IOException ex)
        {
            return Result.Fail<Stream>(OperationError.Provider("Cache read failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<Stream>(OperationError.Provider("Cache read failed: " + ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public bool TryGetLocalPath(string key, out string path)
    {
        ArgumentNullException.ThrowIfNull(key);
        string candidate = Path.Combine(_options.RootDirectory, FileNameFor(key));
        if (File.Exists(candidate))
        {
            path = candidate;
            return true;
        }

        path = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public async Task<Result<CacheStats>> GetStatsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, CacheEntry> index = await LoadIndexInternalAsync(cancellationToken).ConfigureAwait(false);
            long total = index.Values.Sum(e => e.Size);
            return Result.Ok(new CacheStats(index.Count, total, _options.MaxBytes));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result> ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(_options.RootDirectory))
            {
                foreach (string file in Directory.GetFiles(_options.RootDirectory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // Best effort per file.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Best effort per file.
                    }
                }
            }

            await SaveIndexInternalAsync(new Dictionary<string, CacheEntry>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private void EvictInternal(Dictionary<string, CacheEntry> index)
    {
        long total = index.Values.Sum(e => e.Size);
        if (total <= _options.MaxBytes)
        {
            return;
        }

        List<string> lru = index.OrderBy(kv => kv.Value.LastAccessUtc).Select(kv => kv.Key).ToList();
        int removed = 0;
        long freed = 0;
        foreach (string key in lru)
        {
            if (total <= _options.MaxBytes)
            {
                break;
            }

            if (!index.TryGetValue(key, out CacheEntry? entry))
            {
                continue;
            }

            try
            {
                string path = Path.Combine(_options.RootDirectory, entry.FileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            total -= entry.Size;
            freed += entry.Size;
            removed++;
            _ = index.Remove(key);
        }

        PlatformLog.CacheEvicted(_logger, removed, freed);
    }

    private async Task<Dictionary<string, CacheEntry>> LoadIndexInternalAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(_options.RootDirectory, "index.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json, JsonOptions)
                ?? new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }
    }

    private async Task SaveIndexInternalAsync(Dictionary<string, CacheEntry> index, CancellationToken cancellationToken)
    {
        string temp = Path.Combine(_options.RootDirectory, "index.json.tmp");
        string json = JsonSerializer.Serialize(index, JsonOptions);
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, Path.Combine(_options.RootDirectory, "index.json"), overwrite: true);
    }

    private static string FileNameFor(string key)
    {
        bool safe = key.Length > 0 && key.Length <= 128;
        if (safe)
        {
            foreach (char c in key)
            {
                if (!IsSafeChar(c))
                {
                    safe = false;
                    break;
                }
            }
        }

        if (safe)
        {
            return key + ".bin";
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(digest).ToLower(CultureInfo.InvariantCulture) + ".bin";
    }

    private static bool IsSafeChar(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
    }

    private sealed class CacheEntry
    {
        public string FileName { get; set; } = string.Empty;

        public long Size { get; set; }

        public DateTimeOffset LastAccessUtc { get; set; }
    }
}
