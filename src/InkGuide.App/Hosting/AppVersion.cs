using System;
using System.Reflection;

namespace InkGuide.App.Hosting;

/// <summary>Host version stamped from the assembly (also stamped by publish.cmd).</summary>
internal static class AppVersion
{
    public static string Current
    {
        get
        {
            string? informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                int plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        }
    }
}
