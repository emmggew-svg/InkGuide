using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace InkGuide.Infrastructure.Platform.Clock;

/// <summary>Minimal file-system abstraction (testability seam).</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken);
}

/// <summary>Physical file-system implementation.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool FileExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return File.Exists(path);
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Directory.Exists(path);
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }
}
