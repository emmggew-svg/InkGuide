using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Platform.Secrets;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.GoogleDrive;
using InkGuide.Infrastructure.Storage.LocalFolder;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge.Backend;

/// <summary>Resolves the active storage provider for an account (Drive with tokens, else LocalFolder).</summary>
public interface IProviderResolver
{
    Task<ResolvedProvider?> ResolveAsync(string accountId, CancellationToken cancellationToken);
}

/// <summary>Default provider resolution (single active Drive account, LocalFolder fallback).</summary>
public sealed class ProviderResolver : IProviderResolver
{
    private const string LocalAccountId = "local";

    private readonly BackendCompositionOptions _options;
    private readonly BackendSettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly GoogleOAuthService _oauth;
    private readonly GoogleOAuthOptions _oauthOptions;
    private readonly GoogleDriveOptions _driveOptions;
    private readonly DriveResilience _resilience;
    private readonly LocalFolderProvider _local;
    private readonly LocalFolderOptions _localOptions;
    private readonly IHttpClientFactory _http;
    private readonly TimeProvider _clock;
    private readonly ILoggerFactory _loggerFactory;

    public ProviderResolver(
        BackendCompositionOptions options,
        BackendSettingsService settings,
        ISecretStore secrets,
        GoogleOAuthService oauth,
        GoogleOAuthOptions oauthOptions,
        GoogleDriveOptions driveOptions,
        DriveResilience resilience,
        LocalFolderProvider local,
        LocalFolderOptions localOptions,
        IHttpClientFactory http,
        TimeProvider clock,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(oauth);
        ArgumentNullException.ThrowIfNull(oauthOptions);
        ArgumentNullException.ThrowIfNull(driveOptions);
        ArgumentNullException.ThrowIfNull(resilience);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(localOptions);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _options = options;
        _settings = settings;
        _secrets = secrets;
        _oauth = oauth;
        _oauthOptions = oauthOptions;
        _driveOptions = driveOptions;
        _resilience = resilience;
        _local = local;
        _localOptions = localOptions;
        _http = http;
        _clock = clock;
        _loggerFactory = loggerFactory;
    }

    public async Task<ResolvedProvider?> ResolveAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        if (!string.Equals(accountId, LocalAccountId, StringComparison.Ordinal))
        {
            string? token = null;
            try
            {
                token = await _secrets.GetAsync(_oauthOptions.TokenKeyPrefix + accountId, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (!string.IsNullOrEmpty(token))
            {
                string scope = await _settings.GetAsync(BackendSettingsService.Keys.DriveScopeMode, cancellationToken).ConfigureAwait(false);
                var driveOptions = new GoogleDriveOptions
                {
                    AccountId = accountId,
                    FullDrive = string.Equals(scope, "FullDrive", StringComparison.Ordinal),
                    CollisionPolicy = _driveOptions.CollisionPolicy,
                    ChunkBytes = _driveOptions.ChunkBytes,
                    WorkspaceFolderName = _options.WorkspaceFolderName,
                };
                var provider = new GoogleDriveProvider(
                    _http.CreateClient("inkguide"),
                    driveOptions,
                    _oauth,
                    _resilience,
                    _clock,
                    _loggerFactory.CreateLogger<GoogleDriveProvider>());
                return new ResolvedProvider(provider, "drive:" + accountId, accountId);
            }
        }

        string enabled = await _settings.GetAsync(BackendSettingsService.Keys.LocalProviderEnabled, cancellationToken).ConfigureAwait(false);
        bool on = _localOptions.Enabled || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
        if (on && !string.IsNullOrWhiteSpace(_localOptions.RootDirectory))
        {
            _localOptions.Enabled = true;
            return new ResolvedProvider(_local, "local", accountId);
        }

        return null;
    }
}
