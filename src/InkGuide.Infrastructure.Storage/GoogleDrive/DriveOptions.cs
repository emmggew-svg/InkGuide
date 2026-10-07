using System;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Storage.Abstractions;

namespace InkGuide.Infrastructure.Storage.GoogleDrive;

/// <summary>Options for the Google Drive provider.</summary>
public sealed class GoogleDriveOptions
{
    /// <summary>Owning account id (multi-account model).</summary>
    public string AccountId { get; set; } = "default";

    /// <summary>Full-drive scope (restricted) vs app-created files only (default).</summary>
    public bool FullDrive { get; set; }

    /// <summary>Name-collision policy (default block).</summary>
    public NameCollisionPolicy CollisionPolicy { get; set; } = NameCollisionPolicy.Block;

    /// <summary>Resumable upload chunk size; must be a multiple of 256 KB (default 8 MB).</summary>
    public int ChunkBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Workspace root folder name.</summary>
    public string WorkspaceFolderName { get; set; } = "InkGuide Workspace";
}

/// <summary>Receives resumable-upload session URIs so transfers survive restarts.</summary>
public interface IUploadSessionReporter
{
    Task ReportSessionAsync(string operationId, string sessionUri, CancellationToken cancellationToken);
}
