using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.Common;
using InkGuide.Infrastructure.Storage.Logging;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Storage.LocalFolder;

/// <summary>
/// Full <see cref="Abstractions.IStorageProvider"/> contract on a directory.
/// Ids via an index file, revisions via a .revisions side folder, change feed via a journal.
/// </summary>
public sealed class LocalFolderProvider : Abstractions.IStorageProvider, IDisposable
{
    public const string RootId = "lf_root";

    private const string IndexDirName = ".inkguide-index";
    private const string ContentDirName = ".content";
    private const string RevisionsDirName = ".revisions";
    private const int JournalRotateAt = 50000;
    private const int JournalKeep = 10000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly LocalFolderOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<LocalFolderProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public LocalFolderProvider(LocalFolderOptions options, TimeProvider clock, ILogger<LocalFolderProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderId => "localfolder";

    /// <inheritdoc />
    public IReadOnlyList<string> RequestedScopes => Array.Empty<string>();

    /// <summary>Whether this provider is enabled (setting/CLI).</summary>
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.RootDirectory);

    /// <inheritdoc />
    public Task<Result<StorageCapabilities>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        StorageCapabilities capabilities = new(
            SupportsRevisions: true,
            SupportsExport: true,
            SupportsChangeFeed: true,
            SupportsServerSideCopy: true,
            MaxChunkBytes: 8 * 1024 * 1024,
            ChangeFeedResumable: true);
        return Task.FromResult(Result.Ok<StorageCapabilities>(capabilities));
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> GetRootAsync(CancellationToken cancellationToken)
    {
        Result<Index> loaded = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return Result.Fail<StorageItem>(loaded.Failure!);
        }

        string name = Path.GetFileName(_options.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var root = new StorageItem(
            RootId, null, string.IsNullOrEmpty(name) ? "LocalFolder" : name, true, "application/vnd.inkguide.folder",
            0, null, "1", "r1", _clock.GetUtcNow(), _clock.GetUtcNow(), false, false, null,
            new Dictionary<string, string>(), null, true, false, true, true);
        return Result.Ok(root);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItemPage>> ListChildrenAsync(string parentId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentId);
        Result<Index> loaded = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return Result.Fail<StorageItemPage>(loaded.Failure!);
        }

        int take = Math.Clamp(pageSize <= 0 ? 200 : pageSize, 1, 1000);
        int offset = ParseCursor(cursor);
        List<IndexEntry> children = loaded.Value.Items.Values
            .Where(e => string.Equals(e.ParentId, parentId, StringComparison.Ordinal) && !e.Trashed)
            .OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        List<StorageItem> items = children.Skip(offset).Take(take).Select(ToItem).ToList();
        string? next = offset + items.Count < children.Count
            ? (offset + items.Count).ToString(CultureInfo.InvariantCulture)
            : null;
        return Result.Ok(new StorageItemPage(items, next));
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> GetAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        if (string.Equals(itemId, RootId, StringComparison.Ordinal))
        {
            return await GetRootAsync(cancellationToken).ConfigureAwait(false);
        }

