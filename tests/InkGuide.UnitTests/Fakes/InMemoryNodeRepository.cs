using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InkGuide.Application.Common;
using InkGuide.Application.Repositories;
using InkGuide.Domain.Common;
using InkGuide.Domain.Entities;

namespace InkGuide.UnitTests.Fakes;

/// <summary>In-memory node repository for port-level tests.</summary>
public sealed class InMemoryNodeRepository : INodeRepository
{
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<Result<Node?, Failure>> GetAsync(string accountId, string nodeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _nodes.TryGetValue(Key(accountId, nodeId), out Node? node);
        return Task.FromResult(Result.Success<Node?>(node));
    }

    /// <inheritdoc />
    public Task<Result<PagedResult<Node>, Failure>> ListAsync(
        NodeFilter filter,
        string? cursor,
        int limit,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(filter);
        List<Node> matching = new();
        foreach (Node node in _nodes.Values)
        {
            if (!string.Equals(node.AccountId, filter.AccountId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!filter.IncludeTrashed && node.Trashed)
            {
                continue;
            }

            if (filter.ParentId is not null && !string.Equals(node.ParentId, filter.ParentId, StringComparison.Ordinal))
            {
                continue;
            }

            if (filter.RepositoryId is not null && !string.Equals(node.RepositoryId, filter.RepositoryId, StringComparison.Ordinal))
            {
                continue;
            }

            if (filter.IsFolder.HasValue && node.IsFolder != filter.IsFolder.Value)
            {
                continue;
            }

            matching.Add(node);
        }

        matching.Sort(static (left, right) =>
        {
            int folders = right.IsFolder.CompareTo(left.IsFolder);
            if (folders != 0)
            {
                return folders;
            }

            int names = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            return names != 0 ? names : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        });

        int offset = 0;
        if (cursor is not null)
        {
            _ = int.TryParse(cursor, out offset);
        }

        List<Node> page = new();
        for (int i = offset; i < matching.Count && page.Count < limit; i++)
        {
            page.Add(matching[i]);
        }

        string? next = offset + page.Count < matching.Count
            ? (offset + page.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
        return Task.FromResult(Result.Success(new PagedResult<Node>(page, next, matching.Count)));
    }

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> UpsertAsync(Node node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(node);
        _nodes[Key(node.AccountId, node.Id)] = node;
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<Unit, Failure>> DeleteAsync(string accountId, string nodeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _nodes.Remove(Key(accountId, nodeId));
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<Node>, Failure>> FindByHashAsync(string accountId, string md5, long sizeBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<Node> matching = new();
        foreach (Node node in _nodes.Values)
        {
            if (!string.Equals(node.AccountId, accountId, StringComparison.Ordinal) || node.Trashed || node.IsFolder)
            {
                continue;
            }

            if (node.SizeBytes == sizeBytes && string.Equals(node.Md5Checksum, md5, StringComparison.OrdinalIgnoreCase))
            {
                matching.Add(node);
            }
        }

        return Task.FromResult(Result.Success<IReadOnlyList<Node>>(matching));
    }

    private static string Key(string accountId, string nodeId) => accountId + "\0" + nodeId;
}
