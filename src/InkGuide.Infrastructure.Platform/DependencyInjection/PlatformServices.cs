using System;
using InkGuide.Infrastructure.Platform.Ai;
using InkGuide.Infrastructure.Platform.Cache;
using InkGuide.Infrastructure.Platform.Clock;
using InkGuide.Infrastructure.Platform.ExternalEdit;
using InkGuide.Infrastructure.Platform.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace InkGuide.Infrastructure.Platform.DependencyInjection;

/// <summary>Composition helpers for the platform layer.</summary>
public static class PlatformServices
{
    /// <summary>Adds secrets, cache, external-edit, AI describer, clock/filesystem.</summary>
    public static IServiceCollection AddInkGuidePlatform(
        this IServiceCollection services,
        string secretsDirectory,
        ContentCacheOptions cacheOptions,
        ExternalEditOptions externalEditOptions,
        AnthropicOptions aiOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(secretsDirectory);
        ArgumentNullException.ThrowIfNull(cacheOptions);
        ArgumentNullException.ThrowIfNull(externalEditOptions);
        ArgumentNullException.ThrowIfNull(aiOptions);

        _ = services.AddSingleton<ISecretStore>(new DpapiSecretStore(secretsDirectory));
        _ = services.AddSingleton(cacheOptions);
        _ = services.AddSingleton<IContentCache, FileContentCache>();
        _ = services.AddSingleton(externalEditOptions);
        _ = services.AddSingleton<ExternalEditService>();
        _ = services.AddSingleton(aiOptions);
        _ = services.AddSingleton<IDescriptionProvider, AnthropicDescriptionProvider>();
        _ = services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        _ = services.AddSingleton(TimeProvider.System);
        return services;
    }
}
