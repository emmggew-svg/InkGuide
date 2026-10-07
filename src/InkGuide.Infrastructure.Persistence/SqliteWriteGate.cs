using System;
using System.Threading;
using System.Threading.Tasks;

namespace InkGuide.Infrastructure.Persistence;

/// <summary>Serializes all SQLite writers; readers proceed concurrently (WAL).</summary>
public sealed class SqliteWriteGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Runs the callback while holding the exclusive write lock.</summary>
    public async Task<T> ExecuteWriteAsync<T>(Func<CancellationToken, Task<T>> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await write(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs the callback while holding the exclusive write lock.</summary>
    public async Task ExecuteWriteAsync(Func<CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await write(cancellationToken).ConfigureAwait(false);
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
}
