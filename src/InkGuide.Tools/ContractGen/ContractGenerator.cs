using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace InkGuide.Tools.ContractGen;

/// <summary>
/// Generates ui/src/core/contract.ts and docs/BRIDGE_PROTOCOL.md from the C#
/// bridge contracts (baseline catalog + reflected DTOs). With --check, exits
/// non-zero when the committed files differ from freshly generated output.
/// </summary>
internal sealed class ContractGenerator
{
    /// <summary>
    /// Marker line starting the hand-maintained tail of contract.ts.
    /// Everything from this line to EOF is preserved across regenerations so
    /// the UI workstream can keep compat aliases until real DTOs land.
    /// </summary>
    internal const string PreservedMarker = "// --- UI compatibility layer (preserved across contract-gen runs) ---";

    private readonly ILogger<ContractGenerator> _logger;
    private readonly BridgeReflection _reflection;

    public ContractGenerator(ILogger<ContractGenerator> logger, BridgeReflection reflection)
    {
        _logger = logger;
        _reflection = reflection;
    }

    public async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        bool check = args.Any(a => a == "--check");
        string? outTs = ValueOf(args, "--out-ts");
        string? outMd = ValueOf(args, "--out-md");
        string? bridgeDir = ValueOf(args, "--bridge-dir");

        string repoRoot = FindRepoRoot();
        outTs ??= Path.Combine(repoRoot, "ui", "src", "core", "contract.ts");
        outMd ??= Path.Combine(repoRoot, "docs", "BRIDGE_PROTOCOL.md");
        string bridgeDll = bridgeDir is not null
            ? Path.Combine(bridgeDir, "InkGuide.Bridge.dll")
            : Path.Combine(AppContext.BaseDirectory, "InkGuide.Bridge.dll");

        ReflectedContracts reflected = _reflection.Reflect(bridgeDll);
        string ts = WithPreservedTail(EmitTypeScript(reflected), outTs);
        string md = EmitProtocolDoc(reflected);

        if (check)
        {
            bool fresh = MatchesFile(outTs, ts) && MatchesFile(outMd, md);
            if (!fresh)
            {
                if (!MatchesFile(outTs, ts))
                {
                    ToolsLog.StaleFile(_logger, outTs);
                }

                if (!MatchesFile(outMd, md))
                {
                    ToolsLog.StaleFile(_logger, outMd);
                }

                Console.WriteLine("contract-gen --check: STALE (run `contract-gen` to refresh)");
                return 1;
            }

            ToolsLog.CheckPassed(_logger);
            Console.WriteLine("contract-gen --check: fresh");
            return 0;
        }

