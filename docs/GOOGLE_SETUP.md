# InkGuide — GOOGLE_SETUP

> One-time Google Cloud setup so InkGuide can use your Google Drive.
> Follow the steps click-by-click; then run `build\live-check.cmd`.
> No secrets are ever committed to the repo (see step 8).

## What you are creating

A Google Cloud **OAuth client of type "Desktop app"** in your own project.
InkGuide uses the installed-app flow: system browser + loopback redirect +
PKCE. Sign-in never happens inside InkGuide's window (Google forbids it).

Default scope is `drive.file` (per-file access granted by the app — the
non-sensitive `AppFolderOnly` mode). `FullDrive` mode requests the
restricted `drive` scope instead (needed only to adopt pre-existing Drive
folders; requires Google verification to publish).

## Step-by-step

1. Go to [Google Cloud Console](https://console.cloud.google.com/) and sign in.
2. Create a project: top bar project picker → **New Project** → name it
   e.g. `InkGuide` → **Create**. Wait for the notification, then select it.
3. Enable the Drive API: left menu → **APIs & Services → Library** → search
   `Google Drive API` → open it → **Enable**.
4. Configure the consent screen: **APIs & Services → OAuth consent screen** →
   User type **External** → **Create**.
   - App name: `InkGuide`; User support email: your address.
   - App logo: leave empty for personal use.
   - **Scopes**: click **Add or Remove Scopes** and add exactly:
     - `.../auth/drive.file` (default mode), and nothing else.
     - Only if you need FullDrive mode: `.../auth/drive` instead.
   - Test users: while the app is in **Testing** status, add your own Gmail
     address under **Test users** (required until publishing).
   - Save.
5. Create the client: **APIs & Services → Credentials → Create Credentials →
   OAuth client ID** → Application type **Desktop app** → name `InkGuide
   Desktop` → **Create** → **Download JSON**. Keep this file private.
6. Publishing status (important — the 7-day caveat):
   - While **Publishing status = Testing**, Google expires refresh tokens
     after **about 7 days**. You will have to reconnect weekly.
   - Remedies: (a) for strictly personal use, open the consent screen and
     click **Publish App** (status *In production*); Google shows an
     "unverified app" warning you accept once, and tokens stop expiring; or
     (b) stay in Testing and re-authenticate weekly.
   - If you later distribute InkGuide with the `drive` (FullDrive) scope,
     Google requires app verification plus a security assessment — verify the
     current rules in the [OAuth verification docs](https://support.google.com/cloud/answer/7454865).
7. Import the client into InkGuide: open InkGuide → Settings/Accounts →
   **Import OAuth client** → pick the downloaded JSON. It is stored
   DPAPI-protected (CurrentUser) under the app data `secrets\` folder, never
   in plain text. (If no client is imported, every connect attempt returns
   `OAUTH_NOT_CONFIGURED` pointing here.)
8. Connect: Accounts → **Connect** → your system browser opens Google's
   consent page → approve → the browser shows a plain success page and you
   return to InkGuide. Revoking later is done from Accounts → Disconnect
   (tokens are revoked and deleted) or from your
   [Google account permissions](https://myaccount.google.com/permissions).
9. Verify: run `build\live-check.cmd` (add `--local-provider <empty-dir>` for
   the offline variant). Expect a pass/fail table; the tool only ever touches
   its dedicated `InkGuide-LiveCheck-<timestamp>` folder and deletes it after.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `OAUTH_NOT_CONFIGURED` | No client imported (step 7). |
| `REAUTH_REQUIRED` / weekly logout | Testing-mode 7-day expiry (step 6). |
| `Error 403: access_denied` during consent | Your account is not in Test users (step 4). |
| Drive API not enabled | Repeat step 3 in the same project. |
| Port / redirect error | Allow the loopback redirect; corporate proxies may block `http://127.0.0.1:*` — retry off-VPN. |

## Security notes

- Tokens live DPAPI-encrypted; they are never logged (the host redacts
  `ya29.`, `1//`, `Bearer`, `sk-ant-`, `x-api-key`, client secrets).
- InkGuide's only network destinations are Google APIs (Drive + OAuth), plus
  the Anthropic API only when you explicitly enable the optional AI describer.
