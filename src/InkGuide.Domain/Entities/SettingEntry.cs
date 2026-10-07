namespace InkGuide.Domain.Entities;

/// <summary>One schema-versioned settings entry. Secrets never live here (see ISecretStore).</summary>
public sealed class SettingEntry
{
    /// <summary>Setting key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>JSON-encoded setting value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>When the entry was last written (UTC).</summary>
    public System.DateTimeOffset UpdatedAtUtc { get; set; }
}
