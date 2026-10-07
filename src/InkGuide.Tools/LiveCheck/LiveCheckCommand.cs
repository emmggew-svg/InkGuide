using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace InkGuide.Tools.LiveCheck;

/// <summary>
/// Real end-to-end smoke test (MASTER_PROMPT Section 19): auth -> create test
/// folder -> upload -> list -> changes -> rename -> trash -> cleanup, against a
/// dedicated test folder only. The LocalFolder backend runs fully offline; the
/// Google Drive backend reports its configuration state honestly.
/// </summary>
internal sealed class LiveCheckCommand
{
    private readonly ILogger<LiveCheckCommand> _logger;
    private readonly TimeProvider _timeProvider;

    public LiveCheckCommand(ILogger<LiveCheckCommand> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? localRoot = ValueOf(args, "--local-provider");
        if (localRoot is null)
        {
            return ReportDriveNotConfigured();
        }

        ToolsLog.CommandStarted(_logger, "live-check (local-provider)");
        var store = new LocalFolderLiveCheckStore(localRoot, _timeProvider);
        var steps = new List<LiveStep>();
        string folderName = "InkGuide-LiveCheck-" + _timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string folderId = string.Empty;

        folderId = await RunStepAsync(steps, "create-test-folder", "Create dedicated test folder.", async () =>
        {
            string id = await store.CreateFolderAsync(folderName).ConfigureAwait(false);
            return (id, "folder=" + folderName);
        }).ConfigureAwait(false) ?? string.Empty;

        if (!string.IsNullOrEmpty(folderId))
        {
            string fileId = await RunStepAsync(steps, "upload", "Upload probe file.", async () =>
            {
                string id = await store.UploadFileAsync(folderId, "probe.txt", ProbeBytes()).ConfigureAwait(false);
                return (id, "bytes=" + ProbeBytes().Length);
            }).ConfigureAwait(false) ?? string.Empty;

            await RunStepAsync(steps, "list", "List folder children.", async () =>
            {
                string[] names = await store.ListChildrenAsync(folderId).ConfigureAwait(false);
                if (!names.Contains("probe.txt", StringComparer.Ordinal))
                {
                    throw new InvalidOperationException("probe.txt missing from listing.");
                }

                return (folderId, "children=" + names.Length);
            }).ConfigureAwait(false);

            await RunStepAsync(steps, "changes", "Read change feed.", async () =>
            {
                int count = await store.GetChangeCountAsync().ConfigureAwait(false);
                if (count == 0)
                {
                    throw new InvalidOperationException("Change feed is empty.");
                }

                return (folderId, "changes=" + count);
            }).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(fileId))
            {
                await RunStepAsync(steps, "rename", "Rename probe file.", async () =>
                {
                    await store.RenameAsync(folderId, fileId, "renamed.txt").ConfigureAwait(false);
                    return (fileId, "renamed.txt");
                }).ConfigureAwait(false);

                await RunStepAsync(steps, "trash", "Move file to trash.", async () =>
                {
                    await store.TrashAsync(folderId, fileId).ConfigureAwait(false);
                    return (fileId, "trashed");
                }).ConfigureAwait(false);
            }
        }

        await RunStepAsync(steps, "cleanup", "Delete the dedicated test folder.", async () =>
        {
            await store.CleanupAsync(folderName).ConfigureAwait(false);
            if (await store.ExistsAsync(folderName).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Test folder still exists after cleanup.");
            }

            return (folderName, "removed");
        }).ConfigureAwait(false);

        PrintTable(steps);
        int failed = steps.Count(s => s.Outcome == "FAIL");
        return failed == 0 ? 0 : 1;
    }

    private static int ReportDriveNotConfigured()
    {
        Console.WriteLine("live-check (google-drive): NOT RUN");
        Console.WriteLine("Reason: no provider selected. The Drive backend needs a configured");
        Console.WriteLine("OAuth client (see docs/GOOGLE_SETUP.md). Until then, run the full");
        Console.WriteLine("smoke test against the LocalFolder provider:");
        Console.WriteLine("  InkGuide.Tools live-check --local-provider <empty-dir>");
        return 2;
    }

    private async Task<string?> RunStepAsync(
        List<LiveStep> steps,
        string name,
        string description,
        Func<Task<(string Id, string Detail)>> action)
    {
        DateTimeOffset start = _timeProvider.GetUtcNow();
        try
        {
            (string id, string detail) = await action().ConfigureAwait(false);
            long ms = (long)(_timeProvider.GetUtcNow() - start).TotalMilliseconds;
            steps.Add(new LiveStep(name, description, "PASS", detail + " (" + ms + " ms)"));
            ToolsLog.StepPassed(_logger, name, ms);
            return id;
        }
        catch (Exception ex)
        {
            long ms = (long)(_timeProvider.GetUtcNow() - start).TotalMilliseconds;
            steps.Add(new LiveStep(name, description, "FAIL", ex.Message + " (" + ms + " ms)"));
            ToolsLog.StepFailed(_logger, ex, name, ex.Message);
            return null;
        }
    }

    private static void PrintTable(List<LiveStep> steps)
    {
        Console.WriteLine();
        Console.WriteLine("Step                 Result  Detail");
        Console.WriteLine("-------------------  ------  --------------------------------");
        foreach (LiveStep step in steps)
        {
            Console.WriteLine(step.Name.PadRight(20) + "  " + step.Outcome.PadRight(6) + "  " + step.Detail);
        }

        Console.WriteLine();
        int passed = steps.Count(s => s.Outcome == "PASS");
        Console.WriteLine("live-check: " + passed + "/" + steps.Count + " steps passed.");
    }

    private static byte[] ProbeBytes() =>
        Encoding.UTF8.GetBytes("InkGuide live-check probe. Safe to delete.\n");

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

    private sealed record LiveStep(string Name, string Description, string Outcome, string Detail);
}
