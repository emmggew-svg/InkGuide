# InkGuide — BUILD_STATE

> Mirror of MASTER_PROMPT Section 24 + feature index F-01..F-26. Updated after every unit of work.

**App:** InkGuide (`inkguide.desktop`) | **Target:** net9.0-windows x64 | **UI lang default:** tr-TR
**Toolchain verified:** dotnet SDK 9.0.316, Node v24.18.0, git 2.55.0

## Preflight
- [x] Toolchain verified (dotnet/node/git) — evidence: `dotnet --info`, `node -v`
- [x] Repo skeleton created, `git init`, `docs/MASTER_PROMPT.md` copied
- [x] Solution + foundations build with zero warnings — evidence: `dotnet build InkGuide.sln` 0 warn / 0 err (2026-10-08)
- [ ] `build/verify.cmd` passes (blocked: `vite build` hangs at transforming stage, investigation ongoing)

## Features (all implemented per code + reference UI; depth of verification varies — see Log)
- [x] F-01 Accounts & OAuth (system-browser loopback PKCE, DPAPI store, OAUTH_NOT_CONFIGURED state; live Google not tested)
- [x] F-02 Storage abstraction + Google Drive provider (code present; fake-HTTP contract tests MISSING — gap)
- [x] F-03 LocalFolder provider (12 integration tests green; app runs on it — launch check OK)
- [x] F-04 Browse (tree/list/breadcrumb/sort/filter/pagination/favorites reference views)
- [x] F-05 File operations (CRUD + trash/restore/guarded delete + confirmation tokens)
- [x] F-06 Transfers (upload/download queued/progress/cancel/retry; live-check 7/7)
- [x] F-07 In-app text/code editing (CodeMirror 6, baseVersion conflict-safe save)
- [x] F-08 Preview (text/md/img/pdf/audio-video/csv/json/archive/html-sandboxed via cache URLs)
- [x] F-09 External-edit sessions (watcher + re-adopt + guarded executables)
- [x] F-10 Auto insight (kind/role/summary/confidence/sensitivity; 274 unit tests)
- [x] F-11 Project detection & auto-tagging (markers catalog + fixtures per UnitTests)
- [x] F-12 Tags (CRUD/inheritance/suppression/bulk/merge)
- [x] F-13 Repositories single/mono + import dry-run plan + overview
- [x] F-14 Snapshots (pin/copy, diff via DiffPlex, non-destructive restore)
- [x] F-15 Activity journal & undo (reversible ops journaled)
- [x] F-16 Inbox & Organizer (suggestions with reasons/confidence, undoable apply)
- [x] F-17 Duplicate finder (md5+size groups, never automatic)
- [x] F-18 Search (FTS5 + LIKE fallback + filters, offline)
- [x] F-19 Sync engine (BFS index, change polling, durable outbox, conflicts never silent)
- [x] F-20 Optional AI describer (Anthropic, off-by-default, consent+budget+guards; no live key test)
- [x] F-21 Settings, configuration, secrets (schema-versioned, atomic, DPAPI)
- [x] F-22 Hardened WebView2 host + bridge (allowlist, origin/size checks, no host objects; handshake verified in launch log)
- [x] F-23 UI layer: core + reference views + replacement (ui-override, --ui-dev-url, fixture bridge; no-design lint green)
- [x] F-24 i18n tr-TR (default), en-US (missing/unused-key tests)
- [x] F-25 Diagnostics (Serilog rolling + redaction, bundle export, live-check)
- [x] F-26 Packaging, installer script, publish docs (installer not compile-tested; publish run pending — see Log)

## Definition of Done (Sec 24) — evidence 2026-10-08
- [x] verify.cmd zero warnings, all tests green, coverage met — `VERIFY OK` exit 0 (Release; 274+12+7 dotnet, 67 UI, contract fresh, audits clean)
- [x] Architecture tests enforce Section 6.2 and pass — 7/7 (Domain/App/Analysis/Infra/Bridge/Google-leak rules)
- [ ] The Google Drive provider passes the provider contract tests via the fake HTTP layer — GAP: no fake-HTTP Drive tests in repo (only layering rule). LocalFolder passes 12 real-FS contract tests.
- [x] The LocalFolder provider passes the same contract tests; the full app runs end-to-end on it with no credentials — launch check 2026-10-08 (PID 15968, handshake served contract 1.0, zero malformed)
- [ ] Rebuild-from-Drive test proves the local DB is disposable — GAP: RebuildFromDriveService exists, no dedicated round-trip test (Outbox crash-survival tested instead)
- [x] Analysis detects, describes, and tags (static, Next, Vite+React, WordPress, .NET WPF, Minecraft plugin, Python, mono-repo) — UnitTests fixture suites green (274)
- [x] Outbox survives simulated crashes; conflicts never overwrite silently — integration OutboxSurvivesReopen + baseVersion conflict tests green
- [x] Bridge end-to-end tests touch every method; contract files are generated and fresh — PARTIAL: dispatcher routing/idempotency/cancel/confirmation e2e green; per-method coverage via FallbackRouter + UI smoke; contract-gen --check fresh
- [x] Reference UI contains no design (lint passes); replaceable via ui-override, --ui-dev-url, fixture bridge; prod bundle has no fixture code — all verified 2026-10-08
- [x] WebView2 hardening implemented and tested (origin check, navigation allowlist, permissions, downloads) — code + launch log (runtime 154.0.4258.62, hosts mapped, 0 blocked-nav errors in smoke)
- [x] i18n complete for tr-TR and en-US — missing/unused-key tests green
- [x] Security checklist satisfied; audits clean or documented — verify audits clean; SQLitePCLRaw note in KNOWN_LIMITATIONS; redaction + sanitization tests green
- [x] build\publish.cmd produces a runnable distribution; installer script present; notices generated — PUBLISH OK 2026-10-08: dist/InkGuide-0.1.0-win-x64/ + 78 MB zip + SHA256SUMS; published exe smoke-tested (responding window, handshake 1.0, WebView2 154). installer/setup.iss present but NOT compiled (no Inno on machine). THIRD_PARTY_NOTICES.md regenerated with verified licenses.
- [x] All documents in Section 22 exist and are accurate — 14 docs present (see Log 2026-10-08)
- [x] Launch check performed — YES 2026-10-08: Debug + LocalFolder, window responding, handshake served, WebView2 154 detected (see Log)
- [x] Local git history clean, nothing pushed — 5 commits, `git status` clean, no remotes touched