        await WriteFileAsync(outTs, ts).ConfigureAwait(false);
        await WriteFileAsync(outMd, md).ConfigureAwait(false);
        Console.WriteLine("contract-gen: wrote " + outTs);
        Console.WriteLine("contract-gen: wrote " + outMd);
        return 0;
    }

    internal static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "InkGuide.sln")))
            {
                return dir;
            }

            string? parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir)
            {
                break;
            }

            dir = parent;
        }

        return AppContext.BaseDirectory;
    }

    private async Task WriteFileAsync(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string normalized = Normalize(content);
        await File.WriteAllTextAsync(path, normalized).ConfigureAwait(false);
        ToolsLog.WroteFile(_logger, path, new FileInfo(path).Length);
    }

    private static bool MatchesFile(string path, string fresh)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return Normalize(File.ReadAllText(path)) == Normalize(fresh);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static string WithPreservedTail(string fresh, string existingPath)
    {
        if (!File.Exists(existingPath))
        {
            return fresh;
        }

        string existing = Normalize(File.ReadAllText(existingPath));
        int marker = existing.IndexOf(PreservedMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return fresh;
        }

        return Normalize(fresh).TrimEnd() + "\r\n\r\n" + existing[marker..].TrimEnd() + "\r\n";
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

    private static string HashOf(string a, string b)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(a + "\n---\n" + b));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string EmitTypeScript(ReflectedContracts reflected)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// AUTO-GENERATED by InkGuide.Tools contract-gen. Do not edit by hand.");
        sb.AppendLine("// Source: src/InkGuide.Bridge (C# contracts) + baseline catalog (MASTER_PROMPT Sec 15).");
        sb.AppendLine("// Freshness hash: " + HashOf("ts", DtoFingerprint(reflected)));
        sb.AppendLine();
        sb.AppendLine("export const CONTRACT_MAJOR = " + BridgeCatalog.ContractMajor + ";");
        sb.AppendLine("export const CONTRACT_MINOR = " + BridgeCatalog.ContractMinor + ";");
        sb.AppendLine("export const MAX_MESSAGE_BYTES = " + BridgeCatalog.MaxMessageBytes + ";");
        sb.AppendLine();
        sb.AppendLine("export type MessageType = 'req' | 'res' | 'evt' | 'cancel';");
        sb.AppendLine();
        sb.AppendLine("export interface BridgeError {");
        sb.AppendLine("  code: ErrorCode;");
        sb.AppendLine("  message: string;");
        sb.AppendLine("  retryable: boolean;");
        sb.AppendLine("  details?: unknown;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("export interface BridgeEnvelope {");
        sb.AppendLine("  v: 1;");
        sb.AppendLine("  id: string;");
        sb.AppendLine("  type: MessageType;");
        sb.AppendLine("  method?: string;");
        sb.AppendLine("  params?: unknown;");
        sb.AppendLine("  result?: unknown;");
        sb.AppendLine("  error?: BridgeError;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.Append("export type ErrorCode =");
        foreach (BridgeErrorCode code in BridgeCatalog.ErrorCodes)
        {
            sb.Append("\n  | '" + code.Code + "'");
        }

        sb.AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("// Requests carry params: Record<string, unknown>; results are decoded per method.");
        sb.AppendLine("// Reflected DTOs below replace `unknown` once the Bridge owner lands C# contracts.");
        sb.AppendLine("export interface BridgeMethodTable {");
        foreach (BridgeNamespace ns in BridgeCatalog.Namespaces)
        {
            foreach (string method in ns.Methods)
            {
                sb.AppendLine("  '" + BridgeCatalog.MethodId(ns.Name, method) + "': { params: unknown; result: unknown };");
            }
        }

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("export const bridgeNamespaces: Array<{ name: string; description: string; methods: string[] }> = [");
        foreach (BridgeNamespace ns in BridgeCatalog.Namespaces)
        {
            sb.Append("  { name: '" + ns.Name + "', description: '" + Escape(ns.Description) + "', methods: [");
            sb.Append(string.Join(", ", ns.Methods.Select(m => "'" + m + "'")));
            sb.AppendLine("] },");
        }

        sb.AppendLine("];");
        sb.AppendLine();
        sb.AppendLine("export const bridgeEvents: Array<{ name: string; description: string }> = [");
        foreach (BridgeEvent evt in BridgeCatalog.Events)
        {
            sb.AppendLine("  { name: '" + evt.Name + "', description: '" + Escape(evt.Description) + "' },");
        }

        sb.AppendLine("];");

        if (reflected.Enums.Count > 0 || reflected.Dtos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("// Reflected from InkGuide.Bridge.dll");
            foreach (ReflectedEnum e in reflected.Enums.OrderBy(x => x.Name))
            {
                sb.Append("export type " + e.Name + " =");
                sb.Append(string.Join(" ", e.Values.Select(v => "\n  | '" + v + "'")));
                sb.AppendLine(";");
            }

            foreach (ReflectedDto dto in reflected.Dtos.OrderBy(x => x.Name))
            {
                sb.AppendLine();
                sb.AppendLine("export interface " + dto.Name + " {");
                foreach (ReflectedField field in dto.Fields)
                {
                    sb.AppendLine("  " + field.Name + ": " + field.TsType + ";");
                }

                sb.AppendLine("}");
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("// No reflected DTOs yet: the Bridge contract assembly exposes no public DTO types.");
            sb.AppendLine("// The baseline catalog above is the complete contract (v1).");
        }

        return sb.ToString();
    }

    private static string DtoFingerprint(ReflectedContracts reflected)
    {
        var sb = new StringBuilder();
        foreach (ReflectedEnum e in reflected.Enums.OrderBy(x => x.Name))
        {
            sb.Append(e.Name).Append('=').Append(string.Join(",", e.Values)).Append(';');
        }

        foreach (ReflectedDto dto in reflected.Dtos.OrderBy(x => x.Name))
        {
            sb.Append(dto.Name).Append(':');
            foreach (ReflectedField field in dto.Fields)
            {
                sb.Append(field.Name).Append('=').Append(field.TsType).Append(',');
            }

            sb.Append(';');
        }

        foreach (BridgeNamespace ns in BridgeCatalog.Namespaces)
        {
            sb.Append(ns.Name).Append(':').Append(string.Join(",", ns.Methods)).Append(';');
        }

        return sb.ToString();
    }

    private static string Escape(string text) => text.Replace("'", "\\'");

    private static string EmitProtocolDoc(ReflectedContracts reflected)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# InkGuide — BRIDGE_PROTOCOL (v" + BridgeCatalog.ContractMajor + "." + BridgeCatalog.ContractMinor + ")");
        sb.AppendLine();
        sb.AppendLine("> AUTO-GENERATED by InkGuide.Tools contract-gen (baseline catalog + reflected C# DTOs).");
        sb.AppendLine("> Do not edit by hand; edit the C# contracts or the catalog and re-run the tool.");
        sb.AppendLine("> Freshness hash: `" + HashOf("md", DtoFingerprint(reflected)) + "`");
        sb.AppendLine();
        sb.AppendLine("Host (WPF + WebView2) <-> UI message protocol. Transport: UI posts a JSON");
        sb.AppendLine("string via `window.chrome.webview.postMessage`; host replies with");
        sb.AppendLine("`PostWebMessageAsJson`. Max message size: 1 MB. File bytes never cross");
        sb.AppendLine("the bridge; previews use `https://cache.appassets.example` URLs.");
        sb.AppendLine();
        sb.AppendLine("## Envelope (JSON, camelCase, enums as strings)");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{ \"v\": 1, \"id\": \"uuid\", \"type\": \"req|res|evt|cancel\",");
        sb.AppendLine("  \"method\": \"nodes.list\", \"params\": {},");
        sb.AppendLine("  \"result\": {}, \"error\": { \"code\": \"NOT_FOUND\", \"message\": \"dev-only English text\",");
        sb.AppendLine("                           \"retryable\": false, \"details\": {} } }");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("- Handshake: `app.hello` returns `{ contract: { major, minor }, hostVersion,");
        sb.AppendLine("  capabilities[], features{}, settingsSnapshot, uiStatus }`. Compatible when the");
        sb.AppendLine("  major matches and host minor >= UI minor.");
        sb.AppendLine("- Cancellation: `{ type: \"cancel\", id }` aborts the in-flight request.");
        sb.AppendLine("- Pagination: opaque `cursor` + `limit` (default 200).");
        sb.AppendLine("- Mutations accept `idempotencyKey`; destructive ones need a `confirmationToken`.");
        sb.AppendLine("- Events are coalesced (>= 100 ms per topic). Errors cross as codes only;");
        sb.AppendLine("  `message` is developer English; the UI localizes by code.");
        sb.AppendLine();
        sb.AppendLine("## Methods by namespace");
        sb.AppendLine();
        sb.AppendLine("| Namespace | Method | Description |");
        sb.AppendLine("|---|---|---|");
        foreach (BridgeNamespace ns in BridgeCatalog.Namespaces)
        {
            foreach (string method in ns.Methods)
            {
                sb.AppendLine("| `" + ns.Name + "` | `" + method + "` | " + ns.Description + " |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Events (host -> UI)");
        sb.AppendLine();
        sb.AppendLine("| Event | Meaning |");
        sb.AppendLine("|---|---|");
        foreach (BridgeEvent evt in BridgeCatalog.Events)
        {
            sb.AppendLine("| `" + evt.Name + "` | " + evt.Description + " |");
        }

        sb.AppendLine();
        sb.AppendLine("## Error codes (Appendix C)");
        sb.AppendLine();
        sb.AppendLine("| Code | Meaning |");
        sb.AppendLine("|---|---|");
        foreach (BridgeErrorCode code in BridgeCatalog.ErrorCodes)
        {
            sb.AppendLine("| `" + code.Code + "` | " + code.Description + " |");
        }

        sb.AppendLine();
        if (reflected.Dtos.Count > 0 || reflected.Enums.Count > 0)
        {
            sb.AppendLine("## Reflected DTOs (from InkGuide.Bridge.dll)");
            sb.AppendLine();
            foreach (ReflectedEnum e in reflected.Enums.OrderBy(x => x.Name))
            {
                sb.AppendLine("- `" + e.Name + "`: " + string.Join(" | ", e.Values.Select(v => "`" + v + "`")));
            }

            foreach (ReflectedDto dto in reflected.Dtos.OrderBy(x => x.Name))
            {
                sb.AppendLine();
                sb.AppendLine("### `" + dto.Name + "`");
                sb.AppendLine();
                sb.AppendLine("| Field | Type |");
                sb.AppendLine("|---|---|");
                foreach (ReflectedField field in dto.Fields)
                {
                    sb.AppendLine("| `" + field.Name + "` | `" + field.TsType + "` |");
                }
            }
        }
        else
        {
            sb.AppendLine("## DTOs");
            sb.AppendLine();
            sb.AppendLine("No public DTO types are exposed by the Bridge assembly yet. Params and");
            sb.AppendLine("results are `unknown` in `contract.ts` until the Bridge owner lands the C#");
            sb.AppendLine("contracts; re-running contract-gen then will embed them here automatically.");
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
