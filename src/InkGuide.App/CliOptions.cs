using System;
using System.Collections.Generic;

namespace InkGuide.App;

/// <summary>
/// Parsed command-line options (MASTER_PROMPT Section 14.1).
/// </summary>
internal sealed record CliOptions
{
    public string? UiDevUrl { get; init; }
    public string? DataDir { get; init; }
    public string? LocalProviderDir { get; init; }
    public bool Reset { get; init; }
    public bool Portable { get; init; }
    public bool ShowHelp { get; init; }
    public string[] OpenPaths { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }

    private const string HelpText =
        "InkGuide - Drive-backed workspace manager.\n" +
        "\n" +
        "Usage: InkGuide [options] [paths...]\n" +
        "\n" +
        "Options:\n" +
        "  --ui-dev-url <url>       Load the UI from a dev server (debug builds only).\n" +
        "  --data-dir <dir>         Override the application data directory.\n" +
        "  --local-provider <dir>   Enable the LocalFolder provider rooted at <dir>.\n" +
        "  --reset                  Clear cache, settings and window state (asks first).\n" +
        "  --portable               Store data next to the executable.\n" +
        "  --help                   Show this help.\n" +
        "\n" +
        "Any extra positional arguments are treated as file/folder paths to open.";

    public static string Usage => HelpText;

    private static bool IsHttpUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? uiDevUrl = null;
        string? dataDir = null;
        string? localProviderDir = null;
        bool reset = false;
        bool portable = false;
        bool showHelp = false;
        string? error = null;
        var openPaths = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--ui-dev-url")
            {
                i++;
                if (i >= args.Length)
                {
                    error = "Missing value for --ui-dev-url.";
                    break;
                }

                uiDevUrl = args[i];
            }
            else if (arg == "--data-dir")
            {
                i++;
                if (i >= args.Length)
                {
                    error = "Missing value for --data-dir.";
                    break;
                }

                dataDir = args[i];
            }
            else if (arg == "--local-provider")
            {
                i++;
                if (i >= args.Length)
                {
                    error = "Missing value for --local-provider.";
                    break;
                }

                localProviderDir = args[i];
            }
            else if (arg == "--reset")
            {
                reset = true;
            }
            else if (arg == "--portable")
            {
                portable = true;
            }
            else if (arg == "--help" || arg == "-h" || arg == "-?")
            {
                showHelp = true;
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                error = "Unknown option: " + arg;
                break;
            }
            else
            {
                openPaths.Add(arg);
            }
        }

        if (error is null && uiDevUrl is not null && !IsHttpUrl(uiDevUrl))
        {
            error = "Invalid --ui-dev-url value: must be an http(s) URL.";
        }

        return new CliOptions
        {
            UiDevUrl = uiDevUrl,
            DataDir = dataDir,
            LocalProviderDir = localProviderDir,
            Reset = reset,
            Portable = portable,
            ShowHelp = showHelp,
            OpenPaths = openPaths.ToArray(),
            Error = error,
        };
    }
}
