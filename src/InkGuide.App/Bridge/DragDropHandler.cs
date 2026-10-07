using System;
using System.Collections.Generic;
using System.IO;
using InkGuide.App.Hosting;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Bridge;

/// <summary>
/// Validates native drag-and-drop paths received via
/// postMessageWithAdditionalObjects: existence/readability checks, recursive
/// directory expansion, reparse-point (symlink/junction) rejection so drops
/// can never escape the intended tree. Bytes never pass through JavaScript.
/// </summary>
internal sealed class DragDropHandler
{
    private const int MaxEntries = 5000;

    private readonly ILogger<DragDropHandler> _logger;

    public DragDropHandler(ILogger<DragDropHandler> logger)
    {
        _logger = logger;
    }

    public DragDropResult Validate(IReadOnlyList<string> nativePaths)
    {
        ArgumentNullException.ThrowIfNull(nativePaths);

        var accepted = new List<string>();
        var rejected = new List<string>();

        foreach (string raw in nativePaths)
        {
            if (accepted.Count >= MaxEntries)
            {
                rejected.Add(raw);
                continue;
            }

            string path;
            try
            {
                path = Path.GetFullPath(raw);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                rejected.Add(raw);
                continue;
            }

            if (HasReparsePoint(path))
            {
                AppLogMessages.ReparseRejected(_logger, path);
                rejected.Add(raw);
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    accepted.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    ExpandDirectory(path, accepted, rejected);
                }
                else
                {
                    rejected.Add(raw);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppLogMessages.UnreadablePath(_logger, ex);
                rejected.Add(raw);
            }
        }

        return new DragDropResult(accepted.ToArray(), rejected.ToArray());
    }

    private void ExpandDirectory(string root, List<string> accepted, List<string> rejected)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0 && accepted.Count < MaxEntries)
        {
            string current = stack.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                rejected.Add(current);
                AppLogMessages.UnreadableDir(_logger, ex);
                continue;
            }

            foreach (string entry in entries)
            {
                if (accepted.Count >= MaxEntries)
                {
                    rejected.Add(entry);
                    continue;
                }

                if (HasReparsePoint(entry))
                {
                    AppLogMessages.ReparseEntry(_logger);
                    rejected.Add(entry);
                    continue;
                }

                try
                {
                    if (File.Exists(entry))
                    {
                        accepted.Add(entry);
                    }
                    else if (Directory.Exists(entry))
                    {
                        stack.Push(entry);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    rejected.Add(entry);
                    AppLogMessages.UnreadableEntry(_logger, ex);
                }
            }
        }
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            string? current = Path.GetFullPath(path);
            while (current is not null)
            {
                FileAttributes attributes = File.Exists(current)
                    ? File.GetAttributes(current)
                    : Directory.Exists(current)
                        ? new DirectoryInfo(current).Attributes
                        : (FileAttributes)0;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (current is null)
                {
                    return false;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            return true;
        }
    }
}

internal sealed record DragDropResult(string[] Accepted, string[] Rejected);
