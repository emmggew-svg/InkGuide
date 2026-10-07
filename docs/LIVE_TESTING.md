# InkGuide — LIVE_TESTING

> Manual checklist for the owner/ tester. Automated part: `build\live-check.cmd`.
> Rule: never touch real files — use generated fixtures and temp directories.

## A. Offline smoke (no Google account needed)

- [ ] `InkGuide.Tools seed-fixtures --out %TEMP%\ig-fixtures` → 30 files, 8 trees.
- [ ] `build\live-check.cmd --local-provider %TEMP%\ig-live` → 7/7 PASS table.
- [ ] Launch: `dotnet run --project src\InkGuide.App -- --local-provider %TEMP%\ig-live --data-dir %TEMP%\ig-data`
      → window opens, no white flash, no crash. (First run shows the missing-UI
      notice until the UI workstream lands; the host stays open for inspection.)
- [ ] Second launch with a file path → first window activates (single instance),
      no second window.
- [ ] `--help` → usage dialog. `--reset` → confirmation dialog, clears
      cache/settings only.

## B. Google integration (after docs/GOOGLE_SETUP.md)

- [ ] Import OAuth client JSON → connect via system browser → success page.
- [ ] `build\live-check.cmd` (Drive path) → step table; dedicated
      `InkGuide-LiveCheck-<timestamp>` folder created and removed.
- [ ] Revoke at myaccount.google.com/permissions → app shows `ReauthRequired`,
      cached data stays readable, sync stops for that account.

## C. Host hardening spot-checks (with any UI)

- [ ] `app.hello` returns contract 1.x + uiStatus; incompatible `ui-override`
      falls back to bundled UI with a logged reason.
- [ ] DevTools/context menu/zoom off in Release (on in Debug).
- [ ] External links open in the system browser; odd schemes blocked.
- [ ] Drag a folder from Explorer onto the app → validated upload starts;
      symlinked trees rejected (see logs).
- [ ] Kill the renderer (or crash it) → auto-reload, throttled, logged.
- [ ] `window.setBackground` changes chrome instantly (no restart).

## D. Packaging

- [ ] `build\publish.cmd 0.1.0` → dist folder + ZIP + SHA256SUMS; unzip on a
      clean machine (with WebView2 runtime) → app starts.
- [ ] Installer compiles (`iscc`); per-user install; uninstall asks about data.
- [ ] `build\sign.cmd` without cert → prints how-to; with cert → signed exe/dll.
