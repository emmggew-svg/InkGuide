using System;

namespace InkGuide.Infrastructure.Persistence.Entities;

/// <summary>Connected Google account (cached view; tokens live in ISecretStore).</summary>
public sealed class AccountEntity
{
    public string Id { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ScopeMode { get; set; } = "AppFolderOnly";

    public bool IsActive { get; set; }

    public bool ReauthRequired { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Workspace root folder cached view.</summary>
public sealed class WorkspaceEntity
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string ProviderFolderId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset EnsuredAtUtc { get; set; }
}

/// <summary>Repository (single or mono) cached view.</summary>
public sealed class RepositoryEntity
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = "Single";

    public string ProviderFolderId { get; set; } = string.Empty;

    public string ManifestVersion { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Logical project inside a repository.</summary>
public sealed class ProjectEntity
{
    public string Id { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string RepositoryId { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ProjectType { get; set; } = string.Empty;

    public bool Detected { get; set; }

    public bool Confirmed { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
