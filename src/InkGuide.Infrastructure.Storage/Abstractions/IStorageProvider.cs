using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Storage.Common;

namespace InkGuide.Infrastructure.Storage.Abstractions;

// Local port (temporary seam): the canonical IStorageProvider is owned by Application
// (see D-006). This mirrors Section 7.1 and will be adapted once available.

/// <summary>Storage provider contract (Section 7.1). All methods are async, cancellable, Result-returning.</summary>
public interface IStorageProvider
{
    /// <summary>Provider display id: "googledrive" or "localfolder".</summary>
    string ProviderId { get; }

    /// <summary>OAuth/requested scopes for this provider (empty for LocalFolder).</summary>
    IReadOnlyList<string> RequestedScopes { get; }

    Task<Result<StorageCapabilities>> GetCapabilitiesAsync(CancellationToken cancellationToken);

    Task<Result<StorageItem>> GetRootAsync(CancellationToken cancellationToken);

    Task<Result<StorageItemPage>> ListChildrenAsync(string parentId, string? cursor, int pageSize, CancellationToken cancellationToken);

    Task<Result<StorageItem>> GetAsync(string itemId, CancellationToken cancellationToken);

    Task<Result<StorageItem>> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken);

    Task<Result<StorageItem>> UploadAsync(string parentId, string name, Stream content, string mimeType, string? operationId, IProgress<long>? progress, CancellationToken cancellationToken);

    Task<Result<StorageItem>> UpdateContentAsync(string itemId, Stream content, string? expectedVersion, string? operationId, IProgress<long>? progress, CancellationToken cancellationToken);

    Task<Result> DownloadAsync(string itemId, Stream destination, ContentRange? range, CancellationToken cancellationToken);

    Task<Result> ExportAsync(string itemId, string targetMimeType, Stream destination, CancellationToken cancellationToken);

    Task<Result<StorageItem>> RenameAsync(string itemId, string newName, CancellationToken cancellationToken);

    Task<Result<StorageItem>> MoveAsync(string itemId, string newParentId, CancellationToken cancellationToken);

    Task<Result<StorageItem>> CopyAsync(string itemId, string newParentId, string? newName, CancellationToken cancellationToken);

    Task<Result<StorageItem>> TrashAsync(string itemId, CancellationToken cancellationToken);

    Task<Result<StorageItem>> RestoreAsync(string itemId, CancellationToken cancellationToken);

    Task<Result> DeletePermanentAsync(string itemId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<StorageRevision>>> ListRevisionsAsync(string itemId, CancellationToken cancellationToken);

    Task<Result> GetRevisionContentAsync(string itemId, string revisionId, Stream destination, CancellationToken cancellationToken);

    Task<Result<StorageRevision>> PinRevisionAsync(string itemId, string revisionId, bool keepForever, CancellationToken cancellationToken);

    Task<Result<StorageItem>> SetMetadataAsync(string itemId, string? description, IReadOnlyDictionary<string, string>? appProperties, CancellationToken cancellationToken);

    Task<Result<string>> GetStartChangeTokenAsync(CancellationToken cancellationToken);

    Task<Result<StorageChangeSet>> GetChangesAsync(string token, CancellationToken cancellationToken);

    Task<Result<QuotaInfo>> GetQuotaAsync(CancellationToken cancellationToken);

    Task<Result<AccountInfo>> GetAccountInfoAsync(CancellationToken cancellationToken);
}
