using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Platform.Secrets;
using InkGuide.Infrastructure.Storage.Common;
using InkGuide.Infrastructure.Storage.GoogleDrive;
using Microsoft.Extensions.DependencyInjection;

namespace InkGuide.App.Bridge.Backend;

/// <summary>
/// Adapts Platform's DPAPI secret store (plain async) to Storage's
/// result-returning secret backend seam.
/// </summary>
public sealed class SecretBackendAdapter : ISecretBackend
{
    private readonly ISecretStore _secrets;
    private readonly string _directory;

    public SecretBackendAdapter(ISecretStore secrets, string secretsDirectory)    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(secretsDirectory);
        _secrets = secrets;
        _directory = secretsDirectory;
    }

    public async Task<Result<string?>> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            string? value = await _secrets.GetAsync(key, cancellationToken).ConfigureAwait(false);
            return Result.Ok<string?>(value);
        }
        catch (IOException ex)
        {
            return Result.Fail<string?>(OperationError.Provider("Secret read failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<string?>(OperationError.Provider("Secret read failed: " + ex.Message));
        }
    }

    public async Task<Result> SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        try
        {
            await _secrets.SetAsync(key, value, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Secret write failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Secret write failed: " + ex.Message));
        }
    }

    public async Task<Result<IReadOnlyList<string>>> ListKeysAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        try
        {
            if (!Directory.Exists(_directory))
            {
                return Result.Ok<IReadOnlyList<string>>(Array.Empty<string>());
            }

            string[] files = await Task.Run(() => Directory.GetFiles(_directory, "*.dat"), cancellationToken).ConfigureAwait(false);
            var keys = new List<string>();
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    keys.Add(name);
                }
            }

            return Result.Ok<IReadOnlyList<string>>(keys);
        }
        catch (IOException ex)
        {
            return Result.Fail<IReadOnlyList<string>>(OperationError.Provider("Secret list failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<IReadOnlyList<string>>(OperationError.Provider("Secret list failed: " + ex.Message));
        }
    }

    public async Task<Result> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _secrets.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            return Result.Fail(OperationError.Provider("Secret delete failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail(OperationError.Provider("Secret delete failed: " + ex.Message));
        }
    }
}

/// <summary>Shared HttpClient holder for OAuth + Drive (built on IHttpClientFactory).</summary>
internal static class HttpBridgeRegistration
{
    public static IServiceCollection AddHttpBridge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddSingleton(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            return new HttpClientBridge(factory.CreateClient("inkguide"));
        });
        return services;
    }
}
