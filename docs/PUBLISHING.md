# InkGuide — PUBLISHING

> Release checklist for producing a distributable InkGuide build.

## Prerequisites

- Windows 10+, .NET SDK (see `global.json`), Node ≥ 20.
- Inno Setup 6 (`iscc`) for the installer; Windows SDK `signtool` for signing.

## Release steps

1. Set the version in `Directory.Build.props` (`<Version>`) — it stamps the
   assembly, and pass the same value to the scripts.
2. `build\verify.cmd` — must print `VERIFY OK` (zero warnings, tests green,
   contract fresh, audits clean).
3. `build\publish.cmd 0.2.0` → `dist\InkGuide-0.2.0-win-x64\` +
   `dist\InkGuide-0.2.0-win-x64.zip` + `dist\SHA256SUMS.txt`.
   Publish check: the script fails unless `WebView2Loader.dll` is present in
   the output (`runtimes\win-x64\native\`). The product ships as an unpacked
   folder — single-file bundling is deliberately off because the native loader
   does not survive it reliably.
4. `build\sign.cmd` with `SIGN_CERT_THUMBPRINT` (store cert) or
   `SIGN_PFX_PATH` (+`SIGN_PFX_PASSWORD`) set. Without either it prints how.
   Never store certificates in the repo.
5. Compile the installer:
   `iscc installer\setup.iss /DAppVersion=0.2.0 /DSourceDir=dist\InkGuide-0.2.0-win-x64`
   Per-user install to `%LOCALAPPDATA%\Programs\InkGuide`, no admin. Drop an
   Evergreen `MicrosoftEdgeWebview2Setup.exe` next to `setup.iss` to offer the
   runtime during setup. Uninstall asks whether to remove user data (Drive
   files are never affected).
6. Update `CHANGELOG.md` and regenerate `THIRD_PARTY_NOTICES.md` if
   dependencies changed.

## Embedding the production OAuth client

At build time, provide the client via the `INKGUIDE_OAUTH_CLIENT_JSON`
environment variable or place the downloaded JSON where the backend expects
(see `docs/GOOGLE_SETUP.md` step 5); it is imported into DPAPI-protected
storage at first run. Never commit client ids/secrets (the repo `.gitignore`
blocks `client_secret*.json` / `credentials*.json`).

## Google consent-screen / verification notes

- `drive.file` (default `AppFolderOnly` mode) is a **non-sensitive** scope:
  publishable without verification; personal-use installs accept the
  consent screen directly.
- `drive` (FullDrive mode) is a **restricted** scope: distribution needs
  Google verification and, for most cases, a third-party security assessment.
  Verify the current rules in the official verification docs before promising
  a FullDrive release.

## SmartScreen reputation notes

Unsigned or newly-signed binaries trigger Windows SmartScreen warnings. A
reputation builds with signed installs over time; an EV certificate shortens
the path. Always sign releases (step 4); keep the same publisher identity
across versions.

## Update strategy (auto-update is out of scope)

Ship new ZIPs/setups from the same download location. Recommended options for
the owner to choose later: in-app "check for updates" link opening the
download page in the system browser, or a Squirrel/WinGet-based flow. No
telemetry or phone-home is permitted to implement this — checking must be
explicit and user-initiated.

## Required store/branding inputs (owner to-do)

`assets\app.ico` (drop-in; default icon otherwise — no branding is created by
the build), homepage + privacy-policy URLs for the consent screen, license
choice, code-signing certificate.
