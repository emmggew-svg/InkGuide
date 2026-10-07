using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Util.Store;
using InkGuide.Infrastructure.Storage.Common;
using Newtonsoft.Json;

namespace InkGuide.Infrastructure.Storage.GoogleDrive;

/// <summary>
/// Custom <see cref="IDataStore"/> (Google.Apis.Auth) backed by DPAPI-protected
/// storage. No plaintext tokens on disk, ever.
/// </summary>
public sealed class SecretStoreDataStore : IDataStore
{
    private readonly ISecretBackend _backend;
    private readonly string _prefix;

    public SecretStoreDataStore(ISecretBackend backend, string prefix = "datastore-")
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(prefix);
        _backend = backend;
        _prefix = prefix;
    }

    /// <inheritdoc />
    public async Task StoreAsync<T>(string key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        string json = JsonConvert.SerializeObject(value);
        Result stored = await _backend.SetAsync(_prefix + key, json, CancellationToken.None).ConfigureAwait(false);
        if (!stored.IsSuccess)
        {
            throw new InvalidOperationException("Token store write failed: " + stored.Failure?.Message);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync<T>(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = await _backend.DeleteAsync(_prefix + key, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T> GetAsync<T>(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Result<string?> stored = await _backend.GetAsync(_prefix + key, CancellationToken.None).ConfigureAwait(false);
        if (!stored.IsSuccess || string.IsNullOrEmpty(stored.Value))
        {
            return default!;
        }

        T? value = JsonConvert.DeserializeObject<T>(stored.Value!);
        return value!;
    }

    /// <inheritdoc />
    public async Task ClearAsync()
    {
        Result<System.Collections.Generic.IReadOnlyList<string>> keys = await _backend.ListKeysAsync(_prefix, CancellationToken.None).ConfigureAwait(false);
        if (!keys.IsSuccess || keys.Value is null)
        {
            return;
        }

        foreach (string key in keys.Value)
        {
            _ = await _backend.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
