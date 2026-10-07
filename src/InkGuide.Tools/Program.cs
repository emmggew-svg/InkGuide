using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using InkGuide.Tools.ContractGen;
using InkGuide.Tools.LiveCheck;
using InkGuide.Tools.SeedFixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace InkGuide.Tools;

/// <summary>
/// InkGuide.Tools console entry point: contract-gen, live-check, seed-fixtures.
/// User-facing reports go to stdout (this is a CLI); diagnostics go through
/// ILogger into a rolling file under the temp directory.
/// </summary>
internal sealed class Program
{
    private const string Usage =
        "InkGuide.Tools\n" +
        "\n" +
        "Usage:\n" +
        "  InkGuide.Tools contract-gen [--check] [--out-ts <path>] [--out-md <path>] [--bridge-dir <dir>]\n" +
        "  InkGuide.Tools live-check --local-provider <dir>\n" +
        "  InkGuide.Tools live-check            (reports Drive-backend state)\n" +
        "  InkGuide.Tools seed-fixtures --out <dir> [--only <name>]\n";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] rest = args.Length > 1 ? args[1..] : Array.Empty<string>();

        string logDir = Path.Combine(Path.GetTempPath(), "inkguide-tools", "logs");
        Directory.CreateDirectory(logDir);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, "tools-.log"),
                rollingInterval: RollingInterval.Day,
                formatProvider: CultureInfo.InvariantCulture,
                retainedFileCountLimit: 5)
            .CreateLogger());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<BridgeReflection>();
        builder.Services.AddSingleton<ContractGenerator>();
        builder.Services.AddSingleton<LiveCheckCommand>();
        builder.Services.AddSingleton<FixtureSeeder>();

        using IHost host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);

        try
        {
            return command switch
            {
                "contract-gen" => await host.Services.GetRequiredService<ContractGenerator>().RunAsync(rest).ConfigureAwait(false),
                "live-check" => await host.Services.GetRequiredService<LiveCheckCommand>().RunAsync(rest).ConfigureAwait(false),
                "seed-fixtures" => await host.Services.GetRequiredService<FixtureSeeder>().RunAsync(rest).ConfigureAwait(false),
                _ => UnknownCommand(command),
            };
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
            Log.CloseAndFlush();
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.WriteLine("Unknown command: " + command);
        Console.WriteLine(Usage);
        return 2;
    }
}
