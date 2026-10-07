namespace InkGuide.Domain;

/// <summary>
/// Single source of InkGuide runtime identity strings (see DECISIONS D-002).
/// The Drive workspace folder name, manifest folder name and Drive
/// <c>appProperties</c> key prefix must be read from here, never hard-coded.
/// </summary>
public static class AppIdentity
{
    /// <summary>Application display name and solution/namespace prefix.</summary>
    public const string AppName = "InkGuide";

    /// <summary>Reverse-style identifier for mutex names, installer id and user-agent suffix.</summary>
    public const string AppId = "inkguide.desktop";

    /// <summary>Name of the root Drive folder created by the app in the connected account.</summary>
    public const string WorkspaceFolderName = "InkGuide Workspace";

    /// <summary>Name of the per-repo manifest folder holding portable JSON state.</summary>
    public const string ManifestFolderName = ".inkguide";

    /// <summary>Prefix for the compact machine facts stored in Drive file appProperties.</summary>
    public const string AppPropertiesPrefix = "inkguide";

    /// <summary>Kind id of the analysed node (compact insight fact).</summary>
    public const string PropertyKind = "inkguide.k";

    /// <summary>Insight source: rule, ai or user (compact insight fact).</summary>
    public const string PropertyInsightSource = "inkguide.s";

    /// <summary>Short hash of the description text the app last wrote (user-edit detection).</summary>
    public const string PropertyDescriptionHash = "inkguide.dh";

    /// <summary>Short hash of the content bytes that were analysed.</summary>
    public const string PropertyContentHash = "inkguide.ch";

    /// <summary>Transient outbox operation id used for idempotent replay.</summary>
    public const string PropertyOperationId = "inkguide.op";

    /// <summary>Version of the shipped analysis ruleset new insights are stamped with.</summary>
    public const string RulesetVersion = "1.0.0";
}
