using System;
using Microsoft.Extensions.Logging;

namespace InkGuide.Tools;

/// <summary>LoggerMessage delegates for the Tools console (CA1848).</summary>
internal static partial class ToolsLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "InkGuide.Tools {Command} started.")]
    public static partial void CommandStarted(ILogger logger, string command);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Bridge assembly loaded from {Path}: {Types} contract types reflected.")]
    public static partial void BridgeReflected(ILogger logger, string path, int types);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Bridge assembly not found at {Path}; emitting baseline contract only.")]
    public static partial void BridgeMissing(ILogger logger, string path);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Wrote {Path} ({Bytes} bytes).")]
    public static partial void WroteFile(ILogger logger, string path, long bytes);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Contract check failed: {Path} is stale. Run contract-gen to refresh it.")]
    public static partial void StaleFile(ILogger logger, string path);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Contract check passed: generated files are fresh.")]
    public static partial void CheckPassed(ILogger logger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Live-check step {Step} skipped: {Reason}.")]
    public static partial void StepSkipped(ILogger logger, string step, string reason);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Live-check step {Step} passed in {Ms} ms.")]
    public static partial void StepPassed(ILogger logger, string step, long ms);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "Live-check step {Step} failed: {Detail}.")]
    public static partial void StepFailed(ILogger logger, Exception ex, string step, string detail);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Seeded {Count} fixture files under {Root}.")]
    public static partial void SeedDone(ILogger logger, int count, string root);
}
