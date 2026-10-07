using System;
using InkGuide.Infrastructure.Storage.Abstractions;

namespace InkGuide.Infrastructure.Storage.LocalFolder;

/// <summary>Options for the LocalFolder provider (dev/test; hidden unless enabled).</summary>
public sealed class LocalFolderOptions
{
    /// <summary>Backing directory for the provider.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>Enabled via --local-provider or setting. Hidden in Release unless on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Name-collision policy (default block).</summary>
    public NameCollisionPolicy CollisionPolicy { get; set; } = NameCollisionPolicy.Block;
}
