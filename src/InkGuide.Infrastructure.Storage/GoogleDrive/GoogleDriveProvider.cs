using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Drive.v3.Data;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.Common;
using InkGuide.Infrastructure.Storage.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly.CircuitBreaker;

namespace InkGuide.Infrastructure.Storage.GoogleDrive;

/// <summary>
/// <see cref="Abstractions.IStorageProvider"/> over the Drive v3 REST API (My Drive only).
/// Google SDK types never leave this folder. Always specifies fields; least-privilege scopes.
/// </summary>
public sealed class GoogleDriveProvider : Abstractions.IStorageProvider
{
    private const int MinChunkBytes = 256 * 1024;

    private readonly HttpClient _http;
    private readonly GoogleDriveOptions _options;
    private readonly IAccessTokenProvider _tokens;
    private readonly DriveResilience _resilience;
    private readonly IUploadSessionReporter? _sessions;
    private readonly TimeProvider _clock;
    private readonly ILogger<GoogleDriveProvider> _logger;

    public GoogleDriveProvider(
        HttpClient httpClient,
        GoogleDriveOptions options,
        IAccessTokenProvider tokens,
        DriveResilience resilience,
        TimeProvider clock,
        ILogger<GoogleDriveProvider> logger,
        IUploadSessionReporter? sessions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(resilience);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        if (options.ChunkBytes < MinChunkBytes || options.ChunkBytes % MinChunkBytes != 0)
        {
            throw new ArgumentException("ChunkBytes must be a multiple of 256 KB.", nameof(options));
        }

        _http = httpClient;
        _options = options;
        _tokens = tokens;
        _resilience = resilience;
        _clock = clock;
        _logger = logger;
        _sessions = sessions;
    }

    /// <inheritdoc />
    public string ProviderId => "googledrive";

    /// <inheritdoc />
    public IReadOnlyList<string> RequestedScopes => DriveConstants.ScopesFor(_options.FullDrive);

    /// <inheritdoc />
    public Task<Result<StorageCapabilities>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        StorageCapabilities capabilities = new(
            SupportsRevisions: true,
            SupportsExport: true,
            SupportsChangeFeed: true,
            SupportsServerSideCopy: true,
            MaxChunkBytes: _options.ChunkBytes,
            ChangeFeedResumable: true);
        return Task.FromResult(Result.Ok<StorageCapabilities>(capabilities));
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> GetRootAsync(CancellationToken cancellationToken)
    {
        return await GetAsync("root", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItemPage>> ListChildrenAsync(string parentId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentId);
        string query = "'" + EscapeQuery(parentId) + "' in parents and trashed = false";
        string url = DriveConstants.ApiBase + "/files?q=" + Uri.EscapeDataString(query)
            + "&fields=" + Uri.EscapeDataString("nextPageToken,files(" + DriveConstants.FileFields + ")")
            + "&pageSize=" + Math.Clamp(pageSize <= 0 ? 200 : pageSize, 1, 1000).ToString(CultureInfo.InvariantCulture)
            + "&orderBy=name"
            + "&supportsAllDrives=false";
        if (!string.IsNullOrEmpty(cursor))
        {
            url += "&pageToken=" + Uri.EscapeDataString(cursor);
        }

        Result<FileList> listed = await GetJsonAsync<FileList>(url, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            return Result.Fail<StorageItemPage>(listed.Failure!);
        }

        List<StorageItem> items = listed.Value.Files?.Select(ToItem).ToList() ?? new List<StorageItem>();
        return Result.Ok(new StorageItemPage(items, listed.Value.NextPageToken));
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> GetAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId)
            + "?fields=" + Uri.EscapeDataString(DriveConstants.FileFields)
            + "&supportsAllDrives=false";
        Result<DriveFile> file = await GetJsonAsync<DriveFile>(url, cancellationToken).ConfigureAwait(false);
        return file.IsSuccess && file.Value is not null
            ? Result.Ok(ToItem(file.Value))
            : Result.Fail<StorageItem>(file.Failure!);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentId);
        ArgumentNullException.ThrowIfNull(name);
        Result<string> unique = await EnsureUniqueNameAsync(parentId, name, null, cancellationToken).ConfigureAwait(false);
        if (!unique.IsSuccess || unique.Value is null)
        {
            return Result.Fail<StorageItem>(unique.Failure!);
        }

