# InkGuide — DECISIONS (ADR-lite)

## D-001 — Target .NET 9, not 8/10 LTS
- Context: MASTER_PROMPT Sec 4 says target newest LTS installed (net10 if avail, else net8). Machine has only SDK 9.0.200/9.0.316 (STS), with WindowsDesktop runtimes 9.0.x.
- Decision: target `net9.0-windows`, SDK pinned 9.0.316 in global.json.
- Consequence: upgrade to net10 LTS when SDK available; no code change expected beyond TFMs. Documented deviation.

## D-002 — APP_NAME = InkGuide
- Context: prompt variable defaults to Cairn / cairn.desktop. Owner set app name InkGuide.
- Decision: Solution/namespace prefix `InkGuide`, Drive root `InkGuide Workspace`, AppData `InkGuide`, APP_ID `inkguide.desktop`.
- Consequence: `cairn.*` appProperties keys renamed to `inkguide.*` (k,s,dh,ch,op). Manifest folder `.inkguide/` (configurable constant, default `.inkguide`).

## D-003 — Single-run execution adapted to incremental checkpoints
- Context: prompt demands one continuous run with no hand-back. Interactive session has output/time limits.
- Decision: work in small verified increments, persisting state in docs/BUILD_STATE.md so context reset = resume point.
- Consequence: final report only when Sec-24 satisfied.

### Unverified (no web access used yet / to verify against official docs)
- U-001: Google.Apis.Drive.v3 latest version + exact `about.get` field permissions under `drive.file` scope.
- U-002: appProperties limits (124B key+value, 30/file) and revision pin limits.
- U-003: Anthropic default model id (prompt default `claude-haiku-5-5` may not exist) + `anthropic-version` header value.
- U-004: WebView2 virtual-host + CSP combination behavior for PDF viewer and range requests.
- U-005: Microsoft.Web.WebView2 exact latest version.
- U-006 (host/tools workstream): Evergreen bootstrapper link `https://go.microsoft.com/fwlink/p/?LinkId=2124703`; drag-drop native paths via `AdditionalObjects`/`CoreWebView2File.Path`; `CoreWebView2PermissionState.Deny` naming.
- U-007 (host/tools workstream): Google Testing-mode ~7-day refresh-token expiry wording + remedies.

## D-006 — Bump Microsoft.Web.WebView2 pin to 1.0.3296.44
- Context: central pin 1.0.3277.44 no longer resolves from the configured feed; restore emits NU1603, fatal under TreatWarningsAsErrors. Resolver offered 1.0.3296.44.
- Decision: bumped the single pin in Directory.Packages.props (outside host ownership, one line, flagged to foundations/infra).
- Consequence: restore+build green. Re-bump freely when the feed moves.

## D-007 — App + Tools target net9.0-windows10.0.17763.0
- Context: bare `net9.0-windows` infers TargetPlatformVersion 7.0, which trips NETSDK1135 against the repo-wide SupportedOSPlatformVersion 10.0.17763.0. Sibling agents converged on the same qualifier independently.
- Decision: OS-version qualifier in InkGuide.App/Tools csproj (still the net9.0-windows family, Win10 1809 floor per TARGET_OS).
- Consequence: build green. Foundations may alternatively lower/remove the global SOPV; either way the qualifier stays valid.

## D-008 — Set UseWPF directly in InkGuide.App.csproj
- Context: Directory.Build.props sets UseWPF only `Condition="'$(IsWpfProject)'=='true'"`, but props import before the project body defines it, so the condition never fires.
- Decision: set both `<IsWpfProject>true</IsWpfProject>` (as instructed) and `<UseWPF>true</UseWPF>` directly.
- Consequence: WPF compiles. Foundations can fix the ordering; the direct flag remains harmless.

## D-009 — Bridge backend discovered by reflection, not referenced
- Context: layering forbids Bridge -> App references, and the host must build before Bridge contracts land. A compile-time call into the backend is impossible today and would invert the dependency tomorrow.
- Decision: `BridgeModuleLoader` discovers `InkGuide.Bridge.BridgeModule` (`Configure`/`CanHandle`/`HandleAsync` + `BridgeMethodException`) reflectively; absence is a normal logged state. Full seam documented in docs/ARCHITECTURE.md.
- Consequence: host serves host-owned methods now; domain namespaces delegate automatically once Bridge lands. No `dynamic`, no service locator (composition-root wiring only).

## D-010 — Tools uses Console for CLI presentation, ILogger for diagnostics
- Context: repo standard is ILogger-only. A console tool's entire product is stdout text (tables, usage); file logging via Serilog covers diagnostics.
- Decision: user-facing reports via Console; all internal components take ILogger<T> (LoggerMessage delegates, file sink under %TEMP%/inkguide-tools/logs).
- Consequence: script-friendly output + redacted diagnostic logs.

## D-011 — contract-gen ships a baseline catalog until Bridge DTOs land
- Context: InkGuide.Bridge currently exposes no DTOs; the generator must still emit a complete, checkable contract today and enrich itself tomorrow.
- Decision: baseline method/error catalog (MASTER_PROMPT Sec 15 + Appendix C) compiled into the tool + reflection over public Bridge DTOs/enums when present; freshness hash + `--check` gate.
- Consequence: `ui/src/core/contract.ts` + `docs/BRIDGE_PROTOCOL.md` are complete v1 now, auto-enriching later.

