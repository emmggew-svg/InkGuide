# InkGuide — CHANGELOG

All notable changes. Format: Keep a Changelog-ish, versions are SemVer
(`Directory.Build.props` is the source of truth).

## [0.1.0] — 2026-10-07 (host + tools + packaging workstream)

### Added

- WPF host (`InkGuide.App`, composition root only): single `MainWindow` with
  WebView2 only; hardened virtual hosts (`ui`/`cache`/`usercontent`);
  navigation allowlist; popup/permission/download policy; settings-driven
  devtools/menu/keys/zoom; throttled renderer auto-reload; single instance
  with arg forwarding; multi-monitor-safe window state; PerMonitorV2 +
  longPathAware manifest; `--ui-dev-url/--data-dir/--local-provider/--reset/
  --portable` CLI; native dialogs; redacted file logging.
- Bridge transport: origin/size-checked postMessage JSON, background dispatch,
  per-request cancellation, ≥100 ms event coalescing, reparse-point-safe
  drag-and-drop native paths; host-owned methods served, backend discovery
  via reflection (`InkGuide.Bridge.BridgeModule` contract).
- `InkGuide.Tools`: `contract-gen` (→ `ui/src/core/contract.ts` +
  `docs/BRIDGE_PROTOCOL.md`, `--check` gate), `live-check` (7-step smoke on a
  dedicated folder; Drive path reports config state), `seed-fixtures`
  (8 trees, 30 files).
- `build/`: `verify.cmd/ps1`, `publish.cmd/ps1` (self-contained win-x64 folder
  + WebView2Loader check + ZIP + SHA256SUMS), `sign.cmd/ps1`,
  `live-check.cmd/ps1`.
- `installer/setup.iss`: per-user Inno Setup script with optional WebView2
  bootstrapper and data-aware uninstall.
- Docs: ARCHITECTURE, GOOGLE_SETUP, AI_SETUP, RULES_AUTHORING, PUBLISHING,
  PRIVACY (draft), LIVE_TESTING, KNOWN_LIMITATIONS, UI_CONTRACT/UI_REPLACEMENT_GUIDE
  stubs, generated BRIDGE_PROTOCOL; README, THIRD_PARTY_NOTICES.

### Fixed (foundations, with justification in docs/DECISIONS.md)

- `Microsoft.Web.WebView2` central pin 1.0.3277.44 → 1.0.3296.44 (feed no
  longer serves the pinned build; NU1603-as-error).
