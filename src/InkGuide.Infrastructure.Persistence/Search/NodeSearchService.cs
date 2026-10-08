using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Infrastructure.Persistence.Common;
using InkGuide.Infrastructure.Persistence.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InkGuide.Infrastructure.Persistence.Search;

/// <summary>Full-text search over the node mirror with FTS5 and LIKE fallback.</summary>
public sealed class NodeSearchService
{
    private readonly IDbContextFactory<InkGuideDbContext> _factory;
    private readonly ILogger<NodeSearchService> _logger;
    private readonly PersistenceOptions _options;
    private bool _ftsAvailable;

    public NodeSearchService(IDbContextFactory<InkGuideDbContext> factory, PersistenceOptions options, ILogger<NodeSearchService> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    private static readonly char[] SeparatorChars = new[] { ' ', '\t' };

    /// <summary>Called by bootstrap with the FTS5 probe result.</summary>
    public void SetFtsAvailable(bool available)
    {
        _ftsAvailable = available && !_options.ForceLikeFallback;
    }

    /// <summary>Upserts one FTS document (no-op when FTS is unavailable).</summary>
    public async Task IndexNodeAsync(string nodeId, string name, string? summary, string tags, string path, string? content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(path);
        if (!_ftsAvailable)
        {
            return;
        }

        string clipped = content is null ? string.Empty : Clip(content, _options.MaxIndexedContentBytes);
        try
        {
            await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            _ = await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO node_fts(nodeId, name, summary, tags, path, content) VALUES ({0}, {1}, {2}, {3}, {4}, {5});",
                new object[] { nodeId, name, summary ?? string.Empty, tags, path, clipped },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            PersistenceLog.FtsWriteFailed(_logger, ex);
            _ftsAvailable = false;
        }
        catch (InvalidOperationException ex)
        {
            PersistenceLog.FtsWriteFailed(_logger, ex);
            _ftsAvailable = false;
        }
    }

    /// <summary>Removes one FTS document (no-op when FTS is unavailable).</summary>
    public async Task RemoveNodeAsync(string nodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        if (!_ftsAvailable)
        {
            return;
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        _ = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM node_fts WHERE nodeId = {0};",
            new object[] { nodeId },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a ranked text query; falls back to indexed LIKE when FTS is unavailable.</summary>
    public async Task<Result<IReadOnlyList<SearchHit>>> QueryAsync(string accountId, string text, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(text);
        string needle = text.Trim();
        if (needle.Length == 0)
        {
            return Result.Ok<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
        }

        await using InkGuideDbContext db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (_ftsAvailable)
        {
            try
            {
                string match = ToFtsMatch(needle);
                // SqlQueryRaw (not FromSqlRaw+Select): EF must not wrap the FTS
                // query in a subquery — composition breaks the rank pseudo-column
                // and silently degrades every content search to LIKE.
                List<SearchHit> hits = await db.Database.SqlQueryRaw<SearchHit>(
                    "SELECT n.Id AS NodeId, n.Name AS Name, n.PathText AS Path, n.IsFolder AS IsFolder, NULL AS Highlight FROM Nodes n JOIN node_fts ON node_fts.nodeId = n.Id WHERE n.AccountId = {0} AND node_fts MATCH {1} ORDER BY rank LIMIT {2};",
                    accountId, match, Math.Clamp(limit <= 0 ? 50 : limit, 1, 200)).ToListAsync(cancellationToken).ConfigureAwait(false);
                return Result.Ok<IReadOnlyList<SearchHit>>(hits);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                PersistenceLog.FtsQueryFailed(_logger, ex);
                _ftsAvailable = false;
            }
            catch (InvalidOperationException ex)
            {
                PersistenceLog.FtsQueryFailed(_logger, ex);
                _ftsAvailable = false;
            }
        }

        string like = $"%{EscapeLike(needle)}%";
        List<SearchHit> fallback = await db.Nodes.AsNoTracking()
            .Where(n => n.AccountId == accountId && !n.Trashed &&
                (EF.Functions.Like(n.Name, like) || EF.Functions.Like(n.PathText, like) ||
                 (n.Description != null && EF.Functions.Like(n.Description!, like))))
            .OrderBy(n => n.Name)
            .Take(Math.Clamp(limit <= 0 ? 50 : limit, 1, 200))
            .Select(n => new SearchHit(n.Id, n.Name, n.PathText, n.IsFolder, null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok<IReadOnlyList<SearchHit>>(fallback);
    }

    private static string Clip(string content, int maxBytes)
    {
        if (content.Length <= maxBytes)
        {
            return content;
        }

        return content.Substring(0, maxBytes);
    }

    private static string ToFtsMatch(string needle)
    {
        string[] parts = needle.Split(SeparatorChars, StringSplitOptions.RemoveEmptyEntries);
        string[] cleaned = parts.Select(p => "\"" + p.Replace("\"", string.Empty) + "\"*").ToArray();
        return cleaned.Length == 0 ? "\"\"" : string.Join(" AND ", cleaned);
    }

    private static string EscapeLike(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}

/// <summary>One ranked search hit.</summary>
public sealed record SearchHit(string NodeId, string Name, string Path, bool IsFolder, string? Highlight);