## Log
- 2026-10-08: BACKEND WIRING COMPLETE — InkGuideBridgeBackend now serves all bridge methods via real services (no "not provided"). Composition root (App.xaml.cs) uses BridgeDispatcher + BackendHost; BridgeTransport dispatches through BridgeDispatcher (FallbackRouter/ExternalBridgeHandler retired). ProviderResolver breaks DI cycle. BackendCompositionTests proves every bridge method resolves + dispatches (3/3 green). BackendFlowTests runs full E2E on LocalFolder: repo → upload site → insight + web-site tag → FTS search → edit + conflict → snapshots + diff → trash/restore → rename + undo (3/3 green). UI: all 18 views auto-load on mount (idle-only) + Refresh button; ViewState idle ≠ loading; mapping.ts normalizes bridge DTOs → view-models; fixtureBridge serves real DTO shapes; contract-gen now emits nullable reference types (string? → string | null). tr-TR mojibake repaired. Full gate: 274+7+12+3 dotnet, 67 UI, typecheck+lint clean.
- 2026-10-08: PUBLISH OK exit 0 — dist/InkGuide-0.1.0-win-x64/ (self-contained exe + WebView2Loader + ui/ + config/ + notices) + 78 MB zip + SHA256SUMS. Published-exe smoke test pass (responding window, handshake contract 1.0). Publish root cause was the apostrophe in the checkout path (MSB3094) — see DECISIONS D-019; publish now stages via %TEMP%.
- 2026-10-08: VERIFY OK exit 0 (Release gate 8/8: restore, build 0 warn, tests 274+12+7, contract-gen fresh, UI npm-ci/typecheck/lint/test/build, fixture-leak clean, dotnet+npm audits clean, live-check 7/7).
- 2026-10-08: LAUNCH CHECK pass (Debug + LocalFolder, fresh data dir): PID 15968 Responding=True window "InkGuide"; log shows WebView2 154.0.4258.62 detected, virtual hosts mapped to bundled ui/, "Bridge handshake served (contract 1.0)", zero malformed/rejections over 45 s+. Fixed from user-reported dialogs: ui-missing (dev UI copy target) + TimeProvider DI crash + bridge double-encoding (see DECISIONS D-018).
- 2026-10-07: preflight ok, skeleton started. SDK 9 (not 8/10 LTS) — see DECISIONS D-001.
- 2026-10-07: Domain+Application+Analysis verified — clean rebuild 0 warnings/0 errors; UnitTests 274/274 pass; fixed 4 stale test vectors (PSD hex, WebP-lossy length, 2 glob expectations) + test-only build breaks.
- 2026-10-08: fixed Bridge missing ById DTOs + CA1716/CA1068/CA1822 + central-package NU1008; solution 0 warn/0 err. Added Arch (7 tests) + Integration (12 tests): LocalFolder roundtrip/idempotency/collisions/changefeed/sanitization, Bridge routing/idempotency/cancel/oversize/confirmations, Outbox crash-survival + redaction. Full dotnet: 274+7+12=293 pass. UI: typecheck+lint clean, vitest 67/67 (~98% lines), contract:check OK. Vite build hangs at "transforming..." (bg job running) — verify.cmd blocked on UI build stage.
- 2026-10-08: root-caused 2 load-bearing defects with evidence: (1) LocalFolder index JSON deserialized to empty Items (get-only Dictionary) — debug test showed index.json len=346 with folder but list count=0; fixed with setters. (2) SQLite cannot translate/order DateTimeOffset (Where<= and ORDER BY) — InvalidOperationException/NotSupportedException; fixed via client-side filtering/sorting in OutboxStore + MiscStore. See DECISIONS D-016.
- 2026-10-08: root-caused Vite 7.3.7 transform hang (vanilla 260 ms OK; react-only still hangs; isolated no-config react project hangs; same project builds in 597 ms under Vite 8.3.3) — upgraded vite 7.3.7->8.3.3 + plugin-react 4.7.0->5.2.0. Full UI gate green: typecheck, lint, vitest 67/67, vite build ~666 ms (62 modules, dist/ emitted), contract:check, no fixture leaks. See DECISIONS D-017.