## D-012 — live-check Drive path reports state instead of faking success
- Context: Drive REST belongs to the storage workstream; duplicating it in Tools would diverge. R-08/R-16 forbid fake success.
- Decision: without `--local-provider`, live-check prints the config state + GOOGLE_SETUP pointer and exits 2. LocalFolder backend runs all 7 steps for real on a dedicated timestamped folder with guaranteed cleanup.
- Consequence: honest signal today; storage owner plugs the Drive backend into the same step table later.

## D-013 — Publish as an unpacked folder, never single-file
- Context: WebView2Loader.dll (native) does not survive single-file bundling reliably; publish.cmd verifies its presence and fails otherwise.
- Decision: self-contained win-x64 folder + ReadyToRun + no trim; ZIP + SHA256SUMS for distribution.
- Consequence: slightly larger artifact, guaranteed loader story.

## D-014 — AllowUnsafeBlocks for Win32 single-instance activation
- Context: LibraryImport source generation for the EnumWindows delegate callback requires unsafe blocks (SYSLIB1062 as error).
- Decision: `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` scoped to InkGuide.App with a justification comment; only the activation P/Invokes use it.
- Consequence: zero-warning build; no raw pointers elsewhere.

## D-015 — All logging via LoggerMessage delegates (CA1848)
- Context: AnalysisLevel latest-recommended flags every LoggerExtensions call under TreatWarningsAsErrors.
- Decision: one `AppLogMessages` (App) + `ToolsLog` (Tools) source-generated delegate set; fixed event ids.
- Consequence: zero-warning logging with structured, greppable messages.

## D-004 — CA1716 suppression for Bridge Namespace (R-22 justified)
- Context: CA1716 flags IBridgeHandler.Namespace / HandlerBase.Namespace as keyword match. Term is bridge-protocol vocabulary (Sec 14-15, envelope method `ns.method`), mirrored in docs/BRIDGE_PROTOCOL.md and UI contract.ts.
- Decision: SuppressMessage CA1716 with justification on interface + base only; no other suppressions.
- Consequence: zero-warning build without diverging from protocol naming.

## D-016 — LocalFolder index setters + SQLite DateTimeOffset client evaluation
- Context: (1) Debug test proved index.json persisted (len=346, folder present) yet ListChildren(RootId) returned 0 — System.Text.Json does not populate get-only `Dictionary.Items`; (2) EF Core SQLite throws InvalidOperationException/NotSupportedException for `DateTimeOffset <=` in Where and ORDER BY DateTimeOffset.
- Decision: Add public setters to `Index.Items` / `IndexEntry.AppProperties`; filter/sort outbox/journal/snapshot/conflict DateTimeOffset queries in memory after a narrow server filter (outbox sets capped at 500).
- Consequence: 12/12 integration tests green; no schema change.

## D-017 — Vite 7.3.7 -> 8.3.3, plugin-react 4.7.0 -> 5.2.0 (build hang)
- Context: `vite build` hung forever at "transforming..." (multiple 2-5 min timeouts, one node process at 385 CPU-s). Bisected with evidence: vanilla Vite project builds in 260 ms; react-only entry (plain .js, no aliases, no react plugin via diagnostic config) still hangs; isolated TEMP project (no config, react+react-dom) still hangs; same isolated project builds in 597 ms under Vite 8.3.3. Culprit: Vite 7.3.7's Rollup 4.x pipeline on React 19.1 dist under Node 24 (Vite 8 uses Rolldown and is unaffected). The @vitejs/plugin-react was exonerated (hang reproduces without it).
- Decision: `vite@8.3.3` + `@vitejs/plugin-react@5.2.0` (supports vite ^8, same Babel pipeline as 4.x — conservative vs 6.x oxc line). Full app `npm run build` now completes in ~666 ms (62 modules, dist/ with index.html + css + js).
- Consequence: UI gate fully green (typecheck, eslint 0 warnings, vitest 67/67, contract:check, prod bundle has no fixture strings). `package-lock.json` updated.

## D-018 - Bridge double-encoding (launch-check dialogs, 2026-10-08)
- Context: launch showed ui-missing dialog (dotnet run has no bundled UI) + TimeProvider DI crash, then after fixing both, the log showed 2x 'Rejected malformed bridge message' 31 s apart (hello + its timeout-cancel). Root cause: UI stringifies the envelope before postMessage (spec Sec 14.2) so WebMessageAsJson arrives as a JSON string literal, which TryParseRequest rejected (expected Object); symmetrically main.tsx did JSON.parse(ev.data) but PostWebMessageAsJson already delivers an object, so responses were dropped too.
- Decision: host TryParseRequest unwraps one JSON-string level and accepts direct objects (both shapes); main.tsx listener accepts object-or-string with an isEnvelope guard; FallbackRouter logs HandshakeServed(1.0); App registers TimeProvider.System (was missing); App.csproj copies ui/dist to OutDir/ui when present so 'dotnet run' serves the reference UI.
- Consequence: relaunch shows 'Bridge handshake served (contract 1.0)', zero malformed warnings, responding window. Launch evidence in BUILD_STATE Log.
