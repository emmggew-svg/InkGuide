using System.Collections.Generic;

namespace InkGuide.Domain.Entities;

/// <summary>
/// A logical unit inside a repository. In a Single repo the repo itself is the
/// one project; in a Mono repo each detected or declared sub-folder is a project.
/// </summary>
public sealed class Project
{
    /// <summary>Opaque project id (local).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Containing repository id.</summary>
    public string RepositoryId { get; set; } = string.Empty;

    /// <summary>Repo-relative path of the project root, using '/' separators. Empty for a Single repo root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Project display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Detector type id (for example <c>web-nextjs</c>). See the project-marker catalog.</summary>
    public string TypeId { get; set; } = string.Empty;

    /// <summary>Applied tag names (for example <c>type:web-site</c>).</summary>
    public IReadOnlyList<string> Tags { get; set; } = System.Array.Empty<string>();

    /// <summary>True when the project was found by a detector rather than declared by the user.</summary>
    public bool Detected { get; set; }

    /// <summary>True when the user confirmed an auto-detected project.</summary>
    public bool Confirmed { get; set; }
}
