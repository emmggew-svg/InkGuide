using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace InkGuide.App.Hosting;

/// <summary>
/// Configures file logging: rolling files, 10 MB x 10, enriched with a
/// redaction filter so tokens and keys never reach disk (Section 18).
/// </summary>
internal static class SerilogSetup
{
    public static ILoggerFactory CreateFactory(AppPaths paths, string logLevelName)
    {
        ArgumentNullException.ThrowIfNull(paths);

        LogEventLevel level = logLevelName.ToUpperInvariant() switch
        {
            "DEBUG" => LogEventLevel.Debug,
            "TRACE" => LogEventLevel.Verbose,
            "WARNING" => LogEventLevel.Warning,
            "ERROR" => LogEventLevel.Error,
            _ => LogEventLevel.Information,
        };

        string logFile = Path.Combine(paths.LogsDir, "inkguide-.log");

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .Enrich.FromLogContext()
            .Destructure.ByTransforming<Exception>(ex => LogRedaction.Redact(ex.ToString()))
            .WriteTo.File(
                logFile,
                rollingInterval: RollingInterval.Day,
                formatProvider: CultureInfo.InvariantCulture,
                retainedFileCountLimit: 10,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");

        Log.Logger = loggerConfig.CreateLogger();

        return LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, dispose: true));
    }
}
