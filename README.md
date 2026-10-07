# InkGuide

Drive-backed workspace & repository manager — Windows desktop app (WPF host +
WebView2 front-end). Your files live in **your Google Drive**; local disk is a
disposable cache. Everything is classified, tagged, searchable, and editable
from one tidy repo view.

> The UI layer is replaceable by design and currently ships as reference
> scaffolding: the host runs, the bridge contract is generated, and domain
> features land workstream by workstream. See `docs/BUILD_STATE.md` for live
> status and `docs/KNOWN_LIMITATIONS.md` for honest gaps.

## Prerequisites

- Windows 10+, .NET SDK per `global.json`, Node ≥ 20 LTS.
- WebView2 Runtime (the app points you to the Evergreen bootstrapper if missing).

## Build & run

```cmd
build\verify.cmd        &:: full gate: restore, build, tests, contract check, UI, audits
build\live-check.cmd --local-provider %TEMP%\ig-live   &:: offline smoke test
dotnet run --project src\InkGuide.App -- --local-provider %TEMP%\ig-live --data-dir %TEMP%\ig-data
```

First run with a Google account: follow `docs/GOOGLE_SETUP.md` (one-time
Cloud setup incl. the Desktop-app client and the 7-day testing caveat), then
`build\live-check.cmd`.

## Docs

`docs/ARCHITECTURE.md` (start here) · `docs/BRIDGE_PROTOCOL.md` (generated) ·
`docs/GOOGLE_SETUP.md` · `docs/AI_SETUP.md` · `docs/RULES_AUTHORING.md` ·
`docs/PUBLISHING.md` · `docs/PRIVACY.md` (draft) · `docs/LIVE_TESTING.md` ·
`docs/KNOWN_LIMITATIONS.md` · `docs/DECISIONS.md` · `docs/BUILD_STATE.md`

## Rules that matter

- No visual design in this repo's shipped UI (reference views are wireframe-grade).
- No secrets in the repo. No telemetry. Drive is the source of truth.
- Bridge contract is generated: `InkGuide.Tools contract-gen --check` must stay fresh.
