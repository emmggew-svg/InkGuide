using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Hosting;

/// <summary>
/// Opens URLs and folders in the system browser / shell. Only http, https
/// and mailto targets are ever launched; everything else is refused.
/// </summary>
internal static class ExternalBrowser
{
    public static bool IsLaunchable(string? uri)
    {
        if (!Uri.TryCreate(uri ?? string.Empty, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        return parsed.Scheme == Uri.UriSchemeHttp ||
               parsed.Scheme == Uri.UriSchemeHttps ||
               parsed.Scheme == "mailto";
    }

    public static void Open(string? uri, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!IsLaunchable(uri) || uri is null)
        {
            AppLogMessages.LaunchRefused(logger);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo { FileName = uri, UseShellExecute = true };
            Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is IOException)
        {
            AppLogMessages.BrowserOpenFailed(logger, ex);
        }
    }
}