        var body = new { name = unique.Value, mimeType = DriveConstants.FolderMime, parents = new[] { parentId } };
        string url = DriveConstants.ApiBase + "/files?fields=" + Uri.EscapeDataString(DriveConstants.FileFields) + "&supportsAllDrives=false";
        Result<DriveFile> created = await PostJsonAsync<DriveFile, object>(url, body, cancellationToken).ConfigureAwait(false);
        return created.IsSuccess && created.Value is not null
            ? Result.Ok(ToItem(created.Value))
            : Result.Fail<StorageItem>(created.Failure!);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> UploadAsync(string parentId, string name, Stream content, string mimeType, string? operationId, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(mimeType);

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

        Result<string> unique = await EnsureUniqueNameAsync(parentId, name, null, cancellationToken).ConfigureAwait(false);
        if (!unique.IsSuccess || unique.Value is null)
        {
            return Result.Fail<StorageItem>(unique.Failure!);
        }

        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = unique.Value,
            ["parents"] = new[] { parentId },
        };
        if (operationId is not null)
        {
            metadata["appProperties"] = new Dictionary<string, string>(StringComparer.Ordinal) { [DriveConstants.AppKeys.OperationId] = operationId };
        }

        return await ResumableUploadAsync(null, metadata, content, mimeType, operationId, unique.Value, progress, cancellationToken).ConfigureAwait(false);
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

        if (IsGoogleNative(current.Value.MimeType))
        {
            return Result.Fail<StorageItem>(OperationError.Unsupported("Google-native files have no binary content; use ExportAsync."));
        }

        if (expectedVersion is not null && !string.Equals(expectedVersion, current.Value.Version, StringComparison.Ordinal))
        {
            return Result.Fail<StorageItem>(OperationError.Conflict($"Version moved (base={expectedVersion}, remote={current.Value.Version})."));
        }

        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (operationId is not null)
        {
            metadata["appProperties"] = new Dictionary<string, string>(StringComparer.Ordinal) { [DriveConstants.AppKeys.OperationId] = operationId };
        }

