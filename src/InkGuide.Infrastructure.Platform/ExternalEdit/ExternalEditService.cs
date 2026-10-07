using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Platform.Common;
using InkGuide.Infrastructure.Platform.Logging;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Platform.ExternalEdit;

// Local seam: the canonical IExternalEditHost port is owned by Application (see D-006).

/// <summary>Handles a detected external change (queues an outbox update with conflict check).</summary>
public interface IExternalChangeHandler
{
    Task HandleExternalChangeAsync(string sessionId, CancellationToken cancellationToken);
}

/// <summary>External-edit session descriptor.</summary>
public sealed record ExternalSession(
    string SessionId,
    string AccountId,
    string NodeId,
    string FileName,
    string LocalPath,
    string BaseVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastSyncUtc);

/// <summary>Options for external-edit sessions.</summary>
public sealed class ExternalEditOptions
{
    public string RootDirectory { get; set; } = string.Empty;

    public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>
/// Opens files in the default app and watches them back (FileSystemWatcher + debounce +
/// lock detection; handles save-via-temp-and-rename). Sessions survive restarts.
/// Executable/script extensions require an explicit confirmation token (checked by Bridge).
/// </summary>
public sealed class ExternalEditService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".msi", ".js", ".vbs", ".wsf", ".lnk", ".scr",
        ".com", ".jar", ".reg", ".hta", ".cpl", ".msc", ".vb", ".vbe", ".jse", ".ws",
        ".wsh", ".ps1xml", ".psm1", ".gadget",
    };

    private readonly ExternalEditOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExternalEditService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, WatcherState> _watchers = new(StringComparer.Ordinal);
    private bool _disposed;

    public ExternalEditService(ExternalEditOptions options, TimeProvider clock, ILogger<ExternalEditService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Handler invoked on debounced external changes (wired by the host).</summary>
    public IExternalChangeHandler? ChangeHandler { get; set; }

    /// <summary>Whether opening this file externally needs an explicit confirmation token.</summary>
    public static bool RequiresConfirmation(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return RiskyExtensions.Contains(Path.GetExtension(fileName));
    }

    /// <summary>Starts a session: materializes content and begins watching.</summary>
    public async Task<Result<ExternalSession>> StartSessionAsync(
        string accountId,
        string nodeId,
        string fileName,
        string baseVersion,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(baseVersion);
        ArgumentNullException.ThrowIfNull(content);
        string sessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string dir = Path.Combine(_options.RootDirectory, sessionId);
        string path = Path.Combine(dir, SanitizeFileName(fileName));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(dir);
            await using (FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            var session = new ExternalSession(sessionId, accountId, nodeId, fileName, path, baseVersion, _clock.GetUtcNow(), _clock.GetUtcNow());
            Dictionary<string, ExternalSession> sessions = await LoadSessionsInternalAsync(cancellationToken).ConfigureAwait(false);
            sessions[sessionId] = session;
            await SaveSessionsInternalAsync(sessions, cancellationToken).ConfigureAwait(false);
            StartWatcher(session);
            return Result.Ok(session);
        }
        catch (IOException ex)
        {
            return Result.Fail<ExternalSession>(OperationError.Provider("External session failed: " + ex.Message, true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<ExternalSession>(OperationError.Provider("External session failed: " + ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Lists all known sessions.</summary>
    public async Task<Result<IReadOnlyList<ExternalSession>>> ListSessionsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, ExternalSession> sessions = await LoadSessionsInternalAsync(cancellationToken).ConfigureAwait(false);
            return Result.Ok<IReadOnlyList<ExternalSession>>(sessions.Values.OrderBy(s => s.StartedAtUtc).ToList());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ends a session (stops watching; optionally deletes the local file).</summary>
    public async Task<Result> EndSessionAsync(string sessionId, bool deleteFile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, ExternalSession> sessions = await LoadSessionsInternalAsync(cancellationToken).ConfigureAwait(false);
            if (sessions.TryGetValue(sessionId, out ExternalSession? session))
            {
                _ = sessions.Remove(sessionId);
                await SaveSessionsInternalAsync(sessions, cancellationToken).ConfigureAwait(false);
                if (deleteFile)
                {
                    try
                    {
                        if (File.Exists(session.LocalPath))
                        {
                            File.Delete(session.LocalPath);
                        }
                    }
                    catch (IOException)
                    {
                        // Best effort.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Best effort.
                    }
                }
            }

            StopWatcher(sessionId);
            return Result.Ok();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Re-adopts sessions after a restart; drops stale ones.</summary>
    public async Task<Result<int>> ReadoptAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, ExternalSession> sessions = await LoadSessionsInternalAsync(cancellationToken).ConfigureAwait(false);
            int readopted = 0;
            foreach (string id in sessions.Keys.ToList())
            {
                ExternalSession session = sessions[id];
                bool stale = _clock.GetUtcNow() - session.StartedAtUtc > _options.SessionTtl || !File.Exists(session.LocalPath);
                if (stale)
                {
                    _ = sessions.Remove(id);
                    PlatformLog.SessionDropped(_logger, id);
                    continue;
                }

                StartWatcher(session);
                PlatformLog.SessionReadopted(_logger, id, session.FileName);
                readopted++;
            }

            await SaveSessionsInternalAsync(sessions, cancellationToken).ConfigureAwait(false);
            return Result.Ok(readopted);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (WatcherState state in _watchers.Values)
        {
            state.Watcher.Dispose();
            state.Timer.Dispose();
        }

        _watchers.Clear();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private void StartWatcher(ExternalSession session)
    {
        StopWatcher(session.SessionId);
        string? dir = Path.GetDirectoryName(session.LocalPath);
        if (dir is null)
        {
            return;
        }

        var watcher = new FileSystemWatcher(dir)
        {
            Filter = Path.GetFileName(session.LocalPath),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        var state = new WatcherState(session.SessionId, watcher, CreateDebounceTimer(session.SessionId));
        watcher.Changed += (_, _) => ArmTimer(state);
        watcher.Created += (_, _) => ArmTimer(state);
        watcher.Renamed += (_, _) => ArmTimer(state);
        _watchers[session.SessionId] = state;
    }

    private void StopWatcher(string sessionId)
    {
        if (_watchers.TryGetValue(sessionId, out WatcherState? state))
        {
            _ = _watchers.Remove(sessionId);
            state.Watcher.Dispose();
            state.Timer.Dispose();
        }
    }

    private Timer CreateDebounceTimer(string sessionId)
    {
        return new Timer(
            state =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await OnDebouncedAsync((string)state!, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                });
            },
            sessionId,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    private static void ArmTimer(WatcherState state)
    {
        try
        {
            _ = state.Timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Watcher is shutting down.
        }
    }

    private async Task OnDebouncedAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_watchers.TryGetValue(sessionId, out WatcherState? state))
        {
            return;
        }

        string? path = state.Watcher.Path is null ? null : Path.Combine(state.Watcher.Path, state.Watcher.Filter);
        if (path is null || !IsStable(path))
        {
            ArmTimer(state);
            return;
        }

        IExternalChangeHandler? handler = ChangeHandler;
        if (handler is null)
        {
            return;
        }

        try
        {
            await handler.HandleExternalChangeAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PlatformLog.ExternalHandlerFailed(_logger, ex, sessionId);
        }
    }

    private static bool IsStable(string path)
    {
        try
        {
            long first = new FileInfo(path).Length;
            using (FileStream probe = new(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, useAsync: false))
            {
                _ = probe;
            }

            return first >= 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<Dictionary<string, ExternalSession>> LoadSessionsInternalAsync(CancellationToken cancellationToken)
    {
        string path = SessionsPath();
        if (!File.Exists(path))
        {
            return new Dictionary<string, ExternalSession>(StringComparer.Ordinal);
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, ExternalSession>>(json, JsonOptions)
                ?? new Dictionary<string, ExternalSession>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new Dictionary<string, ExternalSession>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, ExternalSession>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, ExternalSession>(StringComparer.Ordinal);
        }
    }

    private async Task SaveSessionsInternalAsync(Dictionary<string, ExternalSession> sessions, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.RootDirectory);
        string temp = SessionsPath() + ".tmp";
        string json = JsonSerializer.Serialize(sessions, JsonOptions);
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, SessionsPath(), overwrite: true);
    }

    private string SessionsPath() => Path.Combine(_options.RootDirectory, "sessions.json");

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Trim().Where(c => Array.IndexOf(invalid, c) < 0).ToArray());
        string result = cleaned.Trim().TrimEnd('.', ' ');
        return result.Length switch
        {
            0 => "external-file",
            > 150 => new string(result.AsSpan(0, 150)),
            _ => result,
        };
    }

    private sealed class WatcherState
    {
        public WatcherState(string sessionId, FileSystemWatcher watcher, Timer timer)
        {
            SessionId = sessionId;
            Watcher = watcher;
            Timer = timer;
        }

        public string SessionId { get; }

        public FileSystemWatcher Watcher { get; }

        public Timer Timer { get; }
    }
}
