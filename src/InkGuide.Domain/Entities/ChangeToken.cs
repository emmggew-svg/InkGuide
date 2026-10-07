namespace InkGuide.Domain.Entities;

/// <summary>Persisted change-feed token of one account for incremental sync.</summary>
public sealed class ChangeToken
{
    /// <summary>Owning account id.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Opaque provider change token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>When the token was stored (UTC).</summary>
    public System.DateTimeOffset UpdatedAtUtc { get; set; }
}