        Result<Index> loaded = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return Result.Fail<StorageItem>(loaded.Failure!);
        }

        if (!loaded.Value.Items.TryGetValue(itemId, out IndexEntry? entry))
        {
            return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
        }

        return Result.Ok(ToItem(entry));
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentId);
        ArgumentNullException.ThrowIfNull(name);
        string clean = Sanitize(name);
        if (clean.Length == 0)
        {
            return Result.Fail<StorageItem>(OperationError.Invalid("Folder name is empty after sanitization."));
        }

        return await MutateAsync("create", null, index =>
        {
            Result<IndexEntry?> parent = RequireParent(index, parentId);
            if (!parent.IsSuccess)
            {
                return Result.Fail<StorageItem>(parent.Failure!);
            }

            Result<string> unique = UniqueName(index, parentId, clean, true);
            if (!unique.IsSuccess || unique.Value is null)
            {
                return Result.Fail<StorageItem>(unique.Failure!);
            }

            string id = "lf_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var entry = new IndexEntry
            {
                Id = id,
                ParentId = parentId,
                Name = unique.Value,
                IsFolder = true,
                Version = 1,
            };
            index.Items[id] = entry;
            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> UploadAsync(string parentId, string name, Stream content, string mimeType, string? operationId, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(mimeType);

        // Idempotent replay: adopt an existing child carrying the same operation id.
        if (!string.IsNullOrEmpty(operationId))
        {
            Result<StorageItem?> adopted = await FindByOperationIdAsync(parentId, operationId!, cancellationToken).ConfigureAwait(false);
            if (!adopted.IsSuccess)
            {
                return Result.Fail<StorageItem>(adopted.Failure!);
            }

            if (adopted.Value is not null)
            {
                return Result.Ok(adopted.Value);
            }
        }

        string clean = Sanitize(name);
        if (clean.Length == 0)
        {
            return Result.Fail<StorageItem>(OperationError.Invalid("File name is empty after sanitization."));
        }

        string id = "lf_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        Result write = await WriteContentFileAsync(id, content, progress, cancellationToken).ConfigureAwait(false);
        if (!write.IsSuccess)
        {
            return Result.Fail<StorageItem>(write.Failure!);
        }

        (long size, string md5) = await HashContentFileAsync(id, cancellationToken).ConfigureAwait(false);
        return await MutateAsync("create", id, index =>
        {
            Result<IndexEntry?> parent = RequireParent(index, parentId);
            if (!parent.IsSuccess)
            {
                return Result.Fail<StorageItem>(parent.Failure!);
            }

            Result<string> unique = UniqueName(index, parentId, clean, false);
            if (!unique.IsSuccess || unique.Value is null)
            {
                return Result.Fail<StorageItem>(unique.Failure!);
            }

            var entry = new IndexEntry
            {
                Id = id,
                ParentId = parentId,
                Name = unique.Value,
                IsFolder = false,
                MimeType = mimeType,
                Size = size,
                Md5 = md5,
                Version = 1,
            };
            if (operationId is not null)
            {
                entry.AppProperties["inkguide.op"] = operationId;
            }

            index.Items[id] = entry;
            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> UpdateContentAsync(string itemId, Stream content, string? expectedVersion, string? operationId, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(content);
        Result<StorageItem> current = await GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value is null)
        {
            return Result.Fail<StorageItem>(current.Failure!);
        }

        if (current.Value.IsFolder)
        {
            return Result.Fail<StorageItem>(OperationError.Unsupported("Folders have no content."));
        }

        if (expectedVersion is not null && !string.Equals(expectedVersion, current.Value.Version, StringComparison.Ordinal))
        {
            return Result.Fail<StorageItem>(OperationError.Conflict($"Version moved (base={expectedVersion}, remote={current.Value.Version})."));
        }

        // Preserve the previous bytes as a revision before overwriting.
        await ArchiveRevisionAsync(itemId, current.Value.Version ?? "1", cancellationToken).ConfigureAwait(false);
        Result write = await WriteContentFileAsync(itemId, content, progress, cancellationToken).ConfigureAwait(false);
        if (!write.IsSuccess)
        {
            return Result.Fail<StorageItem>(write.Failure!);
        }

        (long size, string md5) = await HashContentFileAsync(itemId, cancellationToken).ConfigureAwait(false);
        return await MutateAsync("update", itemId, index =>
        {
            if (!index.Items.TryGetValue(itemId, out IndexEntry? entry) || entry.IsFolder)
            {
                return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
            }

            entry.Size = size;
            entry.Md5 = md5;
            entry.Version += 1;
            if (operationId is not null)
            {
                entry.AppProperties["inkguide.op"] = operationId;
            }

            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> DownloadAsync(string itemId, Stream destination, ContentRange? range, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(destination);
        string path = ContentPath(itemId);
        if (!File.Exists(path))
        {
            return Result.Fail(OperationError.NotFound($"Content for '{itemId}' was not found."));
        }

        try
        {
            await using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            if (range is null)
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                return Result.Ok();
            }

            if (range.From < 0 || (range.To.HasValue && range.To < range.From))
            {
                return Result.Fail(OperationError.Invalid("Invalid content range."));
            }

            source.Seek(range.From, SeekOrigin.Begin);
            long remaining = range.To.HasValue ? range.To.Value - range.From + 1 : source.Length - range.From;
            byte[] buffer = new byte[81920];
            while (remaining > 0)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }

            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Download failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Download failed: " + ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task<Result> ExportAsync(string itemId, string targetMimeType, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(targetMimeType);
        ArgumentNullException.ThrowIfNull(destination);
        Result<StorageItem> current = await GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value is null)
        {
            return Result.Fail(current.Failure!);
        }

        if (!string.Equals(current.Value.MimeType, targetMimeType, StringComparison.Ordinal))
        {
            return Result.Fail(OperationError.Unsupported($"LocalFolder export only supports identical mime types ({current.Value.MimeType})."));
        }

        return await DownloadAsync(itemId, destination, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> RenameAsync(string itemId, string newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(newName);
        string clean = Sanitize(newName);
        if (clean.Length == 0)
        {
            return Result.Fail<StorageItem>(OperationError.Invalid("Name is empty after sanitization."));
        }

        return await MutateAsync("rename", itemId, index =>
        {
            if (!index.Items.TryGetValue(itemId, out IndexEntry? entry))
            {
                return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
            }

            Result<string> unique = UniqueName(index, entry.ParentId, clean, entry.IsFolder, itemId);
            if (!unique.IsSuccess || unique.Value is null)
            {
                return Result.Fail<StorageItem>(unique.Failure!);
            }

            entry.Name = unique.Value;
            entry.Version += 1;
            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> MoveAsync(string itemId, string newParentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(newParentId);
        return await MutateAsync("move", itemId, index =>
        {
            if (!index.Items.TryGetValue(itemId, out IndexEntry? entry))
            {
                return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
            }

            Result<IndexEntry?> parent = RequireParent(index, newParentId);
            if (!parent.IsSuccess)
            {
                return Result.Fail<StorageItem>(parent.Failure!);
            }

            if (entry.IsFolder && IsDescendantOf(index, newParentId, itemId))
            {
                return Result.Fail<StorageItem>(OperationError.Invalid("Cannot move a folder into itself."));
            }

            entry.ParentId = newParentId;
            entry.Version += 1;
            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> CopyAsync(string itemId, string newParentId, string? newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(newParentId);
        Result<StorageItem> created = await MutateAsync("create", null, index =>
        {
            if (!index.Items.TryGetValue(itemId, out IndexEntry? source))
            {
                return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
            }

            Result<IndexEntry?> parent = RequireParent(index, newParentId);
            if (!parent.IsSuccess)
            {
                return Result.Fail<StorageItem>(parent.Failure!);
            }

            string baseName = string.IsNullOrEmpty(newName) ? source.Name : Sanitize(newName!);
            Result<string> unique = UniqueName(index, newParentId, baseName, source.IsFolder);
            if (!unique.IsSuccess || unique.Value is null)
            {
                return Result.Fail<StorageItem>(unique.Failure!);
            }

            string copyId = "lf_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var copy = new IndexEntry
            {
                Id = copyId,
                ParentId = newParentId,
                Name = unique.Value,
                IsFolder = source.IsFolder,
                MimeType = source.MimeType,
                Size = source.Size,
                Md5 = source.Md5,
                Version = 1,
                Description = source.Description,
            };
            foreach (KeyValuePair<string, string> appProp in source.AppProperties)
            {
                copy.AppProperties[appProp.Key] = appProp.Value;
            }

            index.Items[copyId] = copy;
            if (!source.IsFolder)
            {
                try
                {
                    File.Copy(ContentPath(itemId), ContentPath(copyId), overwrite: false);
                }
                catch (IOException ex)
                {
                    index.Items.Remove(copyId);
                    return Result.Fail<StorageItem>(OperationError.Provider("Copy failed: " + ex.Message, true));
                }
                catch (UnauthorizedAccessException ex)
                {
                    index.Items.Remove(copyId);
                    return Result.Fail<StorageItem>(OperationError.Provider("Copy failed: " + ex.Message));
                }
            }

            return Result.Ok(ToItem(copy));
        }, cancellationToken).ConfigureAwait(false);

        if (!created.IsSuccess || created.Value is null)
        {
            return created;
        }

        // Deep copy folder children.
        Result<Index> loaded = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.IsSuccess && loaded.Value is not null && created.Value.IsFolder)
        {
            await CopyChildrenRecursiveAsync(itemId, created.Value.Id, cancellationToken).ConfigureAwait(false);
        }

        return created;
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> TrashAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return await MutateAsync("trash", itemId, index =>
        {
            if (!index.Items.TryGetValue(itemId, out IndexEntry? entry))
            {
                return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
            }

            entry.Trashed = true;
            entry.Version += 1;
            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> RestoreAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return await MutateAsync("restore", itemId, index =>
        {
            if (!index.Items.TryGetValue(itemId, out IndexEntry? entry))
            {
                return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
            }

            entry.Trashed = false;
            entry.Version += 1;
            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> DeletePermanentAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        Result<StorageItem> deleted = await MutateAsync("delete", itemId, index =>
        {
            if (!index.Items.TryGetValue(itemId, out IndexEntry? entry))
            {
                return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
            }

            if (entry.IsFolder && index.Items.Values.Any(e => string.Equals(e.ParentId, itemId, StringComparison.Ordinal)))
            {
                return Result.Fail<StorageItem>(OperationError.Invalid("Folder is not empty."));
            }

            index.Items.Remove(itemId);
            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);

        if (!deleted.IsSuccess)
        {
            return Result.Fail(deleted.Failure!);
        }

        try
        {
            if (File.Exists(ContentPath(itemId)))
            {
                File.Delete(ContentPath(itemId));
            }
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Delete failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Delete failed: " + ex.Message));
        }

        return Result.Ok();
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<StorageRevision>>> ListRevisionsAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        string dir = RevisionDir(itemId);
        var revisions = new List<StorageRevision>();
        if (Directory.Exists(dir))
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.bin");
            }
            catch (IOException ex)
            {
                return Task.FromResult(Result.Fail<IReadOnlyList<StorageRevision>>(OperationError.Provider("Revision list failed: " + ex.Message, true)));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Task.FromResult(Result.Fail<IReadOnlyList<StorageRevision>>(OperationError.Provider("Revision list failed: " + ex.Message)));
            }

            foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                string revId = Path.GetFileNameWithoutExtension(file);
                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                }
                catch (IOException ex)
                {
                    return Task.FromResult(Result.Fail<IReadOnlyList<StorageRevision>>(OperationError.Provider("Revision list failed: " + ex.Message, true)));
                }

                revisions.Add(new StorageRevision(revId, itemId, info.LastWriteTimeUtc, info.Length, null, PinnedRevisions(itemId).Contains(revId, StringComparer.Ordinal)));
            }
        }

        return Task.FromResult(Result.Ok<IReadOnlyList<StorageRevision>>(revisions));
    }

    /// <inheritdoc />
    public async Task<Result> GetRevisionContentAsync(string itemId, string revisionId, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(revisionId);
        ArgumentNullException.ThrowIfNull(destination);
        string path = Path.Combine(RevisionDir(itemId), revisionId + ".bin");
        if (!File.Exists(path))
        {
            return Result.Fail(OperationError.NotFound($"Revision '{revisionId}' was not found."));
        }

        try
        {
            await using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Revision download failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Revision download failed: " + ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task<Result<StorageRevision>> PinRevisionAsync(string itemId, string revisionId, bool keepForever, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(revisionId);
        string path = Path.Combine(RevisionDir(itemId), revisionId + ".bin");
        if (!File.Exists(path))
        {
            return Result.Fail<StorageRevision>(OperationError.NotFound($"Revision '{revisionId}' was not found."));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HashSet<string> pinned = PinnedRevisions(itemId);
            if (keepForever)
            {
                _ = pinned.Add(revisionId);
            }
            else
            {
                _ = pinned.Remove(revisionId);
            }

            await File.WriteAllLinesAsync(PinFile(itemId), pinned, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Result.Fail<StorageRevision>(OperationError.Provider("Pin failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<StorageRevision>(OperationError.Provider("Pin failed: " + ex.Message));
        }
        finally
        {
            _gate.Release();
        }

        FileInfo info = new(path);
        return Result.Ok(new StorageRevision(revisionId, itemId, info.LastWriteTimeUtc, info.Length, null, keepForever));
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> SetMetadataAsync(string itemId, string? description, IReadOnlyDictionary<string, string>? appProperties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return await MutateAsync("metadata", itemId, index =>
        {
            if (!index.Items.TryGetValue(itemId, out IndexEntry? entry))
            {
                return Result.Fail<StorageItem>(OperationError.NotFound($"Item '{itemId}' was not found."));
            }

            if (description is not null)
            {
                entry.Description = description;
            }

            if (appProperties is not null)
            {
                foreach (KeyValuePair<string, string> kv in appProperties)
                {
                    entry.AppProperties[kv.Key] = kv.Value;
                }
            }

            entry.Version += 1;
            return Result.Ok(ToItem(entry));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetStartChangeTokenAsync(CancellationToken cancellationToken)
    {
        Result<JournalState> loaded = await LoadJournalAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return Result.Fail<string>(loaded.Failure!);
        }

        return Result.Ok(loaded.Value.NextSeq.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public async Task<Result<StorageChangeSet>> GetChangesAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        Result<JournalState> loaded = await LoadJournalAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return Result.Fail<StorageChangeSet>(loaded.Failure!);
        }

        if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fromSeq))
        {
            return Result.Ok(new StorageChangeSet(Array.Empty<StorageChange>(), loaded.Value.NextSeq.ToString(CultureInfo.InvariantCulture), MustReindex: true));
        }

        if (fromSeq < loaded.Value.BaseSeq)
        {
            StorageLog.LocalJournalGap(_logger);
            return Result.Ok(new StorageChangeSet(Array.Empty<StorageChange>(), loaded.Value.NextSeq.ToString(CultureInfo.InvariantCulture), MustReindex: true));
        }

        Result<Index> index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (!index.IsSuccess || index.Value is null)
        {
            return Result.Fail<StorageChangeSet>(index.Failure!);
        }

        var changes = new List<StorageChange>();
        foreach (JournalEntry entry in loaded.Value.Entries.Where(e => e.Seq >= fromSeq).OrderBy(e => e.Seq))
        {
            if (string.Equals(entry.Op, "delete", StringComparison.Ordinal) || !index.Value.Items.TryGetValue(entry.ItemId, out IndexEntry? current))
            {
                changes.Add(new StorageChange(entry.ItemId, null, Removed: true));
            }
            else
            {
                changes.Add(new StorageChange(entry.ItemId, ToItem(current), Removed: false));
            }
        }

        return Result.Ok(new StorageChangeSet(changes, loaded.Value.NextSeq.ToString(CultureInfo.InvariantCulture), MustReindex: false));
    }

    /// <inheritdoc />
    public Task<Result<QuotaInfo>> GetQuotaAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok(new QuotaInfo(null, 0L)));
    }

    /// <inheritdoc />
    public Task<Result<AccountInfo>> GetAccountInfoAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok(new AccountInfo("local", "local@localfolder", "LocalFolder")));
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

    private async Task<Result<StorageItem>> MutateAsync(string op, string? itemId, Func<Index, Result<StorageItem>> mutate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(mutate);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Result<Index> loaded = await LoadIndexInternalAsync(cancellationToken).ConfigureAwait(false);
            if (!loaded.IsSuccess || loaded.Value is null)
            {
                return Result.Fail<StorageItem>(loaded.Failure!);
            }

            Result<StorageItem> result = mutate(loaded.Value);
            if (!result.IsSuccess || result.Value is null)
            {
                return result;
            }

            // Stamp modification time on the stored entry and return a fresh view.
            if (loaded.Value.Items.TryGetValue(result.Value.Id, out IndexEntry? stored))
            {
                stored.ModifiedUtc = _clock.GetUtcNow();
                result = Result.Ok(ToItem(stored));
            }

            Result saved = await SaveIndexInternalAsync(loaded.Value, cancellationToken).ConfigureAwait(false);
            if (!saved.IsSuccess)
            {
                return Result.Fail<StorageItem>(saved.Failure!);
            }

            string journalId = result.Value?.Id ?? itemId ?? RootId;
            Result journaled = await AppendJournalInternalAsync(op, journalId, cancellationToken).ConfigureAwait(false);
            if (!journaled.IsSuccess)
            {
                return Result.Fail<StorageItem>(journaled.Failure!);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CopyChildrenRecursiveAsync(string sourceParentId, string destParentId, CancellationToken cancellationToken)
    {
        Result<Index> loaded = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return;
        }

        List<IndexEntry> children = loaded.Value.Items.Values
            .Where(e => string.Equals(e.ParentId, sourceParentId, StringComparison.Ordinal))
            .ToList();
        foreach (IndexEntry child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Result<StorageItem> copy = await CopyAsync(child.Id, destParentId, null, cancellationToken).ConfigureAwait(false);
            _ = copy;
        }
    }

    private async Task<Result<StorageItem?>> FindByOperationIdAsync(string parentId, string operationId, CancellationToken cancellationToken)
    {
        Result<Index> loaded = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return Result.Fail<StorageItem?>(loaded.Failure!);
        }

        foreach (IndexEntry entry in loaded.Value.Items.Values)
        {
            if (string.Equals(entry.ParentId, parentId, StringComparison.Ordinal) &&
                entry.AppProperties.TryGetValue("inkguide.op", out string? op) &&
                string.Equals(op, operationId, StringComparison.Ordinal))
            {
                return Result.Ok<StorageItem?>(ToItem(entry));
            }
        }

        return Result.Ok<StorageItem?>(null);
    }

    private static Result<IndexEntry?> RequireParent(Index index, string parentId)
    {
        if (string.Equals(parentId, RootId, StringComparison.Ordinal))
        {
            return Result.Ok<IndexEntry?>(null);
        }

        if (!index.Items.TryGetValue(parentId, out IndexEntry? parent) || !parent.IsFolder || parent.Trashed)
        {
            return Result.Fail<IndexEntry?>(OperationError.NotFound($"Parent folder '{parentId}' was not found."));
        }

        return Result.Ok<IndexEntry?>(parent);
    }

    private Result<string> UniqueName(Index index, string? parentId, string baseName, bool isFolder, string? excludeId = null)
    {
        bool clash = index.Items.Values.Any(e =>
            string.Equals(e.ParentId, parentId, StringComparison.Ordinal) &&
            !string.Equals(e.Id, excludeId, StringComparison.Ordinal) &&
            !e.Trashed &&
            string.Equals(e.Name, baseName, StringComparison.OrdinalIgnoreCase));
        if (!clash)
        {
            return Result.Ok(baseName);
        }

        if (_options.CollisionPolicy == NameCollisionPolicy.Block)
        {
            return Result.Fail<string>(OperationError.AlreadyExists($"An item named '{baseName}' already exists."));
        }

        for (int i = 2; i < 10000; i++)
        {
            string candidate = baseName + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
            bool taken = index.Items.Values.Any(e =>
                string.Equals(e.ParentId, parentId, StringComparison.Ordinal) &&
                !e.Trashed &&
                string.Equals(e.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (!taken)
            {
                return Result.Ok(candidate);
            }
        }

        _ = isFolder; // Reserved for future policy differences (block vs suffix by kind).
        return Result.Fail<string>(OperationError.AlreadyExists($"An item named '{baseName}' already exists."));
    }

    private static bool IsDescendantOf(Index index, string candidateId, string ancestorId)
    {
        string current = candidateId;
        for (int i = 0; i < 10000; i++)
        {
            if (string.Equals(current, ancestorId, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(current, RootId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!index.Items.TryGetValue(current, out IndexEntry? entry) || entry.ParentId is null)
            {
                return false;
            }

            current = entry.ParentId;
        }

        return false;
    }

    private async Task<Result> WriteContentFileAsync(string id, Stream content, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        string dir = ContentDir();
        try
        {
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, id + ".tmp");
            await using (FileStream file = new(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                    progress?.Report(total);
                }
            }

            File.Move(temp, ContentPath(id), overwrite: true);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Write failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Write failed: " + ex.Message));
        }
    }

    private async Task<(long Size, string Md5)> HashContentFileAsync(string id, CancellationToken cancellationToken)
    {
        await using FileStream file = new(ContentPath(id), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        // Content hash (SHA-256 locally; Drive supplies MD5 — both are opaque identity strings here).
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long size = 0;
        int read;
        while ((read = await file.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer.AsSpan(0, read));
            size += read;
        }

        byte[] digest = hash.GetHashAndReset();
        return (size, Convert.ToHexString(digest).ToLower(CultureInfo.InvariantCulture));
    }

    private Task ArchiveRevisionAsync(string id, string version, CancellationToken cancellationToken)
    {
        string source = ContentPath(id);
        if (!File.Exists(source))
        {
            return Task.CompletedTask;
        }

        try
        {
            string dir = RevisionDir(id);
            Directory.CreateDirectory(dir);
            File.Copy(source, Path.Combine(dir, version + ".bin"), overwrite: true);
        }
        catch (IOException)
        {
            // Best effort: revision archiving must not fail the update itself.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: revision archiving must not fail the update itself.
        }

        return Task.CompletedTask;
    }

    private HashSet<string> PinnedRevisions(string id)
    {
        string pinFile = PinFile(id);
        if (!File.Exists(pinFile))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            return new HashSet<string>(File.ReadAllLines(pinFile), StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private async Task<Result<Index>> LoadIndexAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadIndexInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<Index>> LoadIndexInternalAsync(CancellationToken cancellationToken)
    {
        EnsureDirectories();
        string path = IndexPath();
        if (!File.Exists(path))
        {
            return Result.Ok(new Index());
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            Index? index = JsonSerializer.Deserialize<Index>(json, JsonOptions);
            return Result.Ok(index ?? new Index());
        }
        catch (IOException ex)
        {
            return Result.Fail<Index>(OperationError.Provider("Index read failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<Index>(OperationError.Provider("Index read failed: " + ex.Message));
        }
        catch (JsonException ex)
        {
            return Result.Fail<Index>(OperationError.Provider("Index is corrupt: " + ex.Message));
        }
    }

    private async Task<Result> SaveIndexInternalAsync(Index index, CancellationToken cancellationToken)
    {
        try
        {
            string temp = IndexPath() + ".tmp";
            string json = JsonSerializer.Serialize(index, JsonOptions);
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, IndexPath(), overwrite: true);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Index write failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Index write failed: " + ex.Message));
        }
    }

    private async Task<Result<JournalState>> LoadJournalAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadJournalInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<JournalState>> LoadJournalInternalAsync(CancellationToken cancellationToken)
    {
        EnsureDirectories();
        var state = new JournalState();
        string basePath = Path.Combine(IndexDir(), "journal.base");
        if (File.Exists(basePath))
        {
            string text = await File.ReadAllTextAsync(basePath, cancellationToken).ConfigureAwait(false);
            if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long baseSeq))
            {
                state.BaseSeq = baseSeq;
                state.NextSeq = baseSeq;
            }
        }

        string journalPath = JournalPath();
        if (!File.Exists(journalPath))
        {
            return Result.Ok(state);
        }

        try
        {
            string[] lines = await File.ReadAllLinesAsync(journalPath, cancellationToken).ConfigureAwait(false);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JournalEntry? entry = JsonSerializer.Deserialize<JournalEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    state.Entries.Add(entry);
                    state.NextSeq = Math.Max(state.NextSeq, entry.Seq + 1);
                }
            }

            return Result.Ok(state);
        }
        catch (IOException ex)
        {
            return Result.Fail<JournalState>(OperationError.Provider("Journal read failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<JournalState>(OperationError.Provider("Journal read failed: " + ex.Message));
        }
        catch (JsonException ex)
        {
            return Result.Fail<JournalState>(OperationError.Provider("Journal is corrupt: " + ex.Message));
        }
    }

    private async Task<Result> AppendJournalInternalAsync(string op, string itemId, CancellationToken cancellationToken)
    {
        try
        {
            Result<JournalState> loaded = await LoadJournalInternalAsync(cancellationToken).ConfigureAwait(false);
            if (!loaded.IsSuccess || loaded.Value is null)
            {
                return Result.Fail(loaded.Failure!);
            }

            var entry = new JournalEntry { Seq = loaded.Value.NextSeq, Op = op, ItemId = itemId };
            await File.AppendAllTextAsync(JournalPath(), JsonSerializer.Serialize(entry, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);
            if (loaded.Value.Entries.Count + 1 >= JournalRotateAt)
            {
                await RotateJournalInternalAsync(loaded.Value.NextSeq + 1, cancellationToken).ConfigureAwait(false);
            }

            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Journal write failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Journal write failed: " + ex.Message));
        }
    }

    private async Task RotateJournalInternalAsync(long newBase, CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(JournalPath(), cancellationToken).ConfigureAwait(false);
        string[] keep = lines.Length <= JournalKeep ? lines : lines.Skip(lines.Length - JournalKeep).ToArray();
        await File.WriteAllLinesAsync(JournalPath(), keep, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(IndexDir(), "journal.base"), newBase.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
    }

    private static StorageItem ToItem(IndexEntry entry)
    {
        return new StorageItem(
            entry.Id, entry.ParentId, entry.Name, entry.IsFolder, entry.MimeType, entry.Size,
            entry.Md5, entry.Version.ToString(CultureInfo.InvariantCulture), "r" + entry.Version.ToString(CultureInfo.InvariantCulture),
            entry.ModifiedUtc, entry.CreatedUtc, entry.Trashed, entry.Trashed, entry.Description,
            new Dictionary<string, string>(entry.AppProperties), null, true, true, true, true);
    }

    private static int ParseCursor(string? cursor)
    {
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset))
        {
            return Math.Max(0, offset);
        }

        return 0;
    }

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] cleaned = name.Trim().Where(c => Array.IndexOf(invalid, c) < 0).ToArray();
        string result = new string(cleaned).Trim().TrimEnd('.', ' ');
        if (result.Length > 200)
        {
            result = result.Substring(0, 200);
        }

        return result;
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(IndexDir());
        Directory.CreateDirectory(ContentDir());
        Directory.CreateDirectory(Path.Combine(_options.RootDirectory, RevisionsDirName));
    }

    private string IndexDir() => Path.Combine(_options.RootDirectory, IndexDirName);

    private string IndexPath() => Path.Combine(IndexDir(), "index.json");

    private string JournalPath() => Path.Combine(IndexDir(), "journal.jsonl");

    private string ContentDir() => Path.Combine(_options.RootDirectory, ContentDirName);

    private string ContentPath(string id) => Path.Combine(ContentDir(), id + ".bin");

    private string RevisionDir(string id) => Path.Combine(_options.RootDirectory, RevisionsDirName, id);

    private string PinFile(string id) => Path.Combine(RevisionDir(id), ".pinned");
}

/// <summary>Persisted index entry.</summary>
public sealed class IndexEntry
{
    public string Id { get; set; } = string.Empty;

    public string? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    public string? MimeType { get; set; }

    public long Size { get; set; }

    public string? Md5 { get; set; }

    public long Version { get; set; } = 1;

    public bool Trashed { get; set; }

    public string? Description { get; set; }

    public Dictionary<string, string> AppProperties { get; set; } = new(StringComparer.Ordinal);

    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Whole-provider index document.</summary>
public sealed class Index
{
    public Dictionary<string, IndexEntry> Items { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One journal line.</summary>
public sealed class JournalEntry
{
    public long Seq { get; set; }

    public string Op { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;
}

internal sealed class JournalState
{
    public long BaseSeq { get; set; }

    public long NextSeq { get; set; }

    public List<JournalEntry> Entries { get; } = new();
}
