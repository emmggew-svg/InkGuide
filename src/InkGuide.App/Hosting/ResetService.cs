using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Hosting;

/// <summary>
/// Handles --reset: clears cache, settings and window state after a native
/// confirmation. The local database (including the durable outbox), secrets
/// and downloaded content metadata outside cache/ are preserved, so queued
/// work is never lost by a reset.
/// </summary>
internal sealed class ResetService
{
    private readonly ILogger<ResetService> _logger;

    public ResetService(ILogger<ResetService> logger)
    {
        _logger = logger;
    }

    public bool TryReset(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!NativeDialog.ConfirmReset())
        {
            return false;
        }

        DeleteDirectory(paths.CacheDir);
        DeleteFile(paths.SettingsPath);
        DeleteFile(paths.WindowStatePath);
        AppLogMessages.ResetDone(_logger);
        return true;
    }

    private void DeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            AppLogMessages.ResetDirFailed(_logger, ex);
        }
    }

    private void DeleteFile(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            AppLogMessages.ResetFileFailed(_logger, ex);
        }
    }
}
