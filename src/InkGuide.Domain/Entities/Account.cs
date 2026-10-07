namespace InkGuide.Domain.Entities;

/// <summary>A connected Google account. The model supports many; at least one must be active for sync.</summary>
public sealed class Account
{
    /// <summary>Opaque account id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Account e-mail address from the provider.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name from the provider.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Current connectivity status.</summary>
    public AccountStatus Status { get; set; } = AccountStatus.Active;

    /// <summary>OAuth scope mode granted for this account.</summary>
    public DriveScopeMode ScopeMode { get; set; } = DriveScopeMode.AppFolderOnly;

    /// <summary>When the account was first connected (UTC).</summary>
    public System.DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>When the account last synced successfully (UTC). Null when never synced.</summary>
    public System.DateTimeOffset? LastSyncAtUtc { get; set; }
}
