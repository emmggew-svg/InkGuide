using System;
using InkGuide.Infrastructure.Storage.Abstractions;
using InkGuide.Infrastructure.Storage.GoogleDrive;
using InkGuide.Infrastructure.Storage.LocalFolder;
using Microsoft.Extensions.DependencyInjection;

namespace InkGuide.Infrastructure.Storage.DependencyInjection;

/// <summary>Composition helpers for the storage layer.</summary>
public static class StorageServices
{
    /// <summary>Adds LocalFolder + GoogleDrive providers and manifest support.</summary>
    public static IServiceCollection AddInkGuideStorage(
        this IServiceCollection services,
        LocalFolderOptions localOptions,
        GoogleDriveOptions driveOptions,
        DriveResilienceOptions resilienceOptions,
        GoogleOAuthOptions oauthOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(localOptions);
        ArgumentNullException.ThrowIfNull(driveOptions);
        ArgumentNullException.ThrowIfNull(resilienceOptions);
        ArgumentNullException.ThrowIfNull(oauthOptions);

        _ = services.AddSingleton(localOptions);
        _ = services.AddSingleton(driveOptions);
        _ = services.AddSingleton(resilienceOptions);
        _ = services.AddSingleton(oauthOptions);
        _ = services.AddSingleton(new DriveManifestOptions());
        _ = services.AddSingleton<LocalFolderProvider>();
        _ = services.AddSingleton<DriveResilience>();
        _ = services.AddSingleton<GoogleOAuthService>();
        _ = services.AddSingleton<SecretStoreDataStore>();
        _ = services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<LocalFolderProvider>());
        _ = services.AddHttpClient<GoogleDriveProvider>();
        return services;
    }
}
