# InkGuide — PRIVACY NOTICE (DRAFT)

> **Draft — not legal advice.** This is an engineering description of data
> flows, written so a lawyer can turn it into a policy. It is accurate to the
> code as shipped; it is not a legal document.

## What InkGuide is

A local Windows desktop app. There is no InkGuide server, no account system,
no analytics, no telemetry, no phone-home. Uninstalling removes the program;
your files stay in your Google Drive.

## Where your data lives

| Data | Location |
|---|---|
| Your files (source of truth) | Your Google Drive (`InkGuide Workspace` folder + adopted repos) |
| Fast local mirror | SQLite database + caches under `%LOCALAPPDATA%\InkGuide\` (rebuildable; deleting loses only cache + unsynced outbox entries) |
| OAuth tokens, AI key, imported OAuth client | DPAPI-encrypted (CurrentUser) in `secrets\`; never in plain text, never in logs |
| Settings, window state | `settings.json`, `window-state.json` (local; workspace-roaming settings also mirror to Drive `.inkguide/workspace.json`) |
| Logs | Rolling files under `logs\` (10 MB × 10), with token/key redaction |

## Network traffic (exhaustive)

1. Google APIs (Drive + OAuth) — only after you connect an account. Scopes:
   `drive.file` by default; `drive` only in FullDrive mode.
2. Anthropic API — only when you explicitly enable the optional AI describer
   **and** record data-sharing consent (off by default, per-repo opt-out).

Nothing else leaves the machine. No ads, trackers, crash reporters, or
update checks.

## AI describer specifics

When enabled, the app sends per-file metadata plus a bounded excerpt
(default ≤ 6 KB) to Anthropic. Never sent: files classified `Sensitive`
(secrets/keys/credentials), binaries, images. Daily request budget enforced.
See `docs/AI_SETUP.md`.

## Your controls

- Disconnect an account (tokens revoked + deleted), wipe local data, per-repo
  AI opt-out, `Sensitive` auto-flagging (excluded from search index and AI),
  log-bundle export (`diagnostics.exportBundle`, redacted), uninstall option
  to delete all local data.

## Open points for legal review

Data-controller identity, retention wording for logs/outbox, minor use, and
jurisdiction-specific disclosures (KVKK/GDPR) are not addressed here.
