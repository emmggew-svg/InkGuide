using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Domain.Common;

namespace InkGuide.Application.ExternalEdits;

/// <summary>One external-edit session: a node checked out to a local path and watched for changes.</summary>
/// <param name="NodeId">Edited node id.</param>
/// <param name="LocalPath">Local checkout path.</param>
/// <param name="StartedAtUtc">When the session started (UTC).</param>
/// <param name="LastWriteObservedUtc">Last observed local write (UTC). Null when untouched.</param>
public sealed record ExternalEditSession(
    string NodeId,
    string LocalPath,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? LastWriteObservedUtc);

/// <summary>Hosts external-edit sessions (binaries, big files, Office files) with change watch and sync-back.</summary>
public interface IExternalEditHost
{
    /// <summary>Starts a session: downloads to a session path and opens it in the default app upstream.</summary>
    Task<Result<ExternalEditSession, Failure>> StartAsync(string nodeId, string localPath, CancellationToken ct);

    /// <summary>Lists live sessions (survives restarts via re-adoption).</summary>
    Task<Result<IReadOnlyList<ExternalEditSession>, Failure>> ListAsync(CancellationToken ct);

    /// <summary>Ends a session, syncing back any pending change first.</summary>
    Task<Result<Unit, Failure>> EndAsync(string nodeId, CancellationToken ct);
}
