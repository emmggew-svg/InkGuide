using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;

namespace InkGuide.Application.Storage;

/// <summary>
/// Storage abstraction over Google Drive and the LocalFolder dev/test provider.
/// Drive is the source of truth; every member is async, cancellable and
/// returns a result with a typed failure for expected failures.
/// </summary>
public interface IStorageProvider
{
    /// <summary>What this provider implementation supports.</summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>Gets the workspace root node, creating it when the provider owns the namespace.</summary>
    Task<Result<RemoteNode, Failure>> GetRootAsync(CancellationToken ct);

    /// <summary>Lists one page of children of a folder.</summary>
    /// <param name="parentId">Provider id of the folder.</param>
    /// <param name="cursor">Opaque page cursor from a previous call. Null for the first page.</param>
    /// <param name="limit">Maximum items to return (provider may return fewer).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<ChildPage, Failure>> ListChildrenAsync(string parentId, string? cursor, int limit, CancellationToken ct);

    /// <summary>Gets one node by id.</summary>
    Task<Result<RemoteNode, Failure>> GetAsync(string nodeId, CancellationToken ct);

    /// <summary>Creates a folder.</summary>
    Task<Result<RemoteNode, Failure>> CreateFolderAsync(string parentId, string name, CancellationToken ct);

    /// <summary>Uploads new content (resumable for large payloads). Reports bytes sent via progress.</summary>
    Task<Result<RemoteNode, Failure>> UploadAsync(
        string parentId,
        string name,
        Stream content,
        string mimeType,
        IProgress<long>? progress,
        CancellationToken ct);

    /// <summary>Replaces content with a conflict-safe version check. Never overwrites on version mismatch.</summary>
    /// <param name="nodeId">Node to update.</param>
    /// <param name="content">New content bytes.</param>
    /// <param name="expectedVersion">Base version the caller read. Mismatch yields CONFLICT_VERSION.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<RemoteNode, Failure>> UpdateContentAsync(string nodeId, Stream content, string expectedVersion, CancellationToken ct);

    /// <summary>Downloads content. The caller owns and must dispose the returned stream.</summary>
    /// <param name="nodeId">Node to download.</param>
    /// <param name="fromByte">First byte (inclusive). Null from the start.</param>
    /// <param name="toByte">Last byte (inclusive). Null to the end.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<Stream, Failure>> DownloadAsync(string nodeId, long? fromByte, long? toByte, CancellationToken ct);

    /// <summary>Exports a Google-native file to a target MIME type. The caller disposes the stream.</summary>
    Task<Result<Stream, Failure>> ExportAsync(string nodeId, string targetMimeType, CancellationToken ct);

    /// <summary>Renames a node.</summary>
    Task<Result<RemoteNode, Failure>> RenameAsync(string nodeId, string newName, CancellationToken ct);

    /// <summary>Moves a node to another folder.</summary>
    Task<Result<RemoteNode, Failure>> MoveAsync(string nodeId, string newParentId, CancellationToken ct);

    /// <summary>Copies a node. Implementations without server-side copy upload a fresh copy.</summary>
    Task<Result<RemoteNode, Failure>> CopyAsync(string nodeId, string newParentId, string? newName, CancellationToken ct);

    /// <summary>Moves a node to trash (reversible).</summary>
    Task<Result<Unit, Failure>> TrashAsync(string nodeId, CancellationToken ct);

    /// <summary>Restores a trashed node.</summary>
    Task<Result<Unit, Failure>> RestoreAsync(string nodeId, CancellationToken ct);

    /// <summary>Permanently deletes a node. Requires an explicit confirmation token upstream.</summary>
    Task<Result<Unit, Failure>> DeletePermanentAsync(string nodeId, CancellationToken ct);

    /// <summary>Lists preserved revisions of a file.</summary>
    Task<Result<IReadOnlyList<RevisionInfo>, Failure>> ListRevisionsAsync(string nodeId, CancellationToken ct);

    /// <summary>Pins a revision against provider-side purging where supported.</summary>
    Task<Result<Unit, Failure>> PinRevisionAsync(string nodeId, string revisionId, CancellationToken ct);

    /// <summary>Writes the human description and compact appProperties facts.</summary>
    Task<Result<Unit, Failure>> SetMetadataAsync(
        string nodeId,
        string? description,
        IReadOnlyDictionary<string, string>? appProperties,
        CancellationToken ct);

    /// <summary>Gets the change-feed start token for incremental sync.</summary>
    Task<Result<string, Failure>> GetStartChangeTokenAsync(CancellationToken ct);

    /// <summary>Polls one page of changes since a token.</summary>
    Task<Result<ChangePage, Failure>> GetChangesAsync(string token, CancellationToken ct);

    /// <summary>Reads storage quota usage.</summary>
    Task<Result<QuotaInfo, Failure>> GetQuotaAsync(CancellationToken ct);

    /// <summary>Reads the provider account identity.</summary>
    Task<Result<ProviderAccountInfo, Failure>> GetAccountInfoAsync(CancellationToken ct);
}
