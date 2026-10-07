using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Application.Secrets;
using InkGuide.Domain.Common;

namespace InkGuide.UnitTests.Fakes;

/// <summary>In-memory secret store for port-level tests. Production uses DPAPI.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> SetAsync(string key, string value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _secrets[key] = value;
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<string?, Failure>> GetAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _secrets.TryGetValue(key, out string? value);
        return Task.FromResult(Result.Success<string?>(value));
    }

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> DeleteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _secrets.Remove(key);
        return Task.FromResult(Result.Success());
    }
}
