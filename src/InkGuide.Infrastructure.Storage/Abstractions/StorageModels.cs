using System;
using System.Collections.Generic;

namespace InkGuide.Infrastructure.Storage.Abstractions;

/// <summary>OAuth scope mode: least-privilege file scope or full drive (restricted).</summary>
public enum DriveScopeMode
{
    AppFolderOnly,
    FullDrive,
}

/// <summary>Name-collision policy for providers.</summary>
public enum NameCollisionPolicy
{
    Block,
    AutoSuffix,
}

/// <summary>Provider capability flags.</summary>
public sealed record StorageCapabilities(
    bool SupportsRevisions,
    bool SupportsExport,
    bool SupportsChangeFeed,
    bool SupportsServerSideCopy,
    int MaxChunkBytes,
    bool ChangeFeedResumable);

/// <summary>Cached-view item returned by providers (mirrors the Drive fields contract).</summary>
public sealed record StorageItem(
    string Id,
    string? ParentId,
    string Name,
    bool IsFolder,
    string? MimeType,
    long Size,
    string? Md5,
    string? Version,
    string? HeadRevisionId,
    DateTimeOffset ModifiedTimeUtc,
    DateTimeOffset CreatedTimeUtc,
    bool Trashed,
    bool ExplicitlyTrashed,
    string? Description,
    IReadOnlyDictionary<string, string> AppProperties,
    string? WebViewLink,
    bool CanEdit,
    bool CanDelete,
    bool CanTrash,
    bool CanRename);

/// <summary>One page of children.</summary>
public sealed record StorageItemPage(IReadOnlyList<StorageItem> Items, string? NextCursor);

/// <summary>One change-feed entry.</summary>
public sealed record StorageChange(string ItemId, StorageItem? Item, bool Removed);

/// <summary>Change-feed poll result.</summary>
public sealed record StorageChangeSet(IReadOnlyList<StorageChange> Changes, string NextToken, bool MustReindex);

/// <summary>File revision descriptor.</summary>
public sealed record StorageRevision(
    string Id,
    string FileId,
    DateTimeOffset ModifiedTimeUtc,
    long Size,
    string? Md5,
    bool KeepForever);

/// <summary>Connected account info.</summary>
public sealed record AccountInfo(string Id, string Email, string DisplayName);

/// <summary>Storage quota info.</summary>
public sealed record QuotaInfo(long? LimitBytes, long UsageBytes);

/// <summary>Byte range for partial downloads.</summary>
public sealed record ContentRange(long From, long? To);