        return await ResumableUploadAsync(itemId, metadata, content, current.Value.MimeType ?? "application/octet-stream", operationId, current.Value.Name, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> DownloadAsync(string itemId, Stream destination, ContentRange? range, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(destination);
        Result<StorageItem> current = await GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value is null)
        {
            return Result.Fail(current.Failure!);
        }

        if (IsGoogleNative(current.Value.MimeType))
        {
            return Result.Fail(OperationError.Unsupported("Google-native files have no binary content; use ExportAsync."));
        }

        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "?alt=media&supportsAllDrives=false";
        return await SendVoidWithAuthAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (range is not null)
                {
                    request.Headers.Range = range.To.HasValue
                        ? new RangeHeaderValue(range.From, range.To.Value)
                        : new RangeHeaderValue(range.From, null);
                }

                return request;
            },
            async (response, ct) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Fail(await MapErrorAsync("download", response, ct).ConfigureAwait(false));
                }

                try
                {
                    await response.Content.CopyToAsync(destination, ct).ConfigureAwait(false);
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
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> ExportAsync(string itemId, string targetMimeType, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(targetMimeType);
        ArgumentNullException.ThrowIfNull(destination);
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "/export?mimeType=" + Uri.EscapeDataString(targetMimeType) + "&supportsAllDrives=false";
        return await SendVoidWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            async (response, ct) =>
            {
                if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return Result.Fail(OperationError.Unsupported("This file cannot be exported to " + targetMimeType + "."));
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result.Fail(await MapErrorAsync("export", response, ct).ConfigureAwait(false));
                }

                try
                {
                    await response.Content.CopyToAsync(destination, ct).ConfigureAwait(false);
                    return Result.Ok();
                }
                catch (IOException ex)
                {
                    return Result.Fail(OperationError.Provider("Export failed: " + ex.Message, true));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Result.Fail(OperationError.Provider("Export failed: " + ex.Message));
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> RenameAsync(string itemId, string newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(newName);
        var body = new { name = newName };
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "?fields=" + Uri.EscapeDataString(DriveConstants.FileFields) + "&supportsAllDrives=false";
        Result<DriveFile> updated = await PatchJsonAsync<DriveFile, object>(url, body, cancellationToken).ConfigureAwait(false);
        return updated.IsSuccess && updated.Value is not null
            ? Result.Ok(ToItem(updated.Value))
            : Result.Fail<StorageItem>(updated.Failure!);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> MoveAsync(string itemId, string newParentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(newParentId);
        Result<DriveFile> current = await GetJsonAsync<DriveFile>(
            DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "?fields=" + Uri.EscapeDataString("id,parents") + "&supportsAllDrives=false",
            cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess || current.Value is null)
        {
            return Result.Fail<StorageItem>(current.Failure!);
        }

        string remove = current.Value.Parents is not null ? string.Join(",", current.Value.Parents) : string.Empty;
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId)
            + "?addParents=" + Uri.EscapeDataString(newParentId)
            + "&removeParents=" + Uri.EscapeDataString(remove)
            + "&fields=" + Uri.EscapeDataString(DriveConstants.FileFields)
            + "&supportsAllDrives=false";
        Result<DriveFile> moved = await PatchJsonAsync<DriveFile, object>(url, new { }, cancellationToken).ConfigureAwait(false);
        return moved.IsSuccess && moved.Value is not null
            ? Result.Ok(ToItem(moved.Value))
            : Result.Fail<StorageItem>(moved.Failure!);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> CopyAsync(string itemId, string newParentId, string? newName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(newParentId);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal) { ["parents"] = new[] { newParentId } };
        if (!string.IsNullOrEmpty(newName))
        {
            body["name"] = newName;
        }

        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "/copy?fields=" + Uri.EscapeDataString(DriveConstants.FileFields) + "&supportsAllDrives=false";
        Result<DriveFile> copied = await PostJsonAsync<DriveFile, Dictionary<string, object?>>(url, body, cancellationToken).ConfigureAwait(false);
        return copied.IsSuccess && copied.Value is not null
            ? Result.Ok(ToItem(copied.Value))
            : Result.Fail<StorageItem>(copied.Failure!);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> TrashAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return await SetTrashedAsync(itemId, true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> RestoreAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return await SetTrashedAsync(itemId, false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> DeletePermanentAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "?supportsAllDrives=false";
        return await SendVoidWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, url),
            async (response, ct) =>
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Result.Fail(OperationError.NotFound($"Item '{itemId}' was not found."));
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result.Fail(await MapErrorAsync("delete", response, ct).ConfigureAwait(false));
                }

                return Result.Ok();
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StorageRevision>>> ListRevisionsAsync(string itemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "/revisions?fields=" + Uri.EscapeDataString("revisions(id,modifiedTime,size,md5Checksum,keepForever)") + "&supportsAllDrives=false";
        Result<RevisionList> listed = await GetJsonAsync<RevisionList>(url, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            return Result.Fail<IReadOnlyList<StorageRevision>>(listed.Failure!);
        }

        List<StorageRevision> revisions = listed.Value.Revisions?.Select(r => new StorageRevision(
            r.Id ?? string.Empty, itemId,
            r.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UtcNow,
            r.Size ?? 0L, r.Md5Checksum, r.KeepForever ?? false)).ToList() ?? new List<StorageRevision>();
        return Result.Ok<IReadOnlyList<StorageRevision>>(revisions);
    }

    /// <inheritdoc />
    public async Task<Result> GetRevisionContentAsync(string itemId, string revisionId, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(revisionId);
        ArgumentNullException.ThrowIfNull(destination);
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "/revisions/" + Uri.EscapeDataString(revisionId) + "?alt=media&supportsAllDrives=false";
        return await SendVoidWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            async (response, ct) =>
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Result.Fail(OperationError.NotFound($"Revision '{revisionId}' was not found."));
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result.Fail(await MapErrorAsync("revision-download", response, ct).ConfigureAwait(false));
                }

                try
                {
                    await response.Content.CopyToAsync(destination, ct).ConfigureAwait(false);
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
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<StorageRevision>> PinRevisionAsync(string itemId, string revisionId, bool keepForever, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(revisionId);
        var body = new { keepForever };
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "/revisions/" + Uri.EscapeDataString(revisionId)
            + "?fields=" + Uri.EscapeDataString("id,modifiedTime,size,md5Checksum,keepForever") + "&supportsAllDrives=false";
        Result<Revision> pinned = await PatchJsonAsync<Revision, object>(url, body, cancellationToken).ConfigureAwait(false);
        if (!pinned.IsSuccess || pinned.Value is null)
        {
            return Result.Fail<StorageRevision>(pinned.Failure!);
        }

        Revision revision = pinned.Value;
        return Result.Ok(new StorageRevision(revision.Id ?? revisionId, itemId, revision.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UtcNow, revision.Size ?? 0L, revision.Md5Checksum, revision.KeepForever ?? keepForever));
    }

    /// <inheritdoc />
    public async Task<Result<StorageItem>> SetMetadataAsync(string itemId, string? description, IReadOnlyDictionary<string, string>? appProperties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (description is not null)
        {
            body["description"] = description;
        }

        if (appProperties is not null)
        {
            body["appProperties"] = appProperties;
        }

        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "?fields=" + Uri.EscapeDataString(DriveConstants.FileFields) + "&supportsAllDrives=false";
        Result<DriveFile> updated = await PatchJsonAsync<DriveFile, Dictionary<string, object?>>(url, body, cancellationToken).ConfigureAwait(false);
        return updated.IsSuccess && updated.Value is not null
            ? Result.Ok(ToItem(updated.Value))
            : Result.Fail<StorageItem>(updated.Failure!);
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetStartChangeTokenAsync(CancellationToken cancellationToken)
    {
        string url = DriveConstants.ApiBase + "/changes/startPageToken?spaces=drive&supportsAllDrives=false&fields=" + Uri.EscapeDataString("startPageToken");
        Result<StartPageToken> token = await GetJsonAsync<StartPageToken>(url, cancellationToken).ConfigureAwait(false);
        if (!token.IsSuccess || token.Value?.StartPageTokenValue is null)
        {
            return Result.Fail<string>(token.Failure ?? OperationError.Provider("Change token request failed.", true));
        }

        return Result.Ok(token.Value.StartPageTokenValue);
    }

    /// <inheritdoc />
    public async Task<Result<StorageChangeSet>> GetChangesAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        string url = DriveConstants.ApiBase + "/changes?pageToken=" + Uri.EscapeDataString(token)
            + "&includeRemoved=true&spaces=drive&supportsAllDrives=false&pageSize=100"
            + "&fields=" + Uri.EscapeDataString(DriveConstants.ChangesFields);
        Result<ChangeList> listed = await GetJsonAsync<ChangeList>(url, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            if (listed.Failure is not null && IsInvalidToken(listed.Failure))
            {
                return Result.Ok(new StorageChangeSet(Array.Empty<StorageChange>(), token, MustReindex: true));
            }

            return Result.Fail<StorageChangeSet>(listed.Failure!);
        }

        var changes = new List<StorageChange>();
        if (listed.Value.Changes is not null)
        {
            foreach (Change change in listed.Value.Changes)
            {
                string id = change.FileId ?? change.File?.Id ?? string.Empty;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                bool removed = (change.Removed ?? false) || change.File is null;
                changes.Add(new StorageChange(id, removed ? null : ToItem(change.File!), Removed: removed));
            }
        }

        string next = listed.Value.NewStartPageToken ?? listed.Value.NextPageToken ?? token;
        return Result.Ok(new StorageChangeSet(changes, next, MustReindex: false));
    }

    /// <inheritdoc />
    public async Task<Result<QuotaInfo>> GetQuotaAsync(CancellationToken cancellationToken)
    {
        string url = DriveConstants.ApiBase + "/about?fields=" + Uri.EscapeDataString("storageQuota(limit,usage)") + "&supportsAllDrives=false";
        Result<About> about = await GetJsonAsync<About>(url, cancellationToken).ConfigureAwait(false);
        if (!about.IsSuccess || about.Value is null)
        {
            return Result.Fail<QuotaInfo>(about.Failure!);
        }

        return Result.Ok(new QuotaInfo(about.Value.StorageQuota?.Limit, about.Value.StorageQuota?.Usage ?? 0L));
    }

    /// <inheritdoc />
    public async Task<Result<AccountInfo>> GetAccountInfoAsync(CancellationToken cancellationToken)
    {
        string url = DriveConstants.ApiBase + "/about?fields=" + Uri.EscapeDataString("user(displayName,emailAddress)") + "&supportsAllDrives=false";
        Result<About> about = await GetJsonAsync<About>(url, cancellationToken).ConfigureAwait(false);
        if (!about.IsSuccess || about.Value is null)
        {
            return Result.Fail<AccountInfo>(about.Failure!);
        }

        string email = about.Value.User?.EmailAddress ?? _options.AccountId;
        return Result.Ok(new AccountInfo(email, email, about.Value.User?.DisplayName ?? email));
    }

    /// <summary>Maps a Drive File resource to a StorageItem (all contract fields).</summary>
    internal static StorageItem ToItem(DriveFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var appProps = new Dictionary<string, string>(StringComparer.Ordinal);
        if (file.AppProperties is not null)
        {
            foreach (KeyValuePair<string, string> kv in file.AppProperties)
            {
                appProps[kv.Key] = kv.Value;
            }
        }

        bool isFolder = string.Equals(file.MimeType, DriveConstants.FolderMime, StringComparison.Ordinal);
        return new StorageItem(
            file.Id ?? string.Empty,
            file.Parents is not null && file.Parents.Count > 0 ? file.Parents[0] : null,
            file.Name ?? string.Empty,
            isFolder,
            file.MimeType,
            file.Size ?? 0L,
            file.Md5Checksum,
            file.Version?.ToString(CultureInfo.InvariantCulture),
            file.HeadRevisionId,
            file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UtcNow,
            file.CreatedTimeDateTimeOffset ?? DateTimeOffset.UtcNow,
            file.Trashed ?? false,
            file.ExplicitlyTrashed ?? false,
            file.Description,
            appProps,
            file.WebViewLink,
            file.Capabilities?.CanEdit ?? false,
            file.Capabilities?.CanDelete ?? false,
            file.Capabilities?.CanTrash ?? false,
            file.Capabilities?.CanRename ?? false);
    }

    internal static bool IsGoogleNative(string? mimeType)
    {
        return mimeType is not null && mimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal);
    }

    private async Task<Result<StorageItem>> SetTrashedAsync(string itemId, bool trashed, CancellationToken cancellationToken)
    {
        var body = new { trashed };
        string url = DriveConstants.ApiBase + "/files/" + Uri.EscapeDataString(itemId) + "?fields=" + Uri.EscapeDataString(DriveConstants.FileFields) + "&supportsAllDrives=false";
        Result<DriveFile> updated = await PatchJsonAsync<DriveFile, object>(url, body, cancellationToken).ConfigureAwait(false);
        return updated.IsSuccess && updated.Value is not null
            ? Result.Ok(ToItem(updated.Value))
            : Result.Fail<StorageItem>(updated.Failure!);
    }

    private async Task<Result<string>> EnsureUniqueNameAsync(string parentId, string name, string? excludeId, CancellationToken cancellationToken)
    {
        string query = "'" + EscapeQuery(parentId) + "' in parents and name = '" + EscapeQuery(name) + "' and trashed = false";
        string url = DriveConstants.ApiBase + "/files?q=" + Uri.EscapeDataString(query)
            + "&fields=" + Uri.EscapeDataString("files(id)") + "&pageSize=10&supportsAllDrives=false";
        Result<FileList> listed = await GetJsonAsync<FileList>(url, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            return Result.Fail<string>(listed.Failure!);
        }

        bool clash = listed.Value.Files?.Any(f => !string.Equals(f.Id, excludeId, StringComparison.Ordinal)) ?? false;
        if (!clash)
        {
            return Result.Ok(name);
        }

        if (_options.CollisionPolicy == NameCollisionPolicy.Block)
        {
            return Result.Fail<string>(OperationError.AlreadyExists($"An item named '{name}' already exists."));
        }

        for (int i = 2; i < 10000; i++)
        {
            string candidate = name + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
            Result<string> retry = await EnsureUniqueNameAsync(parentId, candidate, excludeId, cancellationToken).ConfigureAwait(false);
            if (retry.IsSuccess)
            {
                return retry;
            }

            if (retry.Failure is not null && !string.Equals(retry.Failure.Code, "ALREADY_EXISTS", StringComparison.Ordinal))
            {
                return retry;
            }
        }

        return Result.Fail<string>(OperationError.AlreadyExists($"An item named '{name}' already exists."));
    }

    private async Task<Result<StorageItem?>> FindByOperationIdAsync(string parentId, string operationId, CancellationToken cancellationToken)
    {
        string query = "'" + EscapeQuery(parentId) + "' in parents and appProperties has { key='"
            + DriveConstants.AppKeys.OperationId + "' and value='" + EscapeQuery(operationId) + "' } and trashed = false";
        string url = DriveConstants.ApiBase + "/files?q=" + Uri.EscapeDataString(query)
            + "&fields=" + Uri.EscapeDataString("files(" + DriveConstants.FileFields + ")") + "&pageSize=5&supportsAllDrives=false";
        Result<FileList> listed = await GetJsonAsync<FileList>(url, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess || listed.Value is null)
        {
            return Result.Fail<StorageItem?>(listed.Failure!);
        }

        DriveFile? match = listed.Value.Files?.FirstOrDefault();
        return Result.Ok<StorageItem?>(match is null ? null : ToItem(match));
    }

    private async Task<Result<StorageItem>> ResumableUploadAsync(
        string? fileId,
        Dictionary<string, object?> metadata,
        Stream content,
        string mimeType,
        string? operationId,
        string displayName,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        string sessionUrl = fileId is null
            ? DriveConstants.UploadBase + "/files?uploadType=resumable&fields=" + Uri.EscapeDataString(DriveConstants.FileFields) + "&supportsAllDrives=false"
            : DriveConstants.UploadBase + "/files/" + Uri.EscapeDataString(fileId) + "?uploadType=resumable&fields=" + Uri.EscapeDataString(DriveConstants.FileFields) + "&supportsAllDrives=false";

        Result<string> session = await InitiateSessionAsync(sessionUrl, metadata, mimeType, cancellationToken).ConfigureAwait(false);
        if (!session.IsSuccess || session.Value is null)
        {
            return Result.Fail<StorageItem>(session.Failure!);
        }

        if (operationId is not null && _sessions is not null)
        {
            try
            {
                await _sessions.ReportSessionAsync(operationId, session.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Best effort: a missed session report only loses crash-resume of the upload session.
            }
        }

        StorageLog.UploadResumed(_logger, displayName, 0);
        return await PutChunksAsync(session.Value, content, mimeType, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<string>> InitiateSessionAsync(string url, Dictionary<string, object?> metadata, string mimeType, CancellationToken cancellationToken)
    {
        string json = JsonConvert.SerializeObject(metadata);
        return await SendWithAuthAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("X-Upload-Content-Type", mimeType);
                return request;
            },
            async (response, ct) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Fail<string>(await MapErrorAsync("upload-initiate", response, ct).ConfigureAwait(false));
                }

                Uri? location = response.Headers.Location;
                if (location is null)
                {
                    return Result.Fail<string>(OperationError.Provider("Upload initiation returned no session URI.", true));
                }

                return Result.Ok(location.ToString());
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<StorageItem>> PutChunksAsync(string sessionUri, Stream content, string mimeType, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[_options.ChunkBytes];
        long offset = 0;
        long? total = content.CanSeek ? content.Length : null;
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            long end = offset + read - 1;
            string contentRange = total.HasValue
                ? "bytes " + offset.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture) + "/" + total.Value.ToString(CultureInfo.InvariantCulture)
                : "bytes " + offset.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture) + "/*";
            byte[] chunk = read == buffer.Length ? buffer : buffer[..read];
            Result<DriveFile?> uploaded = await PutChunkAsync(sessionUri, chunk, contentRange, mimeType, cancellationToken).ConfigureAwait(false);
            if (!uploaded.IsSuccess)
            {
                return Result.Fail<StorageItem>(uploaded.Failure!);
            }

            offset += read;
            progress?.Report(offset);
            if (uploaded.Value is not null)
            {
                return Result.Ok(ToItem(uploaded.Value));
            }
        }

        // Empty stream: finalize with zero bytes.
        Result<DriveFile?> empty = await PutChunkAsync(sessionUri, Array.Empty<byte>(), "bytes */0", mimeType, cancellationToken).ConfigureAwait(false);
        if (!empty.IsSuccess || empty.Value is null)
        {
            return Result.Fail<StorageItem>(empty.Failure ?? OperationError.Provider("Empty upload did not complete.", true));
        }

        return Result.Ok(ToItem(empty.Value));
    }

    private async Task<Result<DriveFile?>> PutChunkAsync(string sessionUri, byte[] chunk, string contentRange, string mimeType, CancellationToken cancellationToken)
    {
        return await SendWithAuthAsync(
            () =>
            {
                var chunkContent = new ByteArrayContent(chunk);
                chunkContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                chunkContent.Headers.ContentRange = System.Net.Http.Headers.ContentRangeHeaderValue.Parse(contentRange);
                var request = new HttpRequestMessage(HttpMethod.Put, sessionUri)
                {
                    Content = chunkContent,
                };
                return request;
            },
            async (response, ct) =>
            {
                if (response.StatusCode == (HttpStatusCode)308)
                {
                    return Result.Ok<DriveFile?>(null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result.Fail<DriveFile?>(await MapErrorAsync("upload-chunk", response, ct).ConfigureAwait(false));
                }

                string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                DriveFile? file;
                try
                {
                    file = JsonConvert.DeserializeObject<DriveFile>(body);
                }
                catch (JsonException ex)
                {
                    return Result.Fail<DriveFile?>(OperationError.Provider("Upload response was invalid: " + ex.Message, true));
                }

                return file is null
                    ? Result.Fail<DriveFile?>(OperationError.Provider("Upload response was empty.", true))
                    : Result.Ok<DriveFile?>(file);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<T>> GetJsonAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        return await SendWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            async (response, ct) => await ReadJsonAsync<T>("get", response, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<T>> PostJsonAsync<T, TBody>(string url, TBody body, CancellationToken cancellationToken) where T : class
    {
        string json = JsonConvert.SerializeObject(body);
        return await SendWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            },
            async (response, ct) => await ReadJsonAsync<T>("post", response, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<T>> PatchJsonAsync<T, TBody>(string url, TBody body, CancellationToken cancellationToken) where T : class
    {
        string json = JsonConvert.SerializeObject(body);
        return await SendWithAuthAsync(
            () => new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            },
            async (response, ct) => await ReadJsonAsync<T>("patch", response, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<T>> ReadJsonAsync<T>(string op, HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail<T>(OperationError.NotFound("Drive item was not found."));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail<T>(await MapErrorAsync(op, response, cancellationToken).ConfigureAwait(false));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        T? value;
        try
        {
            value = JsonConvert.DeserializeObject<T>(body);
        }
        catch (JsonException ex)
        {
            return Result.Fail<T>(OperationError.Provider("Drive response was invalid: " + ex.Message, true));
        }

        return value is null
            ? Result.Fail<T>(OperationError.Provider("Drive response was empty.", true))
            : Result.Ok(value);
    }

    private static async Task<OperationError> MapErrorAsync(string op, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            body = string.Empty;
        }
        catch (TaskCanceledException)
        {
            body = string.Empty;
        }

        int status = (int)response.StatusCode;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => OperationError.AuthRequired($"Drive {op} was rejected (401)."),
            HttpStatusCode.PaymentRequired => OperationError.QuotaExceeded($"Drive {op} exceeded quota."),
            HttpStatusCode.Forbidden when body.Contains("rateLimitExceeded", StringComparison.Ordinal) => OperationError.RateLimited($"Drive {op} was rate limited."),
            HttpStatusCode.Forbidden => OperationError.Provider($"Drive {op} was forbidden: {TrimBody(body)}"),
            HttpStatusCode.NotFound => OperationError.NotFound($"Drive {op}: item was not found."),
            HttpStatusCode.TooManyRequests => OperationError.RateLimited($"Drive {op} was rate limited."),
            HttpStatusCode.RequestEntityTooLarge => OperationError.Provider($"Drive {op}: payload too large.", false),
            _ when status >= 500 => OperationError.Provider($"Drive {op} failed ({status}): {TrimBody(body)}", true),
            _ => OperationError.Provider($"Drive {op} failed ({status}): {TrimBody(body)}"),
        };
    }

    private static string TrimBody(string body)
    {
        return body.Length <= 300 ? body : body.Substring(0, 300);
    }

    private static bool IsInvalidToken(OperationError failure)
    {
        return failure.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            && failure.Message.Contains("token", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeQuery(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    }

    private async Task<Result> SendVoidWithAuthAsync(Func<HttpRequestMessage> createRequest, Func<HttpResponseMessage, CancellationToken, Task<Result>> handle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentNullException.ThrowIfNull(handle);
        Result<bool> wrapped = await SendWithAuthAsync<bool>(
            createRequest,
            async (response, ct) =>
            {
                Result handled = await handle(response, ct).ConfigureAwait(false);
                return handled.IsSuccess ? Result.Ok(true) : Result.Fail<bool>(handled.Failure!);
            },
            cancellationToken).ConfigureAwait(false);
        return wrapped.IsSuccess ? Result.Ok() : Result.Fail(wrapped.Failure!);
    }

    private async Task<Result<T>> SendWithAuthAsync<T>(Func<HttpRequestMessage> createRequest, Func<HttpResponseMessage, CancellationToken, Task<Result<T>>> handle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentNullException.ThrowIfNull(handle);
        Result<string> token = await _tokens.GetAccessTokenAsync(_options.AccountId, cancellationToken).ConfigureAwait(false);
        if (!token.IsSuccess || token.Value is null)
        {
            return Result.Fail<T>(token.Failure!);
        }

        try
        {
            HttpResponseMessage response = await _resilience.SendAsync(
                async ct =>
                {
                    using HttpRequestMessage attempt = createRequest();
                    attempt.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
                    return await _http.SendAsync(attempt, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                Result<string> refreshed = await _tokens.RefreshAccessTokenAsync(_options.AccountId, cancellationToken).ConfigureAwait(false);
                if (!refreshed.IsSuccess || refreshed.Value is null)
                {
                    return Result.Fail<T>(refreshed.Failure!);
                }

                HttpResponseMessage retryResponse = await _resilience.SendAsync(
                    async ct =>
                    {
                        using HttpRequestMessage retry = createRequest();
                        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.Value);
                        return await _http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
                using (retryResponse)
                {
                    return await handle(retryResponse, cancellationToken).ConfigureAwait(false);
                }
            }

            using (response)
            {
                return await handle(response, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (BrokenCircuitException)
        {
            return Result.Fail<T>(OperationError.Offline("Drive is unreachable (circuit open)."));
        }
        catch (HttpRequestException ex)
        {
            return Result.Fail<T>(OperationError.Provider("Drive request failed: " + ex.Message, true));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<T>(OperationError.Provider("Drive request timed out: " + ex.Message, true));
        }
    }
}
