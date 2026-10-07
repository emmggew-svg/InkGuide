using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;

namespace InkGuide.Application.Caching;

/// <summary>Cache usage statistics.</summary>
/// <param name="EntryCount">Number of cached entries.</param>
/// <param name="TotalBytes">Total cached bytes.</param>
/// <param name="LimitBytes">Configured cache cap in bytes.</param>
public sealed record CacheStats(long EntryCount, long TotalBytes, long LimitBytes);

/// <summary>Disposable local content cache backing previews and offline reads. LRU-evicted under a byte cap.</summary>
public interface IContentCache
{
    /// <summary>Stores content under a key, evicting least-recently-used entries over the cap.</summary>
    Task<Result<Unit, Failure>> StoreAsync(string key, Stream content, CancellationToken ct);

    /// <summary>Opens cached content for reading. The caller owns and must dispose the returned stream.</summary>
    Task<Result<Stream, Failure>> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>Removes one entry. Succeeds when the key is already absent.</summary>
    Task<Result<Unit, Failure>> RemoveAsync(string key, CancellationToken ct);

    /// <summary>Clears the whole cache.</summary>
    Task<Result<Unit, Failure>> ClearAsync(CancellationToken ct);

    /// <summary>Reads cache usage statistics.</summary>
    Task<Result<CacheStats, Failure>> GetStatsAsync(CancellationToken ct);
}
