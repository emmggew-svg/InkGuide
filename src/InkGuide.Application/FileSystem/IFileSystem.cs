using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;

namespace InkGuide.Application.FileSystem;

/// <summary>One entry of a recursive directory listing with a repo-relative path.</summary>
/// <param name="RelativePath">Path relative to the listing root, using '/' separators.</param>
/// <param name="IsDirectory">True for directories.</param>
/// <param name="SizeBytes">File size in bytes. Zero for directories.</param>
public sealed record FileSystemEntry(string RelativePath, bool IsDirectory, long SizeBytes);

/// <summary>Minimal file-system abstraction so detectors and importers stay testable without disk access.</summary>
public interface IFileSystem
{
    /// <summary>Lists entries under a root, recursively, with '/'-separated relative paths.</summary>
    Task<Result<IReadOnlyList<FileSystemEntry>, Failure>> ListEntriesAsync(string rootPath, CancellationToken ct);

    /// <summary>Reads a whole text file.</summary>
    Task<Result<string, Failure>> ReadAllTextAsync(string path, CancellationToken ct);

    /// <summary>Reads a whole binary file.</summary>
    Task<Result<byte[], Failure>> ReadAllBytesAsync(string path, CancellationToken ct);

    /// <summary>Checks existence of a file or directory.</summary>
    Task<Result<bool, Failure>> ExistsAsync(string path, CancellationToken ct);
}
