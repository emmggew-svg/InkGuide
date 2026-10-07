# InkGuide — BUILD_STATE

> Mirror of MASTER_PROMPT Section 24 + feature index F-01..F-26. Updated after every unit of work.

**App:** InkGuide (`inkguide.desktop`) | **Target:** net9.0-windows x64 | **UI lang default:** tr-TR
**Toolchain verified:** dotnet SDK 9.0.316, Node v24.18.0, git 2.55.0

## Preflight
- [x] Toolchain verified (dotnet/node/git) — evidence: `dotnet --info`, `node -v`
- [x] Repo skeleton created, `git init`, `docs/MASTER_PROMPT.md` copied
- [x] Solution + foundations build with zero warnings — evidence: `dotnet build InkGuide.sln` 0 warn / 0 err (2026-10-08)
- [ ] `build/verify.cmd` passes (blocked: `vite build` hangs at transforming stage, investigation ongoing)

## Features
- [ ] F-01 Accounts & OAuth
- [ ] F-02 Storage abstraction + Google Drive provider
- [ ] F-03 LocalFolder provider
- [ ] F-04 Browse
- [ ] F-05 File operations
- [ ] F-06 Transfers
- [ ] F-07 In-app text/code editing
- [ ] F-08 Preview
- [ ] F-09 External-edit sessions
- [ ] F-10 Auto insight
- [ ] F-11 Project detection & auto-tagging
- [ ] F-12 Tags
- [ ] F-13 Repositories (single & mono), import
- [ ] F-14 Snapshots
- [ ] F-15 Activity journal & undo
- [ ] F-16 Inbox & Organizer
- [ ] F-17 Duplicate finder
- [ ] F-18 Search (FTS + filters)
- [ ] F-19 Sync engine
- [ ] F-20 Optional AI describer (Anthropic)
- [ ] F-21 Settings, configuration, secrets
- [ ] F-22 Hardened WebView2 host + bridge
- [ ] F-23 UI layer: core + reference views + replacement
- [ ] F-24 i18n tr-TR, en-US
- [ ] F-25 Diagnostics
- [ ] F-26 Packaging, installer, publish docs

## Definition of Done (Sec 24)
- [ ] verify.cmd zero warnings, all tests green, coverage met
- [ ] Architecture tests pass
- [ ] Drive provider contract tests (fake HTTP, scopes/fields asserted)
- [ ] LocalFolder provider contract tests + full app runs on it
- [ ] Rebuild-from-Drive test
- [ ] Analysis fixtures: static, nextjs, vite-react, wordpress, dotnet-wpf, minecraft-plugin, python, mono-repo
- [ ] Outbox crash-survival; conflicts never silent-overwrite
- [ ] Bridge e2e every method; generated contract fresh
- [ ] Reference UI no-design lint; ui-override/dev-url/fixture; prod bundle no fixtures
- [ ] WebView2 hardening tested
- [ ] i18n complete
- [ ] Security checklist + audits
- [ ] publish.cmd runnable dist + installer + notices
- [ ] All Sec-22 docs accurate
- [ ] Launch check performed or honestly reported
- [ ] Local git history clean, nothing pushed

## Log
- 2026-10-07: preflight ok, skeleton started. SDK 9 (not 8/10 LTS) — see DECISIONS D-001.
- 2026-10-07: Domain+Application+Analysis verified — clean rebuild 0 warnings/0 errors; UnitTests 274/274 pass; fixed 4 stale test vectors (PSD hex, WebP-lossy length, 2 glob expectations) + test-only build breaks.
- 2026-10-08: fixed Bridge missing ById DTOs + CA1716/CA1068/CA1822 + central-package NU1008; solution 0 warn/0 err. Added Arch (7 tests) + Integration (12 tests): LocalFolder roundtrip/idempotency/collisions/changefeed/sanitization, Bridge routing/idempotency/cancel/oversize/confirmations, Outbox crash-survival + redaction. Full dotnet: 274+7+12=293 pass. UI: typecheck+lint clean, vitest 67/67 (~98% lines), contract:check OK. Vite build hangs at "transforming..." (bg job running) — verify.cmd blocked on UI build stage.
- 2026-10-08: root-caused 2 load-bearing defects with evidence: (1) LocalFolder index JSON deserialized to empty Items (get-only Dictionary) — debug test showed index.json len=346 with folder but list count=0; fixed with setters. (2) SQLite cannot translate/order DateTimeOffset (Where<= and ORDER BY) — InvalidOperationException/NotSupportedException; fixed via client-side filtering/sorting in OutboxStore + MiscStore. See DECISIONS D-016.
- 2026-10-08: root-caused Vite 7.3.7 transform hang (vanilla 260 ms OK; react-only still hangs; isolated no-config react project hangs; same project builds in 597 ms under Vite 8.3.3) — upgraded vite 7.3.7->8.3.3 + plugin-react 4.7.0->5.2.0. Full UI gate green: typecheck, lint, vitest 67/67, vite build ~666 ms (62 modules, dist/ emitted), contract:check, no fixture leaks. See DECISIONS D-017.
