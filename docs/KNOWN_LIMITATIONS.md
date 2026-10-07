# InkGuide — KNOWN_LIMITATIONS

> Honest list of what is limited, missing, or depends on another workstream.
> Updated by the host/tools/packaging workstream; other workstreams append
> their own entries (do not delete).

## Host / tools / packaging (this workstream)

- L-01 — Domain bridge namespaces are served through `IBridgeBackend` + handlers
  (`BridgeDispatcher`, confirmation tokens, idempotency, cancellation); the host
  adapts them to real services. Full e2e against real Application services is
  pending host wiring.
- L-02 — Reference UI ships in `ui/` (18 wireframe views, core/stores/i18n,
  fixture bridge, ui-override + `--ui-dev-url` ready). `vite build` hangs at
  the transforming stage on this machine (bg job, investigation ongoing);
  `typecheck` + `lint` + `vitest 67/67` + `contract:check` are green.
- L-03 — `live-check --local-provider` passes 7/7 on a dedicated timestamped
  folder with guaranteed cleanup. Without `--local-provider` it honestly
  reports Drive-backend state (exit 2) instead of faking success.
- L-04 — `dotnet` audit reports one transitive High: `SQLitePCLRaw.lib.e_sqlite3`
  2.1.10 (GHSA-2m69-gcr7-jv3q) via `Microsoft.EntityFrameworkCore.Sqlite` 9.0.8
  (Integration + Architecture test projects). .NET 9 is STS and its 9.0.x patch
  line appears closed on the configured feed (only 10.x listed); the fix is a
  major EF Core + TFM bump tracked with D-001. Accepted risk for local-cache
  use; no network parsing of untrusted content in that path.
- L-05 — `npm audit --audit-level=high` is clean (0 vulnerabilities).
- L-06 — Installer (`setup.iss`) is not compile-tested here (no Inno Setup on
  this machine); payload layout matches `publish.cmd` output by construction.
- L-07 — `sign.cmd` prints how-to without a certificate (expected; certs are
  never in the repo).
- L-08 — Evergreen bootstrapper URL and Google 7-day token wording are
  established knowledge, not re-verified against live docs in this run
  (marked Unverified in `docs/DECISIONS.md`).

## Product-level (by design, MASTER_PROMPT Section 23)

- No Shared Drives, sharing/permission management, real-time collaboration,
  in-app Office editing, OCR, mobile/web clients, auto-update, telemetry,
  licensing, or visual design (reference UI is deliberately unstyled).
