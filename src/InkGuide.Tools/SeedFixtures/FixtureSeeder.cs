using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace InkGuide.Tools.SeedFixtures;

/// <summary>
/// Generates deterministic fixture trees (static site, Next.js, Vite+React,
/// WordPress, .NET WPF, Minecraft plugin, Python, mixed mono-repo) for the
/// analysis detector tests. All content is fixed literals; no randomness.
/// </summary>
internal sealed class FixtureSeeder
{
    private readonly ILogger<FixtureSeeder> _logger;

    public FixtureSeeder(ILogger<FixtureSeeder> logger)
    {
        _logger = logger;
    }

    public async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? outDir = ValueOf(args, "--out");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            Console.WriteLine("seed-fixtures: missing required --out <dir>");
            return 2;
        }

        string? only = ValueOf(args, "--only");
        int count = 0;
        foreach ((string name, IReadOnlyDictionary<string, string> files) in Fixtures())
        {
            if (only is not null && !name.Equals(only, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach ((string relative, string content) in files)
            {
                string path = Path.Combine(outDir, name, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
                count++;
            }
        }

        if (only is not null && count == 0)
        {
            Console.WriteLine("seed-fixtures: unknown fixture '" + only + "'. Known: " + string.Join(", ", Fixtures().Select(f => f.Name)));
            return 2;
        }

        ToolsLog.SeedDone(_logger, count, Path.GetFullPath(outDir));
        Console.WriteLine("seed-fixtures: wrote " + count + " files under " + Path.GetFullPath(outDir));
        return 0;
    }

    private static string? ValueOf(string[] args, string flag)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == flag)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static IEnumerable<(string Name, IReadOnlyDictionary<string, string> Files)> Fixtures()
    {
        yield return ("static-site", new Dictionary<string, string>
        {
            ["index.html"] = "<!doctype html>\n<html><head><title>Fixture</title><link rel=\"stylesheet\" href=\"styles.css\"></head><body><script src=\"app.js\"></script></body></html>\n",
            ["styles.css"] = "body { margin: 0; }\n",
            ["app.js"] = "console.log('fixture');\n",
            ["README.md"] = "# Fixture static site\n",
        });
        yield return ("nextjs-site", new Dictionary<string, string>
        {
            ["package.json"] = "{\"name\":\"fixture-next\",\"dependencies\":{\"next\":\"14.0.0\",\"react\":\"18.0.0\"}}\n",
            ["next.config.js"] = "module.exports = {};\n",
            ["app/page.tsx"] = "export default function Page() { return <h1>hi</h1>; }\n",
            ["tsconfig.json"] = "{\"compilerOptions\":{\"strict\":true}}\n",
        });
        yield return ("vite-react", new Dictionary<string, string>
        {
            ["package.json"] = "{\"name\":\"fixture-vite\",\"dependencies\":{\"react\":\"18.0.0\"},\"devDependencies\":{\"vite\":\"5.0.0\"}}\n",
            ["vite.config.ts"] = "import { defineConfig } from 'vite';\nexport default defineConfig({});\n",
            ["index.html"] = "<!doctype html>\n<html><body><div id=\"root\"></div><script src=\"/src/main.tsx\"></script></body></html>\n",
            ["src/main.tsx"] = "import React from 'react';\nconsole.log(React.version);\n",
        });
        yield return ("wordpress", new Dictionary<string, string>
        {
            ["wp-config.php"] = "<?php // fixture\ndefine('DB_NAME', 'fixture');\n",
            ["wp-content/themes/fixture/index.php"] = "<?php get_header(); ?>\n",
            ["wp-content/themes/fixture/style.css"] = "/* Theme Name: Fixture */\n",
        });
        yield return ("dotnet-wpf", new Dictionary<string, string>
        {
            ["fixture.sln"] = "Microsoft Visual Studio Solution File, Format Version 12.00\n",
            ["src/App/App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>WinExe</OutputType>\n    <TargetFramework>net9.0-windows</TargetFramework>\n    <UseWPF>true</UseWPF>\n  </PropertyGroup>\n</Project>\n",
            ["src/App/App.xaml"] = "<Application xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />\n",
        });
        yield return ("minecraft-plugin", new Dictionary<string, string>
        {
            ["plugin.yml"] = "name: Fixture\nversion: 1.0.0\nmain: com.example.Fixture\n",
            ["pom.xml"] = "<project><groupId>com.example</groupId><artifactId>fixture</artifactId></project>\n",
            ["src/main/java/com/example/Fixture.java"] = "package com.example;\npublic class Fixture {}\n",
        });
        yield return ("python-project", new Dictionary<string, string>
        {
            ["pyproject.toml"] = "[project]\nname = \"fixture\"\nversion = \"0.1.0\"\n",
            ["requirements.txt"] = "requests==2.0.0\n",
            ["src/fixture/__init__.py"] = "\"\"\"Fixture package.\"\"\"\n",
        });
        yield return ("mixed-mono", new Dictionary<string, string>
        {
            ["package.json"] = "{\"private\":true,\"workspaces\":[\"packages/*\"]}\n",
            ["pnpm-workspace.yaml"] = "packages:\n  - 'packages/*'\n",
            ["packages/web/package.json"] = "{\"name\":\"web\",\"dependencies\":{\"react\":\"18.0.0\"}}\n",
            ["packages/web/index.html"] = "<!doctype html>\n<html><body>web</body></html>\n",
            ["packages/api/package.json"] = "{\"name\":\"api\"}\n",
            ["packages/api/index.js"] = "module.exports = {};\n",
        });
    }
}
