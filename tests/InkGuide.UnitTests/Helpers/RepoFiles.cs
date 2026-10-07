using System;
using System.IO;

namespace InkGuide.UnitTests.Helpers;

/// <summary>Locates repo files (shipped config) from the test output directory.</summary>
internal static class RepoFiles
{
    /// <summary>Reads a file from the repo config/ folder found by walking up from the test binaries.</summary>
    internal static string ReadConfig(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return File.ReadAllText(ConfigPath(fileName));
    }

    /// <summary>Resolves the path of a file in the repo config/ folder.</summary>
    internal static string ConfigPath(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && directory is not null; i++)
        {
            string candidate = Path.Combine(directory, "config", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException(fileName);
    }
}
