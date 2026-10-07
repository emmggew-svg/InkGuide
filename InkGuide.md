# MASTER PROMPT — {APP_NAME}
### Drive-backed workspace & repository manager · WPF + WebView2 · single-run autonomous build

> **Document version:** 1.0
> **Audience:** an autonomous coding agent with file-system and shell access (CLI or IDE agent).
> **Owner:** the person who handed you this file. The owner will not answer questions during the run.
> **Your output:** a complete, working, tested, publish-ready desktop product in this repository — **not** a design, **not** a prototype, **not** a plan.

---

## 0. Read this first

1. Read this **entire** document before you write a single line of code or run a single command.
2. This is **one continuous engagement**. There are no phases that end with a hand-back to the owner. You do not stop, summarize, or ask for confirmation until the Definition of Done (Section 24) is satisfied or an Allowed Halt (Section 3.2) occurs.
3. If this document contradicts itself, precedence is: **Section 2 (Rules) > Section 24 (Definition of Done) > everything else.**
4. If official documentation (Google Drive API, WebView2, Anthropic API, .NET, Vite, …) contradicts a claim in this document, **the official documentation wins**. Record the discrepancy in `docs/DECISIONS.md`.
5. If you have web access, verify every external-API detail you rely on against official docs. If you do not, use established knowledge and list each unverified assumption under an **"Unverified"** heading in `docs/DECISIONS.md`.
6. Copy this file to `docs/MASTER_PROMPT.md` in the repository on your first action. If your context is ever reset or compacted, re-read it together with `docs/BUILD_STATE.md` and continue exactly where you left off.

### 0.1 Owner-editable variables

The owner may edit this table before running. If unchanged, use the defaults.

| Variable | Default | Meaning |
|---|---|---|
| `APP_NAME` | `Cairn` | **Working title.** Used for solution/namespace prefix, Drive root folder name (`{APP_NAME} Workspace`), AppData folder, default window title. Must live in exactly one place per layer (`Directory.Build.props` for .NET, `ui/package.json` + `ui/src/core/appIdentity.ts` for UI, `AppIdentity.cs` for runtime strings). Never hard-code the name elsewhere. |
| `APP_ID` | `cairn.desktop` | Reverse-style identifier for mutex names, installer ID, user-agent suffix. |
| `DEFAULT_UI_LANGUAGE` | `tr-TR` | Default UI language. Secondary: `en-US`. |
| `DRIVE_SCOPE_MODE` | `AppFolderOnly` | `AppFolderOnly` → OAuth scope `drive.file` (non-sensitive, publishable). `FullDrive` → scope `drive` (restricted; enables importing pre-existing Drive folders; personal use / needs Google verification to publish). Switchable by setting. |
| `AI_DESCRIBER_DEFAULT` | `off` | Optional AI file descriptions (Section 11). Always opt-in. |
| `AI_MODEL_DEFAULT` | `claude-haiku-5-5` | Default model id for the AI describer. Configurable; verify against current Anthropic docs. |
| `TARGET_OS` | Windows 10 or newer, x64 | ARM64 is a nice-to-have, not required. |
| `CONTENT_CACHE_LIMIT_DEFAULT` | 2 GB | Local content cache cap (LRU). |
| `MAX_INLINE_EDIT_SIZE` | 5 MB | Largest file editable in the in-app editor. |
| `MAX_ANALYSIS_READ_BYTES` | 256 KB | Max bytes read per file for analysis. |

---

## 1. Mission

Build **{APP_NAME}**: a Windows desktop application (WPF host + WebView2 front-end) that turns the user's **Google Drive** into a tidy, GitHub-style workspace.

**The owner's words, distilled:**

- Files do **not** live on the owner's computer. They live in the **Google Drive of the Google account connected inside the app**, accessed through the Drive API. Local disk is only a disposable cache.
- From inside the app the owner must be able to **change and edit everything**: create, rename, move, copy, delete, edit text/code, replace binaries, manage metadata.
- Every file that enters the system is **automatically identified**: what kind of file it is and **what it is for**, written down in plain words. If a folder is a **web-site project**, it is **tagged** as such (and with its technologies).
- There is a **file tree** and a **repository system** — a single repo or a **mono-repo** — "like GitHub but more comprehensive and better, as a desktop app".
- The owner hates disorder. The product exists to **end disorder**: everything in one place, classified, tagged, searchable, and tidy.
- The **UI will be designed separately by someone else**. You build the **complete structure and a working product** whose UI layer is **fully replaceable later**. You **must not design the look and feel**.

**Success looks like:** the owner runs one command, gets a working app; connects a Google account (after a one-time Google Cloud setup documented step-by-step); drags in a folder; watches it get classified, described and tagged; browses it as a repo; edits a file; sees it sync to Drive; and later drops in a professionally designed UI without touching the back-end.

---

## 2. Non-negotiable rules

### 2.1 You MUST

| ID | Rule |
|---|---|
| R-01 | **Deliver end-to-end in one run.** Every feature F-01…F-26 (Section 5) is implemented, wired, tested, and reachable from the UI. |
| R-02 | **Drive is the source of truth.** The local SQLite database and caches are *rebuildable*. Deleting them must lose nothing except the cache and un-synced outbox entries. A rebuild-from-Drive test is mandatory. |
| R-03 | **Strict layering** (Section 6). Dependencies point inward only; enforced by architecture tests. |
| R-04 | **The UI is a replaceable layer.** Back-end ↔ UI communicate only through the versioned bridge contract (Section 14–15). The back-end knows nothing about markup, CSS, or framework. |
| R-05 | **Anything an owner or designer may change is data, not code**: detector rules (JSON), file-role catalog (JSON), design tokens (CSS variables), strings (i18n JSON), settings (JSON), prompts (resource files). |
| R-06 | **Tests ship with code.** No feature is "done" without automated tests (Section 20). |
| R-07 | **Verify by running.** Build, test, lint, typecheck, and (if the environment allows) launch. Fix every failure. |
| R-08 | **Be honest.** Report exactly what was verified and what was not. Never claim a live Google Drive test you did not perform. |
| R-09 | **Secure by default** (Section 18). Least-privilege OAuth scopes, DPAPI-protected secrets, hardened WebView2, sanitized untrusted content. |
| R-10 | **Reversible by default.** Delete = move to trash. Permanent deletion requires an explicit confirmation token. Reversible operations are journaled and undoable. |
| R-11 | **Log every non-obvious decision** in `docs/DECISIONS.md` (ADR-lite: context → decision → consequence). |
| R-12 | **Keep UI state out of the browser.** Durable UI preferences go through the bridge (`settings.getUiState/setUiState`), so a replacement UI inherits them. `localStorage` may only be used for disposable caches. |
| R-13 | **Use the newest stable, actively maintained dependencies** installed via package managers (`dotnet add package`, `npm install`) — never typed from memory. Commit lockfiles. |

### 2.2 You MUST NOT

| ID | Rule |
|---|---|
| R-14 | **Do not design the UI.** No palette, typography, spacing system, icons, illustrations, animations, shadows, gradients, branding, logo, empty-state art, theme-switch UI, or "polish". Details in Section 16. This is the owner's most emphasized rule. |
| R-15 | **No stubs.** No `NotImplementedException`, `TODO`, `FIXME`, "placeholder implementation", empty handlers, or commented-out code in shipped paths. The *only* allowed gaps are values the owner must supply (Google OAuth client, AI key, app icon, code-signing cert) and these must be handled by explicit, tested "not configured" states. |
| R-16 | **No fabricated data or fake success.** Fixtures/mocks exist only in tests and in the clearly-flagged dev "fixture bridge" (Section 16.4), which is excluded from production bundles. |
| R-17 | **No secrets in the repository** or in logs. |
| R-18 | **No OAuth inside an embedded browser.** Google forbids it. Use the system browser + loopback redirect + PKCE. |
| R-19 | **No telemetry, no analytics, no phone-home.** The only outbound network traffic allowed: Google APIs (Drive + OAuth), and the Anthropic API *only when the owner enabled the AI describer*. |
| R-20 | **No actions outside the project directory** except the app's own AppData folder at runtime. No global installs, no system-setting changes, no `git push`, no publishing anywhere. |
| R-21 | **No questions, no stopping** (Section 3). Decide, record, continue. |
| R-22 | **Never weaken tests, disable analyzers, or add `#pragma warning disable` / `// eslint-disable` / `@ts-ignore`** to get green. Fix the cause. A suppression needs a justification comment and an entry in `docs/DECISIONS.md`. |
| R-23 | **No blocking on async** (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`), no `Thread.Sleep`, no `async void` (except true event handlers), no static mutable state, no service locator, no `dynamic`. |
| R-24 | **No license-hostile dependencies.** Allowed: MIT, Apache-2.0, BSD, ISC, MS-PL. Avoid GPL/AGPL/SSPL and commercial-only or relicensed packages (e.g. FluentAssertions ≥ 8, split-license image libraries, Syncfusion/Telerik/DevExpress). Generate `THIRD_PARTY_NOTICES.md`. |
| R-25 | **No UI kits, CSS frameworks, icon packs, icon fonts, web fonts, animation libraries, or CDN assets.** |
| R-26 | **Do not touch or upload the owner's real files** while testing. Use generated fixtures and temp directories. |

---

## 3. Execution protocol (single continuous run)

### 3.1 Operating mode

- Work **autonomously**. Make decisions using this document; when it is silent, choose the **most conservative, reversible, standard** option and log it.
- Write code **to files**, not into chat. Keep chat output minimal until the final report.
- Work in **small verified increments**: write → build → test → fix → continue.
- Maintain `docs/BUILD_STATE.md`: a checklist of F-01…F-26 and the Definition-of-Done items with `[ ]`/`[x]`, plus one line of verification evidence each. Update it after every significant unit of work. It is your memory if context is reset.
- If you hit a hard technical obstacle, try at least **three distinct approaches** before choosing a documented fallback. Never silently drop a feature.

### 3.2 Allowed halts (the only three)

1. **H1 — Toolchain missing and not installable non-interactively** (no suitable .NET SDK, no Node.js). Write `docs/HALT_REPORT.md` with the exact install command(s), then stop.
2. **H2 — The working directory is not writable.**
3. **H3 — Completing a task would require deleting or modifying files outside the project directory.**

Everything else — ambiguity, missing credentials, failing tests, long builds — is **not** a reason to stop.

### 3.3 Internal build order (not deliverable phases)

Follow this sequence *within the single run*; do not pause between steps.

1. **Preflight:** verify toolchain (Section 4), create repo skeleton, `git init` (local only), copy this prompt to `docs/`.
2. **Foundations:** solution, `Directory.Build.props`, central package management, analyzers, `.editorconfig`, `global.json`, `.gitignore`, `.gitattributes`.
3. **Domain + Application contracts** (entities, value objects, ports, result/error types).
4. **Persistence:** SQLite via EF Core, migrations, FTS, repositories, rebuild-from-Drive logic.
5. **Storage abstraction + LocalFolder provider + provider contract-test suite.**
6. **Analysis engine:** identification, inspection, role catalog, project detectors, tagging, templates — with fixture trees.
7. **Sync engine:** pull (index + changes), outbox, conflicts, manifest read/write, offline.
8. **Google Drive provider + OAuth + resilience**, tested against a fake HTTP layer.
9. **Domain services:** repositories/mono-repos, import planner, snapshots, activity/undo, organizer, duplicates, search.
10. **Optional AI describer** (Anthropic) with privacy guards.
11. **Bridge:** contracts, dispatcher, handlers, TypeScript contract generator, end-to-end bridge tests.
12. **WPF host + WebView2:** hardening, virtual hosts, drag-drop, external-edit, single-instance, crash handling, settings.
13. **UI:** `core` layer (bridge client, stores, i18n, fixture bridge) → unstyled reference views → structural CSS + placeholder tokens.
14. **Packaging:** publish script, installer script, docs, notices.
15. **Full verification pass + final report** (Section 25).

### 3.3.1 Quality bar for every step

Compile with **zero warnings**, tests green, lints clean, public APIs documented, no dead code.

---

## 4. Environment & toolchain

- **Assume** the dev machine is **Windows 10**, shell is **cmd** (PowerShell available), with **Node.js (v24 expected) + npm**. **Verify**, don't trust: run `dotnet --list-sdks`, `node -v`, `npm -v`, `git --version`.
- Write scripts for **cmd/PowerShell** (`.cmd` preferred). No bash-isms. Use `%VAR%` syntax in `.cmd`.
- **.NET:** target the **newest LTS SDK installed** (`net10.0-windows` if available, else `net8.0-windows`). Pin in `global.json` with `rollForward: latestFeature`. Enable nullable, implicit usings off (explicit usings), `LangVersion=latest`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`.
- **WebView2 Runtime:** verify presence on the machine. The app must detect a missing runtime at startup and show a **plain native dialog** (no design) with the Evergreen bootstrapper link; never crash.
- **Windows paths:** keep repo paths short; enable `longPathAware` in the app manifest; sanitize file names (Section 18).
- **Node:** UI toolchain is Vite + TypeScript. Support Node ≥ 20 LTS; develop against the installed version.
- **Central Package Management** (`Directory.Packages.props`). Packages to evaluate and use if verified current/maintained:
  - .NET: `Google.Apis.Drive.v3`, `Google.Apis.Auth`, `Microsoft.Web.WebView2`, `Microsoft.Extensions.Hosting` (+DI/Options/Logging/Http), `Microsoft.EntityFrameworkCore.Sqlite`, `Serilog` (+File/Extensions.Logging), `Polly` (resilience), `DiffPlex`, `xunit`, `NSubstitute`, `Shouldly` (or plain xUnit asserts), `NetArchTest.Rules`, `coverlet.collector`.
  - UI: `react`, `react-dom`, `typescript`, `vite`, `zustand`, `@codemirror/*` (CodeMirror 6), `marked` + `dompurify`, `vitest`, `jsdom`, `@testing-library/react`, `eslint` (+ `typescript-eslint`, `eslint-plugin-jsx-a11y`, `eslint-plugin-react-hooks`).
- Avoid anything that is not on this list unless you can justify it in `docs/DECISIONS.md`.

---

## 5. Product definition

### 5.1 Glossary (use these terms exactly, in code and docs)

| Term | Meaning |
|---|---|
| **Account** | A connected Google account (model supports many; UI requirement: connect, disconnect, switch). |
| **Workspace** | The root Drive folder `{APP_NAME} Workspace` created by the app in the account's Drive. Contains Repositories and the `Inbox`. |
| **Repository** | A top-level folder in the Workspace with a manifest. Kind: **Single** or **Mono**. |
| **Project** | A logical unit inside a repo. In a Single repo the repo itself is the one project. In a Mono repo, each detected/declared sub-folder is a project. |
| **Node** | A file or folder (cached view of a Drive item). |
| **Insight** | The auto-generated understanding of a node: kind, role, summary, confidence, sensitivity, provenance. |
| **Tag** | A label attached to nodes/projects/repos; either `auto` (system-applied) or `user`. |
| **Snapshot** | A commit-like, named, point-in-time record of a repo/project's files and revisions. |
| **Inbox** | Special Workspace folder where unsorted files land; the Organizer proposes where they belong. |
| **Outbox** | Durable queue of local intents awaiting push to Drive. |
| **Provider** | An implementation of `IStorageProvider` (Google Drive, LocalFolder). |
| **Bridge** | The versioned message protocol between host and UI. |
| **Reference views** | The deliberately unstyled functional UI shipped so the product works before the real design arrives. |

### 5.2 Feature index (every item is mandatory)

| ID | Feature |
|---|---|
| F-01 | Accounts & OAuth: connect/disconnect/switch, re-auth detection, OAuth-client import |
| F-02 | Storage abstraction + **Google Drive provider** |
| F-03 | **LocalFolder provider** (dev/test; hidden in Release unless enabled by setting) |
| F-04 | Browse: tree, lists, breadcrumbs, sort, filter, pagination, favorites/pins |
| F-05 | File operations: create folder/file, rename, move, copy, trash, restore, guarded permanent delete, bulk ops |
| F-06 | Transfers: upload files/folders, download, resumable, queued, progress, cancel/retry |
| F-07 | In-app text/code editing with versioned, conflict-safe save |
| F-08 | Preview: text, markdown, image, PDF, audio/video, CSV/TSV, JSON, archive listing, metadata fallback |
| F-09 | External-edit sessions (open in default app, auto-sync back) |
| F-10 | **Auto insight**: kind, role, plain-language summary, confidence, sensitivity |
| F-11 | **Project detection & auto-tagging** (incl. web-site projects and their technologies) |
| F-12 | Tags: CRUD, assign, inheritance, suppression of unwanted auto-tags |
| F-13 | **Repositories** (single & mono), projects, local-folder import with dry-run plan, overview stats |
| F-14 | Snapshots: create (pin/copy), list, diff, restore |
| F-15 | Activity journal & undo |
| F-16 | Inbox & Organizer suggestions |
| F-17 | Duplicate finder |
| F-18 | Search: full-text + filters (tag, kind, project, date, size) |
| F-19 | Sync engine: initial index, change polling, offline outbox, conflict resolution |
| F-20 | Optional AI describer (Anthropic), privacy-guarded |
| F-21 | Settings, configuration, secrets |
| F-22 | Hardened WebView2 host + bridge |
| F-23 | UI layer: `core` + unstyled reference views + replacement mechanisms |
| F-24 | i18n: `tr-TR`, `en-US` |
| F-25 | Diagnostics: logging, log bundle export, live-check tool |
| F-26 | Packaging, installer script, publish docs, privacy draft |

---

## 6. Architecture

### 6.1 Repository layout (this is the mono-repo you create)

```text
{APP_NAME}/
├─ {APP_NAME}.sln
├─ global.json  Directory.Build.props  Directory.Packages.props
├─ .editorconfig  .gitignore  .gitattributes
├─ README.md  CHANGELOG.md  THIRD_PARTY_NOTICES.md
├─ config/                      # shipped data rules (copied to output; user-overridable at runtime)
│  ├─ project-markers.json      # project/type detectors
│  ├─ file-roles.json           # file-role catalog + summary templates
│  ├─ kind-map.json             # extension/MIME → kind
│  └─ ignore-patterns.json      # default ignore list for imports
├─ src/
│  ├─ {APP_NAME}.Domain/                  # entities, value objects, Result/Error — zero dependencies
│  ├─ {APP_NAME}.Application/             # ports (interfaces) + use-case services
│  ├─ {APP_NAME}.Analysis/                # identification, inspection, detectors, tagging
│  ├─ {APP_NAME}.Infrastructure.Persistence/   # EF Core SQLite, FTS5, migrations
│  ├─ {APP_NAME}.Infrastructure.Storage/       # GoogleDrive/ and LocalFolder/ providers
│  ├─ {APP_NAME}.Infrastructure.Platform/      # DPAPI secrets, cache, external-edit, AI client, clock, file system
│  ├─ {APP_NAME}.Bridge/                  # contracts (DTOs), dispatcher, handlers — host-agnostic
│  ├─ {APP_NAME}.App/                     # WPF host (thin): WebView2, windowing, DI composition root
│  └─ {APP_NAME}.Tools/                   # console: contract-gen, live-check, seed-fixtures
├─ tests/
│  ├─ {APP_NAME}.UnitTests/
│  ├─ {APP_NAME}.IntegrationTests/
│  └─ {APP_NAME}.ArchitectureTests/
├─ ui/                                    # Vite + React + TypeScript
│  ├─ src/core/        # framework-agnostic logic: generated contract, bridge client, stores, fixture bridge
│  ├─ src/views/       # REFERENCE views: functional, unstyled, replaceable
│  ├─ src/theme/       # tokens.css (placeholder values) + structure.css (layout only)
│  ├─ src/i18n/        # tr-TR.json, en-US.json
│  └─ src/app/         # composition: router/shell wiring only
├─ build/              # verify.cmd, publish.cmd, sign.cmd, live-check.cmd
├─ installer/          # setup.iss (Inno Setup script)
└─ docs/
```

### 6.2 Layer rules (enforced by `{APP_NAME}.ArchitectureTests`)

- `Domain` → depends on nothing.
- `Application` → `Domain` only. Defines ports: `IStorageProvider`, `INodeRepository`, `IOutbox`, `ISecretStore`, `IDescriptionProvider`, `IClock`/`TimeProvider`, `IFileSystem`, `IContentCache`, `IExternalEditHost`, `IEventPublisher`, etc.
- `Analysis` → `Domain`, `Application`.
- `Infrastructure.*` → `Domain`, `Application` (+ their own third-party libs). **Never** referenced by `Application`, `Analysis`, `Bridge`.
- `Bridge` → `Application`, `Domain`. No WPF, no WebView2 types.
- `App` → composition root only; may reference everything. **No business logic in `App`.**
- `Tools` → may reference everything; not referenced by anything.
- Google SDK types **never** leak outside `Infrastructure.Storage/GoogleDrive`.

### 6.3 Engineering standards

- **C#:** file-scoped namespaces; `sealed` by default; records for DTOs/value objects; `Async` suffix; `CancellationToken` on every async API; `ILogger<T>` only (no `Console`); `TimeProvider` for time; expected failures return `Result<T, Error>` (exceptions are for bugs); `ConfigureAwait(false)` in library layers; XML docs on public APIs in `Application` and `Bridge`.
- **DI:** `Microsoft.Extensions.Hosting` generic host, constructor injection only.
- **TypeScript:** `strict: true`, `noUncheckedIndexedAccess`, no `any`, exhaustive `switch`, ESM, path aliases (`@core`, `@views`).
- **Time & IDs:** UTC ISO-8601 everywhere across the bridge; IDs are opaque strings.
- **Threading:** the WPF UI thread is never blocked by I/O; heavy work on background workers with bounded concurrency.

---

## 7. Storage & Google Drive integration (F-01, F-02, F-03)

### 7.1 `IStorageProvider` contract

Semantic members (design exact signatures yourself; all async, cancellable, `Result`-returning):

- `Capabilities` (supports revisions, supports export, supports change feed, supports server-side copy, max chunk size, …)
- `GetRootAsync`, `ListChildrenAsync(parent, cursor)`, `GetAsync(id)`
- `CreateFolderAsync`, `UploadAsync(parent, name, stream, mime, progress)` (resumable), `UpdateContentAsync(id, stream, expectedVersion)`, `DownloadAsync(id, destination, range?)`, `ExportAsync(id, targetMime)`
- `RenameAsync`, `MoveAsync`, `CopyAsync`, `TrashAsync`, `RestoreAsync`, `DeleteAsync`
- `ListRevisionsAsync`, `GetRevisionContentAsync`, `PinRevisionAsync(keepForever)`
- `SetMetadataAsync(description, appProperties)`
- `GetStartChangeTokenAsync`, `GetChangesAsync(token)`
- `GetQuotaAsync`, `GetAccountInfoAsync`

A **provider contract test suite** (shared abstract test class) must run against every provider: `LocalFolder` for real, `GoogleDrive` through a fake `HttpMessageHandler` that asserts exact REST calls (paths, `fields`, query params, scopes).

### 7.2 Google Drive specifics

- Library: `Google.Apis.Drive.v3` + `Google.Apis.Auth`. Never leak SDK types beyond the provider.
- **My Drive only.** `supportsAllDrives=false`; Shared Drives are out of scope.
- **Always specify `fields`** (partial responses). Needed fields at minimum: `id, name, mimeType, parents, size, md5Checksum, modifiedTime, createdTime, version, headRevisionId, trashed, explicitlyTrashed, description, appProperties, webViewLink, capabilities(canEdit,canDelete,canTrash,canRename,canMoveItemWithinDrive)`.
- **Scopes by mode:** `AppFolderOnly` → `drive.file`; `FullDrive` → `drive`. Request **nothing else**. Display name/email come from `about.get` (verify it is permitted under the chosen scope; if not, add the narrowest identity scope and document it).
- **OAuth:** installed-app flow through the **system browser**, **loopback redirect on a random free port, PKCE**. Serve a minimal plain-text success/failure page from the loopback listener (no styling). Implement a custom `IDataStore` backed by DPAPI-protected storage (no plaintext tokens on disk, ever).
- **OAuth client resolution order:** (1) client embedded at build time via an env var / untracked file (for the published build), (2) client JSON imported by the owner at runtime (`accounts.importOAuthClient`), stored DPAPI-protected, (3) none → every connect attempt returns `OAUTH_NOT_CONFIGURED` with a pointer to `docs/GOOGLE_SETUP.md`. Never commit a client id/secret.
- **Token failure:** `invalid_grant`/revoked → mark account `ReauthRequired`, emit `account.stateChanged`, stop that account's sync, keep cached data readable.
- **Testing-mode warning:** for an External consent screen in "Testing" status, Google issues refresh tokens that expire after about 7 days. `docs/GOOGLE_SETUP.md` must explain this and the two remedies (set publishing status to *In production* for personal use and accept the "unverified app" screen; or re-authenticate weekly). Verify wording against current Google docs.
- **Resilience (Polly):** retry with exponential backoff + jitter on 429, 403 with reasons `rateLimitExceeded`/`userRateLimitExceeded`/`backendError`, and 5xx; honor `Retry-After`; global request concurrency limiter (default 4, configurable); circuit-breaker for sustained failure → `OFFLINE` state.
- **Resumable uploads:** chunk size multiple of 256 KB (default 8 MB, configurable); survive network drops and app restarts (persist the resumable session URI in the outbox when possible); progress + cancellation.
- **Google-native files** (`application/vnd.google-apps.*`): no binary content. Preview/analyze via export (Docs → `text/plain`, Sheets → `text/csv`, Slides → `text/plain`; respect export size limits); "edit" means opening `webViewLink` in the **system browser**. Optionally offer an experimental embedded view behind a setting — note Google may block sign-in in embedded browsers.
- **Shortcuts** (`application/vnd.google-apps.shortcut`): shown as link nodes; never followed implicitly.
- **Name collisions:** Drive allows duplicate names in one folder; the app enforces a configurable policy (`block` | `auto-suffix`, default `block`) to prevent chaos.
- **Change feed:** use `changes.getStartPageToken` and `changes.list` (`includeRemoved=true`, `spaces=drive`, minimal `fields`). Persist the token per account. Invalid/expired token → trigger a **full re-index**.
- **`appProperties` limits:** key+value ≤ 124 bytes, ≤ 30 per file (verify). Therefore store only compact items there (see 7.3).
- **Revisions:** Drive may purge old revisions of binary files unless pinned (`keepForever`), and pinning has a per-file limit (verify). Snapshot logic must handle pin failures by falling back to server-side copy for that file.
- **Quota:** surface `about.storageQuota` through `storage.getUsage`.

### 7.3 What lives where on Drive (durability design)

| Data | Location | Why |
|---|---|---|
| File content | the Drive file | obvious |
| Human summary ("what is this for") | Drive file `description` field | visible even in Drive's web UI; durable |
| Compact machine facts: `cairn.k` (kind id), `cairn.s` (source: rule/ai/user), `cairn.dh` (short hash of the description the app last wrote), `cairn.ch` (short content hash analyzed), `cairn.op` (transient op id for idempotent replay) | file `appProperties` | tiny, private to the app, survive moves |
| Tags, tag definitions, tag assignments, projects list, repo kind, naming policy | `.cairn/` folder inside each repo: `repo.json`, `tags.json`, `projects.json` | portable, human-readable, versioned by Drive revisions |
| Snapshots | `.cairn/snapshots/<timestamp>-<id>.json` (+ optional `data/` copies) | commit-like records |
| Workspace-level settings that must roam | `{APP_NAME} Workspace/.cairn/workspace.json` | rebuild after reinstall |

- The `.cairn` folder name is configurable constant; the UI contract exposes a "hide system folders" filter (default on).
- Manifest writes are **debounced (2–5 s), coalesced, optimistic-concurrency-guarded** (version check) with **per-entry last-writer-wins merge** on conflict. Schema-versioned with forward migration.
- **User-edit detection:** if a remote `description` hash ≠ stored `cairn.dh`, treat the description as **user-authored** (`source=user`) and never overwrite it automatically.
- **Idempotent replay:** before replaying an interrupted upload/create from the outbox, query the parent for an `appProperties` match on `cairn.op`; if found, adopt it instead of creating a duplicate.

### 7.4 LocalFolder provider (dev/test only)

Implements the full contract on a directory (IDs via an index file, revisions via a `.revisions` side folder, change feed via a journal). Enabled by `--local-provider <dir>` or a setting; **hidden in Release unless the setting is on**. It exists so the whole product — sync, analysis, UI, bridge — is runnable and testable **without Google credentials**. It is *never* presented as the product's storage in the UI copy.

---

## 8. Data model & local database (F-04, F-19, F-18)

**Engine:** SQLite via EF Core, WAL mode, one serialized writer + concurrent readers, batched transactions. Schema versioned; on incompatible version → drop and rebuild from Drive (cache is disposable). Verify FTS5 availability at startup; if missing, degrade to indexed `LIKE` search and log it.

**Entities** (design columns yourself; requirements in bold):

`Account`, `Workspace`, `Repository`, `Project`, **`Node`** (id = provider id, accountId, parentId, name, isFolder, mime, size, md5, version, headRevisionId, modifiedTime, trashed, repoId, projectId, `pathText`), **`NodeInsight`** (kind, role, summary, confidence, source, sensitivity, contentHash, analyzedAt, rulesetVersion), `Tag`, `NodeTag`, `Favorite`, `Snapshot`, `SnapshotEntry`, `OperationJournal` (activity/undo), **`OutboxOperation`** (opId, type, payload, state, attempts, nextAttemptAt, lastError), `ConflictRecord`, `TransferJob`, `AnalysisJob`, `ChangeToken`, `SettingsKv`, `SchemaVersion`.

**Required indexes:** `(accountId, parentId, isFolder, name)`, `(accountId, repoId, projectId)`, `(accountId, md5, size)` (duplicates), `(accountId, trashed)`, `NodeTag(tagId, nodeId)`, `OutboxOperation(state, nextAttemptAt)`.

**FTS:** virtual table over name, description/summary, tag names, path, and **indexed text content** of small non-sensitive text files (cap per file; never index `Sensitive` nodes).

**Scale target:** 100,000 nodes — paged folder listing from DB < 300 ms for a 10,000-child folder; memory steady-state < 400 MB; verified by a synthetic-dataset performance test using the LocalFolder provider.

---

## 9. Sync engine (F-19)

- **Initial index:** breadth-first from the Workspace root with paging, batched DB writes, progress events; resumable after interruption.
- **Incremental:** poll `changes.list` — default every 30 s when the window is focused, 5 min when not (configurable). Apply removals/moves/renames/trash. Debounce bursts.
- **Push:** every local mutation is applied **optimistically** to the DB and written to the **durable Outbox first** (before acknowledging the UI). A worker replays with per-item backoff. Failed items surface in the UI, never vanish silently.
- **Content conflicts:** saving requires `baseVersion`. If Drive's `version` moved, **never overwrite**. Store the local copy as a conflict copy (`<name> (conflict <local-time>)`), create a `ConflictRecord`, emit an event. Resolutions: `keepLocal` (re-upload as new revision after fresh confirm), `keepRemote` (discard local), `keepBoth`, and for text a side-by-side diff payload to merge manually.
- **Structural conflicts** (remotely moved/deleted while edited locally): detect, record, require explicit resolution.
- **Offline:** detect via network-change events + request failures; reads served from cache/DB; writes queue; state exposed via `sync.stateChanged` (`idle | indexing | syncing | offline | paused | error | reauthRequired`).
- **Crash recovery at startup:** resume transfers, replay outbox, re-adopt external-edit sessions, clear stale locks.
- **Auto-analysis hook:** on upload completion and on discovering new/changed nodes (md5/version changed), enqueue analysis (Section 10). For local-origin uploads, **analyze from the local source before upload** (no re-download).
- **Manifest merge** (Section 7.3) runs inside the same worker to avoid races.
- **Idempotency:** every outbox op has a client-generated `opId`; replays are safe.

---

## 10. Analysis engine — insight, projects, tags (F-10, F-11, F-12)

### 10.1 Pipeline (per node)

1. **Identify** — extension map, MIME, **magic-byte sniffing** (first ≤ 8 KB), Google-native mapping → `kind` (taxonomy in Appendix A).
2. **Inspect** (reads at most `MAX_ANALYSIS_READ_BYTES`; use range reads on Drive; for zip-based formats read the central directory from the tail):
   - text-ness + encoding + EOL; programming language (extension, shebang, content cues);
   - structured peeks: JSON/YAML/TOML/XML top-level keys, CSV/TSV header columns, `package.json` name/description/scripts/dependencies, `.csproj` target framework/UseWPF, HTML `<title>`/meta description, Markdown H1, first meaningful doc-comment/docstring (≤ 200 chars);
   - Office/ODF/EPUB (zip+XML): title, author, sheet names, slide count — metadata only;
   - images: dimensions via hand-written header readers (PNG/JPEG/GIF/WebP/BMP; SVG as text) — no heavy imaging dependency;
   - PDF: title/page count via lightweight parsing, tolerate failure; archives: entry count + top-level names.
3. **Role inference** — data-driven catalog `config/file-roles.json`: glob patterns + priority + optional content predicates → `role` (e.g. `readme`, `license`, `changelog`, `ci-workflow`, `dockerfile`, `package-manifest`, `lockfile (generated)`, `entry-point`, `test`, `db-migration`, `env-config (sensitive)`, `build-output (generated)`, `web-page`, `stylesheet`, `client-script`, `design-asset`, …). Each role has a **summary template** with slots.
4. **Summarize (no AI required)** — fill templates from inspected facts so even AI-off descriptions are meaningful. Example: *"TypeScript React component in the `ui` project; exports 3 components; ~120 lines. Part of the web-site project 'portfolio'."* Keep to 1–3 sentences, plain words, in `DEFAULT_UI_LANGUAGE`-neutral English **or** localized via templates (templates are localizable data: `tr-TR`, `en-US`).
5. **Optional AI enrichment** (Section 11) may improve `summary`/`role`; it never replaces `user`-authored text.
6. **Persist + write back:** DB, then outbox → Drive `description` + `appProperties`. Precedence **user > ai > rule**.

**Confidence** is a 0–1 score per insight, derived from the strength/number of signals; expose it.

**Re-analysis:** when `md5Checksum` changes; on manual `insight.reanalyze`; when the ruleset version changes (only for `rule`-sourced insights, lazily in background). Queue is prioritized: user-initiated > visible-in-UI > background; concurrency `max(1, cores/4)`; cancellable; resumable.

**Sensitivity:** classify as `Sensitive` when names/contents match secret patterns (`.env*`, `*.pem`, `*.key`, `*.pfx`, `id_rsa*`, `credentials*.json`, `client_secret*.json`, `*token*.json`, private-key PEM headers, common API-key prefixes, high-entropy token heuristics). `Sensitive` nodes: **excluded from content indexing, previews of contents in logs, and AI**; the insight flags them so the UI can warn.

### 10.2 Project detection (folder-level)

- Data-driven catalog `config/project-markers.json`: entries `{ id, name, tags[], markers[{ path-glob, contains?, jsonPath? }], weight, excludes[], parentSensitive? }`. User overrides/additions live in `%LOCALAPPDATA%\{APP_NAME}\rules\` and merge over the shipped catalog (user wins on equal `id`).
- Score each directory from marker evidence (depth-limited, default 4). **Nearest root wins**, except recognized **mono-roots** (npm/pnpm/yarn workspaces, `.sln` with multiple projects, Cargo/Go workspaces, Nx/Turbo/Lerna) which become `Mono` repos whose children are detected as **projects**.
- **Auto-apply** tags when confidence ≥ 0.6 (configurable); below that, surface as **suggestions**.
- **Web-site projects are the primary case** (see Appendix B): static sites (`index.html` + assets), SSG/frameworks (Next, Nuxt, Astro, Gatsby, Remix, SvelteKit, Docusaurus, Eleventy, Hugo, Jekyll), SPAs (Vite/CRA + React/Vue/Svelte/Angular), CMS (WordPress), PHP sites. They receive tag **`type:web-site`** plus technology tags (`tech:nextjs`, `tech:react`, …). Other types (dotnet, node-package, python, java, rust, go, **minecraft-plugin/mod**, unity, unreal, godot, android, ios, notebooks, docs, design, media) per Appendix B.
- Every detector requires **positive and negative fixture trees** in tests.

### 10.3 Tags

- Model: `name` (unique per workspace, case-insensitive), optional `namespace` (`type:`, `tech:`, `status:`), `description`, `origin` (`auto|user`), `colorToken` (nullable string — **you do not choose colors**), `isSystem`.
- Assignable to nodes, projects, repos. **Inheritance:** a tag on a folder/project is queryable on descendants (filter "include descendants").
- **Suppression:** if the owner removes an `auto` tag, record a suppression so it is not re-applied.
- Bulk assign/unassign; rename/merge/delete with usage counts; persisted in `.cairn/tags.json` (Section 7.3).

---

## 11. Optional AI describer (F-20)

- Port `IDescriptionProvider`: `DescribeAsync(request, ct) → { summary, role?, kindHint?, tagSuggestions[], confidence }`. Request carries node metadata, a **bounded excerpt**, sibling names, and project context.
- Implementation `AnthropicDescriptionProvider`: `HttpClient` → `POST https://api.anthropic.com/v1/messages`, headers `x-api-key`, `anthropic-version: 2023-06-01`, `content-type: application/json`. Model from settings (default `AI_MODEL_DEFAULT`). Request a **strict JSON** reply; validate against a schema; retry/timeouts via Polly; handle 429/5xx.
- **Off by default.** Enabling requires `ai.setKey` **and** `ai.acknowledgeDataSharing=true` (consent recorded with timestamp). Per-repo opt-out flag. API key stored DPAPI-protected.
- **Privacy guards:** never send `Sensitive` nodes, binaries, or images (vision off by default); excerpt cap (default 6 KB); strip obvious secrets from excerpts; daily request budget (default 500, configurable) → `AI_BUDGET_EXCEEDED`.
- **Prompt-injection hardening:** file contents are **untrusted data**. The prompt template (an editable resource file) must state this; the model has **no tools**; its output is validated and only ever written to `description`/insight fields — never executed, never used to change files or paths.
- Provide `ai.test` (a harmless connectivity check) and a clear status (`notConfigured | consentRequired | ready | budgetExceeded | error`).
- Tested entirely with a fake handler; no real key needed. `docs/AI_SETUP.md` explains setup and costs honestly.

---

## 12. Repositories, snapshots, activity, organizer (F-13 … F-17)

### 12.1 Repositories & mono-repos

- `repos.create({ name, kind: single|mono, template? })` creates the Drive folder + `.cairn/repo.json`. Templates are **data** (`folder skeleton` lists), shipped sample: `empty`, `web-site`, `documents`.
- **Mono repo:** `projects.json` lists `{ id, path, type, tags[], detected: bool, confirmed: bool }`; auto-detected projects require no user action but can be confirmed/edited/removed; moving a folder between projects is supported.
- **Local folder import** (`import.plan` → `import.execute`): `plan` is a **dry run** returning counts, total size, ignored items (default ignore: `node_modules`, `.git`, `bin`, `obj`, `dist`, `build`, `__pycache__`, `.venv`, `target`, `.idea`, `.vs` — data in `config/ignore-patterns.json`, editable), name-sanitization changes, collisions, and detected projects/tags. `execute` is queued through the outbox, resumable, restartable, cancellable; preserves structure; analyzes from local source.
- **Repo overview** data: totals by kind, size, last activity, language breakdown (by bytes and by file count), project list with types/tags, README excerpt, recent activity.
- `FullDrive` mode only: `repos.adoptExistingFolder` to turn an existing Drive folder into a repo.
- Export: `repos.exportZip` (host streams a ZIP to a local path chosen via native dialog).

### 12.2 Snapshots (commit-like)

- `snapshots.create({ scope, message, mode })`, `mode ∈ { pin (default), copy }`.
  - `pin`: record `{ nodeId, path, size, md5, headRevisionId }` and pin each head revision (`keepForever`) where supported; on pin failure fall back to `copy` for that file.
  - `copy`: server-side `files.copy` into `.cairn/snapshots/<id>/data/`.
- `snapshots.diff(a, b|current)` → added / removed / modified / moved-renamed lists; **text diff on demand** (DiffPlex) from revision content.
- `snapshots.restore({ snapshotId, scope, paths? })` is **non-destructive**: writes restored content as *new revisions*; recreates files from trash when possible. Be explicit in the result about what **cannot** be restored (permanently deleted without a copy).
- No branches, merges, or git protocol (Section 23).

### 12.3 Activity & undo

- Every app-initiated mutation writes to `OperationJournal` (who/when/what/before/after). The feed is queryable and paged. **Undo** supported for rename, move, trash↔restore, tag changes, description edits, and organizer applies; irreversible ops are flagged as such. Retention configurable (default 90 days / 10 000 entries).

### 12.4 Inbox & Organizer

- Unsorted uploads go to `Inbox`. `organize.suggest(nodeIds)` returns ranked destinations (repo/project/folder) with **reasons and confidence**, using kind, role, detected project type, tags, sibling similarity, and existing structure; AI may refine if enabled.
- `organize.apply(plan)` runs through the outbox, is journaled, and is **undoable**. `autoOrganize` setting **defaults off**; when on, applies only above a confidence threshold.

### 12.5 Duplicates

- Group by `(md5Checksum, size)` from Drive metadata (no downloads). Actions: choose which to keep, trash the rest — **never automatic**.

### 12.6 Search

- `search.query({ text, filters: { tagIds, kinds, repoId, projectId, modifiedFrom/To, sizeMin/Max, includeTrashed }, cursor })` using FTS with ranking, prefix matching, and highlights; `search.suggest` for type-ahead. Works fully offline from the DB.

---

## 13. Editing & preview (F-07, F-08, F-09)

### 13.1 In-app text/code editing

- Component: **CodeMirror 6** (functional only; styled solely through CSS variables from `tokens.css`).
- `nodes.readText` → `{ text, encoding, eol, bom, version, etagLike }`; `nodes.writeText` requires `baseVersion`; preserve BOM and line endings; refuse binary or `> MAX_INLINE_EDIT_SIZE` with `TOO_LARGE_TO_EDIT` (UI offers external edit).
- Dirty-state handling, `Ctrl+S`, optional autosave (setting, default off), conflict-safe save (Section 9), unsaved-changes guard contract for the UI.

### 13.2 Preview

| Kind | Mechanism |
|---|---|
| text/code | read-only/edit via editor |
| Markdown | render via `marked` → **DOMPurify-sanitized** HTML |
| image (png/jpg/gif/webp/svg) | `<img>` from a cache URL; **SVG is only ever loaded as an image, never inlined into the DOM** |
| PDF | WebView2's built-in viewer via cache URL (verify), else metadata fallback |
| audio/video | `<audio>/<video>` from cache URL (verify range-request support; if absent, document the limitation) |
| CSV/TSV | host parses first N rows (default 1 000) → table data |
| JSON | host validates + returns structure; UI renders |
| archives | entry listing (no extraction in preview) |
| HTML | **sandboxed**: separate virtual host + `<iframe sandbox>` without `allow-same-origin`+`allow-scripts` together; no script execution in the app origin |
| other | metadata + insight only |

Previews are streamed from the **content cache**; the bridge returns **URLs**, never file bytes.

### 13.3 External edit sessions (binaries, big files, Office files)

`nodes.openExternal` downloads to `%LOCALAPPDATA%\{APP_NAME}\external\<nodeId>\<name>`, opens with the default app, and watches the file (`FileSystemWatcher` + debounce + lock detection; handle save-via-temp-and-rename editors). On change → outbox update with conflict check. Sessions are listable/endable; survive restarts (re-adopted); cleaned after a configurable time. Executable/script extensions require an explicit confirmation token (Section 18).

---

## 14. WebView2 host & bridge protocol (F-22)

### 14.1 Host (WPF `{APP_NAME}.App`)

- One `MainWindow` containing **only** the `WebView2` control. Native window chrome stays **default** (custom title bars are a design decision — not yours).
- `CoreWebView2Environment` with user-data folder `%LOCALAPPDATA%\{APP_NAME}\webview2`. Detect missing runtime → plain native dialog (Section 4).
- **Virtual hosts** (reserved TLD, HTTPS-mapped):
  - `https://ui.appassets.example` → UI folder (override folder if valid, else bundled) — access kind **Deny**.
  - `https://cache.appassets.example` → preview/content cache — **DenyCors** (embedding allowed, scripted cross-origin reads blocked).
  - `https://usercontent.appassets.example` → sandboxed HTML previews.
- **Hardening:**
  - `NavigationStarting`/`FrameNavigationStarting`: cancel everything not on the allowlist (the three virtual hosts).
  - `NewWindowRequested`: handle; allow only `http`, `https`, `mailto` → system browser; block all other schemes.
  - `PermissionRequested`: deny all by default.
  - `DownloadStarting`: cancel and route through the app's download flow.
  - `AreHostObjectsAllowed = false` (the bridge is message-based only); `IsStatusBarEnabled = false`; `IsPasswordAutosaveEnabled = false`; `IsGeneralAutofillEnabled = false`.
  - Dev tools / default context menu / browser accelerator keys / zoom control: **settings-driven**; Release defaults: dev tools off, default context menu off, accelerator keys off, zoom control off. Debug defaults: all on.
  - `ProcessFailed` → auto-reload with throttling + log.
- **Dev/override loading:** `--ui-dev-url <url>` points the view at a Vite dev server (Debug/dev flag only). `ui-override` folder mechanism in Section 16.5.
- **Window state:** persist and restore size/position/maximized state with multi-monitor sanity checks. Per-monitor DPI v2 and `longPathAware` in `app.manifest`.
- **Single instance:** named mutex; a second launch activates the first window and forwards arguments (file paths → `app.openPaths` event).
- **Command line:** `--ui-dev-url`, `--data-dir`, `--local-provider <dir>`, `--reset` (clear cache/settings after confirmation via native dialog), `--portable`.
- **Unhandled exceptions:** hook `DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`; log, show a minimal native dialog, preserve outbox; never lose queued work.
- **Drag & drop of files from Explorer:** the UI uses `chrome.webview.postMessageWithAdditionalObjects` with the dropped `File` objects so the host receives **native paths** (`CoreWebView2File.Path`); bytes never pass through JavaScript. Host validates existence/readability, handles directories recursively, and rejects reparse-point escapes.
- **Background color:** exposed as bridge call `window.setBackground(color)` so the final UI can avoid white-flash at startup (default neutral).

### 14.2 Message protocol

- Transport: UI → host `window.chrome.webview.postMessage(jsonString)`; host → UI `PostWebMessageAsJson`. The host **verifies the origin of every message** (`e.Source` equals the UI virtual host or dev URL) and enforces a max message size (default 1 MB). All WebView2 calls marshal to the UI thread; request handling runs on background threads.
- Envelope (JSON, camelCase, enums as strings):

```json
{ "v": 1, "id": "uuid", "type": "req|res|evt|cancel",
  "method": "nodes.list", "params": {},
  "result": {}, "error": { "code": "NOT_FOUND", "message": "dev-only English text",
                           "retryable": false, "details": {} } }
```

- **Handshake:** `app.hello` → `{ contract: { major, minor }, hostVersion, capabilities[], features{}, settingsSnapshot, uiStatus }`. A UI declares the contract it needs; compatibility = same `major`, host `minor` ≥ UI `minor`.
- **Cancellation:** `{ type:"cancel", id }` aborts the in-flight request's `CancellationToken`.
- **Pagination:** opaque `cursor` + `limit` (default 200).
- **Mutations** accept an optional `idempotencyKey`. Destructive ones accept/require a `confirmationToken` (host issues a token via a prior `*.prepare` call or error `CONFIRMATION_REQUIRED` that carries what must be confirmed).
- **Events** are coalesced/throttled (≥ 100 ms per topic) to protect the UI.
- **Errors:** only **codes** cross the bridge for UI use (Appendix C); `message` is English and developer-oriented. The UI localizes by code.
- **Never** send file bytes over the bridge; use cache URLs or host-side paths.

---

## 15. Bridge API surface (contract the UI builds against)

Design exact DTOs yourself; **generate** `ui/src/core/contract.ts` and `docs/BRIDGE_PROTOCOL.md` from the C# contracts via `{APP_NAME}.Tools contract-gen`. A script `npm run contract:check` and a CI-free `build\verify.cmd` step must fail if the generated files are stale.

| Namespace | Methods (non-exhaustive minimum) |
|---|---|
| `app` | `hello`, `getInfo`, `getUiStatus`, `quit`, `restart`, `openLogsFolder`, `openExternalUrl` |
| `window` | `setBackground`, `setTitle` |
| `settings` | `get`, `update`, `reset`, `getUiState`, `setUiState` |
| `accounts` | `list`, `connect`, `cancelConnect`, `disconnect`, `setActive`, `importOAuthClient`, `getOAuthStatus` |
| `workspace` | `get`, `ensure`, `getStats` |
| `repos` | `list`, `create`, `rename`, `delete`(guarded), `get`, `getOverview`, `exportZip`, `adoptExistingFolder`(FullDrive) |
| `projects` | `list`, `get`, `confirm`, `update`, `remove`, `move` |
| `nodes` | `list`, `get`, `getPath`, `createFolder`, `createFile`, `rename`, `move`, `copy`, `trash`, `restore`, `prepareDelete`, `deletePermanent`, `readText`, `writeText`, `getPreview`, `openExternal`, `openInBrowser`, `revisions`, `restoreRevision`, `setFavorite`, `listFavorites`, `listTrash`, `emptyTrash`(guarded) |
| `transfers` | `uploadPaths`, `download`, `list`, `pause`, `resume`, `cancel`, `retry`, `clearFinished` |
| `import` | `plan`, `execute`, `status` |
| `insight` | `get`, `reanalyze`, `setSummary`, `setKind`, `bulkReanalyze`, `getQueueStatus` |
| `tags` | `list`, `create`, `update`, `merge`, `delete`, `assign`, `unassign`, `bulkAssign`, `getUsage`, `listSuggestions`, `acceptSuggestion`, `dismissSuggestion` |
| `search` | `query`, `suggest` |
| `sync` | `getStatus`, `syncNow`, `pause`, `resume`, `listOutbox`, `retryOutbox`, `discardOutbox`(guarded), `listConflicts`, `getConflict`, `resolveConflict` |
| `snapshots` | `create`, `list`, `get`, `diff`, `textDiff`, `restore`, `delete`(guarded) |
| `activity` | `list`, `undo` |
| `organize` | `listInbox`, `suggest`, `apply`, `getSettings` |
| `duplicates` | `scan`, `list`, `resolve` |
| `external` | `listSessions`, `endSession` |
| `ai` | `getStatus`, `setKey`, `clearKey`, `acknowledgeDataSharing`, `test`, `getUsage` |
| `storage` | `getUsage`, `getCacheStats`, `clearCache` |
| `rules` | `getCatalogInfo`, `reload`, `validate` (validates user rule overrides) |
| `diagnostics` | `exportBundle`, `getHealth`, `log` (UI→host log relay) |

**Events (host → UI):** `account.stateChanged`, `sync.stateChanged`, `sync.progress`, `node.changed` (batched), `transfer.progress`, `analysis.progress`, `insight.updated`, `conflict.created`, `external.sessionChanged`, `app.openPaths`, `settings.changed`, `error.global`.

---

## 16. UI layer rules — the "no design" contract (F-23, F-24)

> **Read this section twice. The owner will have the UI professionally designed. Your UI exists so the product is complete and usable, and so a designer has a precise, testable surface to replace.**

### 16.1 Architecture

- `ui/src/core` — **framework-agnostic TypeScript, zero markup, zero CSS**:
  - generated `contract.ts`;
  - `bridge` client: promise-based requests, `AbortSignal` cancellation, event subscriptions, timeouts, handshake + version check, reconnect/ready state;
  - **stores** (Zustand or equivalent): tree/navigation, selection, listing cache, transfers, sync status, conflicts, tags, search, settings, activity, editor sessions, organizer, snapshots, AI status;
  - i18n loader, formatting helpers (bytes, dates via `Intl`), keyboard-shortcut **registry (data)**.
  - Unit-tested with a mock bridge.
- `ui/src/views` — **reference views**: functional, **deliberately unstyled**, "wireframe-grade". Pure functions of store state + callbacks.
- `ui/src/theme/tokens.css` — **every** visual value as a CSS custom property with **neutral placeholder values** (browser-default look: `system-ui`, grayscale, 1 px borders). Header comment: `PLACEHOLDER — replaced by the owner's designer`.
- `ui/src/theme/structure.css` — **layout only** (grid areas, overflow, min-widths, scroll containers, panes). No colors, fonts, shadows, radii, transitions.
- `ui/src/i18n/{tr-TR,en-US}.json` — **all** user-visible strings by key. Host messages are codes, not strings.

### 16.2 What reference views MUST do

- Use semantic HTML: `nav`, `main`, `aside`, `section`, `ul/li`, `table`, `button`, `dialog`, `form`, `label`, `input`.
- Every interactive element has an accessible name, a `data-testid`, and a `data-action` hook.
- Class names are structural only (`.tree`, `.tree__row`, `.inspector__section`); no utility-class systems.
- Cover **every** F-feature with a reachable, working control. Required screens/areas:
  1. Onboarding / connect account (incl. `OAUTH_NOT_CONFIGURED` state with import-client control)
  2. Repo list & create
  3. Repo overview (stats, README, projects)
  4. File browser (tree + list, breadcrumbs, sort/filter, multi-select, drag-drop upload/move)
  5. Node inspector (insight, tags, description edit, revisions, details, open actions)
  6. Text editor
  7. Previewer
  8. Search (global + filters)
  9. Tags manager (+ auto-tag suggestions)
  10. Inbox & Organizer
  11. Duplicates
  12. Snapshots (list/create/diff/restore)
  13. Activity & Undo
  14. Sync status, Outbox, **Conflict resolver**
  15. Transfers panel
  16. Trash
  17. Settings (general, accounts, storage/cache, sync, analysis & rules, AI, advanced/dev)
  18. About & diagnostics (export log bundle)
- Handle **every** state per view: loading, empty, error (by code), offline, reauth-required, not-configured, permission-denied.

### 16.3 What reference views MUST NOT do (violations fail the build review)

No colors, fonts, font sizes beyond tokens, spacing aesthetics, shadows, gradients, border radii beyond tokens, animations, transitions, icons, emoji, images, logos, illustrations, empty-state art, custom scrollbars, skeleton loaders, theme/dark-mode toggles or switching logic, third-party component or CSS libraries. Inline `style` attributes are forbidden (except dynamic geometry that cannot be expressed otherwise, each justified in a comment). Plain text labels only.

A **lint rule/test** must enforce: no `style=` attributes, no color literals or `font-family` outside `tokens.css`, no imports from UI kits.

### 16.4 Dev fixture bridge (designer workflow)

Provide `core/fixtureBridge`: an in-memory implementation of the bridge contract with realistic generated data, selectable by a build flag/URL param (`?bridge=fixture`), so the UI can be developed in a plain browser without the host. It must be **tree-shaken out of production builds** (verify by test: the production bundle contains no fixture strings) and clearly labeled `DEV ONLY` in code and in `docs/UI_REPLACEMENT_GUIDE.md`.

### 16.5 Replacement mechanisms (all mandatory)

1. **`ui-override` folder** — if `%LOCALAPPDATA%\{APP_NAME}\ui-override\` contains `index.html` and `ui-manifest.json`, the host serves it instead of the bundled UI **provided** the manifest's `contract` is compatible. Incompatible/invalid → fall back to the bundled UI, log the reason, expose it via `app.getUiStatus`. No recompilation needed to swap UIs.
2. **`ui-manifest.json`** schema: `{ "name", "version", "contract": "1.x", "entry": "index.html", "minHostVersion", "features": [] }` — documented and validated (JSON Schema shipped in `docs/`).
3. **`--ui-dev-url`** for a Vite/other dev server.
4. **Build output** is a static bundle with **relative base** (`base: './'`), **no CDN, no external fonts/scripts**, works offline, **strict CSP** (`script-src 'self'`, no inline/eval scripts, `connect-src 'none'`, `object-src 'none'`, `base-uri 'none'`, `form-action 'none'`; `style-src 'self' 'unsafe-inline'` only because of CodeMirror's dynamic styles — document it). Apply CSP via the most robust available mechanism (meta tag in the bundled UI; response headers for `usercontent` content) and record the choice.
5. **`docs/UI_CONTRACT.md`** — for **every** view: purpose, bridge calls used, store slices, events handled, all states, destructive actions that need confirmation, expected keyboard shortcuts. **`docs/UI_REPLACEMENT_GUIDE.md`** — step-by-step: how a designer or agent builds a new UI against the contract, tests it with the fixture bridge, and installs it via `ui-override`.

### 16.6 i18n

`tr-TR` (default) and `en-US` complete for every string; missing-key test fails the build; pluralization/formatting via `Intl`. Language switchable at runtime through settings.

---

## 17. Settings, configuration, secrets (F-21)

- **Folder:** `%LOCALAPPDATA%\{APP_NAME}\` → `data\` (SQLite), `cache\content\`, `cache\preview\`, `external\`, `logs\`, `webview2\`, `rules\` (user rule overrides), `ui-override\`, `secrets\`, `settings.json`.
- **Portable mode:** `--portable`/`portable.flag` next to the exe → data beside the exe (DPAPI remains user-bound; document).
- `settings.json`: schema-versioned, **atomic writes** (temp + rename), migration on load, validation with safe defaults.
- **Precedence:** defaults < `settings.json` < env vars (`{APP_ID}_*`) < CLI args.
- **Setting groups & defaults** (must all exist, with sane defaults): general (language), sync (poll intervals, max parallel requests, pause), cache (limit, LRU), analysis (autoAnalyze=true, readBytes, confidenceThreshold=0.6, ignore patterns), naming policy (`block`), safety (confirmPermanentDelete=true, confirmExecutableOpen=true), organizer (autoOrganize=false, threshold), editor (autosave=false, maxInlineSize), snapshots (default mode `pin`), activity retention, AI (enabled=false, model, budget, consent), advanced (devtools, default context menu, accelerator keys, local provider, drive scope mode), privacy (log level).
- **Secrets** (tokens, AI key, imported OAuth client) → `ISecretStore` over **DPAPI (CurrentUser)**; never in `settings.json`, never logged. `app.wipeLocalData` (via `storage.clearCache` + `accounts.disconnect` + secrets removal) must exist and be tested.

---

## 18. Security & privacy

Assume **everything coming from files, file names, Drive metadata, and AI output is hostile.**

- **No `innerHTML` with untrusted data.** Markdown is sanitized with DOMPurify; SVG only as `<img>`; HTML previews only in sandboxed frames from the separate virtual host.
- **File-name safety:** strip invalid Windows characters, reserved device names (`CON, PRN, AUX, NUL, COM1–9, LPT1–9`), trailing dots/spaces, enforce ≤ 255 chars, normalize Unicode, prevent path traversal and zip-slip (import/extract), reject reparse-point escapes. Tested with an adversarial name corpus.
- **Executable safety:** never run downloaded content automatically. `openExternal` for risky extensions (`.exe .bat .cmd .ps1 .msi .js .vbs .wsf .lnk .scr .com .jar .reg .hta …`) requires an explicit confirmation token.
- **OAuth:** system browser + PKCE + loopback; minimal scopes; DPAPI token storage; revocation on disconnect.
- **WebView2:** origin-checked bridge, navigation allowlist, no host objects, no remote content, permissions denied, downloads intercepted (Section 14).
- **Logging:** structured, with **redaction** of tokens/keys (`ya29.`, `1//`, `Bearer`, `sk-ant-`, `x-api-key`, client secrets). Never log file contents.
- **Network:** TLS defaults only; no certificate-validation bypasses; no other outbound hosts (R-19).
- **Dependencies:** `dotnet list package --vulnerable --include-transitive` and `npm audit` must be clean or each exception documented.
- **Data minimization:** no analytics; AI path is opt-in with recorded consent; `Sensitive` nodes are excluded from indexing/AI.
- **Privacy draft:** `docs/PRIVACY.md` accurately describes data flows (clearly marked *draft, not legal advice*).

---

## 19. Reliability, performance, errors, logging (F-25)

- **Async everywhere**; cancellation tokens honored; UI thread never blocked > ~50 ms by I/O.
- **Durability:** outbox write precedes UI acknowledgement; atomic file writes; startup recovery routines (Section 9).
- **Errors:** typed `Error` (code, retryable, details) from Domain/Application; mapped to Appendix C codes at the bridge; one global error channel (`error.global`) for unexpected failures; no swallowed exceptions.
- **Logging:** Serilog rolling files (10 MB × 10), levels from settings, correlation IDs spanning bridge request → services → provider calls. `diagnostics.exportBundle` zips redacted logs + redacted settings + system info (no tokens, no content).
- **Performance budgets** (measured by tests/harness where feasible): cold start to interactive shell ≤ 2 s with warm cache; 10 k-child folder page from DB < 300 ms; initial index of 50 k items never blocks the UI; memory < 400 MB steady state on the 100 k-node synthetic set; WebView2 init is async and non-blocking.
- **Live-check tool:** `{APP_NAME}.Tools live-check` (via `build\live-check.cmd`): after the owner configures OAuth, performs a real end-to-end smoke test (auth → create test folder → upload → list → change feed → rename → trash → cleanup) against a *dedicated test folder only*, prints a pass/fail table. Because you cannot run this yourself without credentials, this tool is how the owner verifies the Google integration in minutes.

---

## 20. Testing & quality gates

| Area | Requirement |
|---|---|
| Domain / Application / Analysis | unit tests, **≥ 85 %** line coverage |
| Analysis detectors | positive **and** negative fixture tree for every detector and role rule |
| Name sanitization & path safety | adversarial corpus tests |
| Persistence | migrations, indexes, FTS, rebuild-from-Drive round-trip test |
| Sync | deterministic fake provider with **injected faults**: network drop mid-upload, 429/5xx, token expiry, concurrent remote edit, remote move/delete races, crash-and-replay (**≥ 80 %**) |
| Providers | shared contract tests against LocalFolder; GoogleDrive via fake HTTP asserting exact calls/`fields`/scopes |
| Bridge | in-process end-to-end tests (no WebView2) exercising **every** method at least once, incl. error codes, cancellation, idempotency, confirmation tokens |
| Architecture | NetArchTest rules for Section 6.2 |
| UI core | Vitest, **≥ 80 %** coverage with mock bridge |
| UI views | Testing Library smoke tests: each view renders in every state; required `data-testid`s present; no-design lint (Section 16.3) |
| i18n | missing-key and unused-key tests |
| Security | markdown sanitization tests, SVG-as-image test, origin-check test, redaction tests |
| Performance | synthetic 100 k-node harness |
| Static | `dotnet build` zero warnings; `tsc --noEmit` strict; ESLint (incl. jsx-a11y, react-hooks) clean |

`build\verify.cmd` runs the whole gate in one command: restore → build (warnings as errors) → .NET tests (+coverage) → contract-check → `npm ci` → typecheck → lint → UI tests → UI build → prod-bundle fixture-leak check → dependency audits. **Exit non-zero on any failure.**

**Launch check:** if the environment can start GUI apps, launch the host (LocalFolder provider, temp data dir) for ≥ 10 s, confirm the bridge handshake in logs, then exit cleanly. If it cannot, **say so** in the report.

---

## 21. Packaging & publish-readiness (F-26)

- **`build\publish.cmd [version]`**: clean → `npm ci && npm run build` → `dotnet publish` (Release, `win-x64`, self-contained, ReadyToRun; **no trimming** for WPF; verify the single-file/native-library story with `WebView2Loader.dll` — if single-file misbehaves, publish as a folder and document) → copy UI/config → `dist\{APP_NAME}-{version}-win-x64\` → ZIP + `SHA256SUMS.txt`.
- **Versioning:** SemVer in `Directory.Build.props`; stamped into assembly, UI `appIdentity`, installer.
- **Manifest:** `longPathAware`, PerMonitorV2 DPI, supported OS Windows 10+; application icon is the owner's job — use a default and honor `assets/app.ico` if present (**do not create branding**).
- **Installer:** `installer\setup.iss` (Inno Setup, per-user, no admin): installs under `%LOCALAPPDATA%\Programs\{APP_NAME}`, optionally runs a WebView2 Evergreen bootstrapper if the file `installer\MicrosoftEdgeWebview2Setup.exe` is present, start-menu shortcut, uninstall asks whether to remove user data.
- **Signing:** `build\sign.cmd` signs when `SIGN_CERT_THUMBPRINT` (or a PFX env var) is set; otherwise it prints how. Never store certs in the repo.
- **`docs/PUBLISHING.md`:** release checklist; Google OAuth consent-screen/verification notes (`drive.file` is non-sensitive; `drive` is restricted and needs verification + security assessment — verify current rules), required homepage/privacy-policy URLs, branding fields, how to embed the production OAuth client at build time, SmartScreen reputation notes, update strategy (auto-update is **out of scope**; recommend options).
- **`THIRD_PARTY_NOTICES.md`:** generated from actual dependencies (.NET + npm) with licenses.

---

## 22. Documentation deliverables

`README.md` (prerequisites, build, run, first-run), `docs/ARCHITECTURE.md` (with Mermaid diagrams), `docs/DECISIONS.md`, `docs/BUILD_STATE.md`, `docs/MASTER_PROMPT.md`, `docs/BRIDGE_PROTOCOL.md` (generated + prose), `docs/UI_CONTRACT.md`, `docs/UI_REPLACEMENT_GUIDE.md`, `docs/GOOGLE_SETUP.md` (click-by-click: create Cloud project → enable Drive API → consent screen → scopes → OAuth client of type *Desktop app* → publishing status & the 7-day token caveat → import JSON into the app → run live-check), `docs/AI_SETUP.md`, `docs/RULES_AUTHORING.md` (how to edit `project-markers.json`, `file-roles.json`, add a detector, test it), `docs/PUBLISHING.md`, `docs/PRIVACY.md` (draft), `docs/LIVE_TESTING.md` (manual checklist), `docs/KNOWN_LIMITATIONS.md`, `CHANGELOG.md`.

Docs must be accurate to the code as shipped; stale docs fail the DoD.

---

## 23. Out of scope (do not build, do not stub)

Git protocol/branches/merges/pull requests · Shared Drives · sharing & permission management · real-time collaboration · editing Office documents inside the app · OCR · mobile/web clients · any server/back-end of our own · storage providers other than Google Drive and LocalFolder (the abstraction must make adding them possible) · auto-update · telemetry/analytics · accounts/billing/licensing · app icon, logo, branding, marketing site · **any visual design of the UI**.

---

## 24. Definition of Done

All must be true (tick them off in `docs/BUILD_STATE.md` with evidence):

- [ ] Every feature F-01…F-26 implemented and reachable from the reference UI; none stubbed.
- [ ] `build\verify.cmd` passes end-to-end with **zero warnings**, all tests green, coverage targets met.
- [ ] Architecture tests enforce Section 6.2 and pass.
- [ ] The Google Drive provider passes the provider contract tests via the fake HTTP layer; exact scopes/`fields` asserted.
- [ ] The LocalFolder provider passes the same contract tests; the full app runs end-to-end on it with no credentials.
- [ ] Rebuild-from-Drive test proves the local DB is disposable.
- [ ] Analysis detects, describes, and tags: a static site, a Next.js site, a Vite+React app, a WordPress tree, a .NET WPF solution, a Minecraft plugin, a Python project, and a mixed mono-repo — each with positive and negative fixtures.
- [ ] Outbox survives simulated crashes; conflicts never overwrite silently.
- [ ] Bridge end-to-end tests touch every method; contract files are generated and fresh.
- [ ] Reference UI contains **no design** (Section 16.3 lint passes); UI replaceable via `ui-override`, `--ui-dev-url`, and fixture bridge; production bundle has no fixture code.
- [ ] WebView2 hardening implemented and tested (origin check, navigation allowlist, permissions, downloads).
- [ ] i18n complete for `tr-TR` and `en-US`.
- [ ] Security checklist (Section 18) satisfied; audits clean or documented.
- [ ] `build\publish.cmd` produces a runnable distribution; installer script present; notices generated.
- [ ] All documents in Section 22 exist and are accurate.
- [ ] Launch check performed, **or** its absence stated honestly in the report.
- [ ] `git` repo (local) contains a clean, logically-structured history ending with the working state; nothing pushed anywhere.

---

## 25. Final report (your only closing chat message)

Keep it factual and under ~80 lines, in this shape:

1. **Result:** one paragraph — what exists and how to run it (exact commands).
2. **Feature matrix:** F-01…F-26 → `done` / `partial (why)` / `not done (why)`.
3. **Verification evidence:** the real commands you ran and their outcomes (tests passed/total, coverage numbers, audit results, launch check yes/no).
4. **Not verified:** everything you could not verify (notably live Google Drive, real AI calls, installer compilation, signing).
5. **Owner to-do list:** (a) follow `docs/GOOGLE_SETUP.md` and run `build\live-check.cmd`; (b) optional AI key + consent; (c) supply `assets/app.ico`; (d) choose a license; (e) code-signing certificate; (f) hand `docs/UI_REPLACEMENT_GUIDE.md` to the UI designer.
6. **Key decisions & risks:** top items from `docs/DECISIONS.md`.

No marketing language. No claims you cannot back with evidence from the run.

---

## Appendix A — Kind taxonomy (stable ids; extend, never repurpose)

`source-code`, `web-page`, `stylesheet`, `client-script`, `markup`, `config`, `data-structured`, `data-tabular`, `database`, `notebook`, `document-text`, `document-rich`, `spreadsheet`, `presentation`, `ebook`, `pdf`, `image-raster`, `image-vector`, `design-source`, `audio`, `video`, `font`, `archive`, `executable`, `library-binary`, `installer`, `disk-image`, `model-3d`, `game-asset`, `minecraft-asset`, `log`, `certificate-or-key`, `shortcut`, `google-doc`, `google-sheet`, `google-slides`, `google-other`, `folder`, `unknown`.

## Appendix B — Starter project-marker catalog (ship as `config/project-markers.json`; extensible)

| Project type id | Typical markers (any weighted combination) | Tags applied |
|---|---|---|
| `web-static` | `index.html` at root + `*.css`/`*.js`/`assets/` | `type:web-site`, `tech:html`, `tech:css` |
| `web-nextjs` | `next.config.*`, `package.json` dep `next` | `type:web-site`, `tech:nextjs`, `tech:react` |
| `web-nuxt` | `nuxt.config.*`, dep `nuxt` | `type:web-site`, `tech:nuxt`, `tech:vue` |
| `web-astro` | `astro.config.*`, dep `astro` | `type:web-site`, `tech:astro` |
| `web-gatsby` | `gatsby-config.*` | `type:web-site`, `tech:gatsby`, `tech:react` |
| `web-remix` | `remix.config.*` or dep `@remix-run/*` | `type:web-site`, `tech:remix`, `tech:react` |
| `web-sveltekit` | `svelte.config.*` + dep `@sveltejs/kit` | `type:web-site`, `tech:sveltekit`, `tech:svelte` |
| `web-docusaurus` | `docusaurus.config.*` | `type:web-site`, `tech:docusaurus`, `type:docs-site` |
| `web-eleventy` | `.eleventy.js`/`eleventy.config.*` | `type:web-site`, `tech:eleventy` |
| `web-hugo` | `hugo.toml`/`config.toml` + `content/` | `type:web-site`, `tech:hugo` |
| `web-jekyll` | `_config.yml` + `_posts/` or `_layouts/` | `type:web-site`, `tech:jekyll` |
| `web-spa-vite` | `vite.config.*` + `index.html` + dep `react`/`vue`/`svelte` | `type:web-site`, `tech:vite`, `tech:<framework>` |
| `web-angular` | `angular.json` | `type:web-site`, `tech:angular` |
| `web-wordpress` | `wp-config.php` or `wp-content/` | `type:web-site`, `tech:wordpress`, `tech:php` |
| `web-php` | `index.php` + `composer.json` | `type:web-site`, `tech:php` |
| `dotnet-solution` | `*.sln`, `*.csproj` (+ `UseWPF`/`UseWindowsForms`/`Microsoft.NET.Sdk.Web`) | `type:dotnet`, `tech:csharp`, `tech:wpf`/`tech:aspnet` |
| `node-package` | `package.json` without web markers | `type:node-package`, `tech:nodejs` |
| `python-project` | `pyproject.toml`/`requirements.txt`/`setup.py` | `type:python`, `tech:python` |
| `java-maven` / `java-gradle` | `pom.xml` / `build.gradle(.kts)` | `type:java`, `tech:java` |
| `rust-crate` | `Cargo.toml` | `type:rust`, `tech:rust` |
| `go-module` | `go.mod` | `type:go`, `tech:go` |
| `minecraft-plugin` | `plugin.yml`, `paper-plugin.yml`, `bungee.yml`, `velocity-plugin.json`, or deps on `paper-api`/`spigot-api` | `type:minecraft-plugin`, `tech:java` |
| `minecraft-mod` | `fabric.mod.json`, `META-INF/mods.toml`, `neoforge.mods.toml` | `type:minecraft-mod` |
| `unity` | `ProjectSettings/ProjectVersion.txt` + `Assets/` | `type:game`, `tech:unity` |
| `unreal` | `*.uproject` | `type:game`, `tech:unreal` |
| `godot` | `project.godot` | `type:game`, `tech:godot` |
| `android-app` | `AndroidManifest.xml` + gradle | `type:mobile-app`, `tech:android` |
| `ios-app` | `*.xcodeproj`/`*.xcworkspace` | `type:mobile-app`, `tech:ios` |
| `data-notebooks` | many `*.ipynb` | `type:data-science` |
| `docs-collection` | mostly `*.md`/`*.docx`/`*.pdf`, no code markers | `type:documents` |
| `design-assets` | `*.psd`/`*.fig`/`*.ai`/`*.xd`/`*.sketch` | `type:design` |
| `media-library` | mostly image/audio/video | `type:media` |
| **mono-roots** | `pnpm-workspace.yaml`, npm/yarn `workspaces`, `nx.json`, `turbo.json`, `lerna.json`, Cargo `[workspace]`, `go.work`, multi-project `.sln` | `type:monorepo` (+ children detected individually) |

## Appendix C — Bridge error codes

`UNKNOWN`, `INTERNAL`, `INVALID_PARAMS`, `NOT_FOUND`, `ALREADY_EXISTS`, `CONFLICT_VERSION`, `PERMISSION_DENIED`, `AUTH_REQUIRED`, `OAUTH_NOT_CONFIGURED`, `REAUTH_REQUIRED`, `OFFLINE`, `RATE_LIMITED`, `QUOTA_EXCEEDED`, `TOO_LARGE_TO_EDIT`, `UNSUPPORTED_OPERATION`, `CANCELLED`, `CONFIRMATION_REQUIRED`, `PROVIDER_ERROR`, `INTEGRITY_ERROR`, `PATH_REJECTED`, `SENSITIVE_CONTENT_BLOCKED`, `AI_NOT_CONFIGURED`, `AI_CONSENT_REQUIRED`, `AI_BUDGET_EXCEEDED`, `UI_INCOMPATIBLE`.

## Appendix D — One-line mental model

> **Drive holds the truth. SQLite is a fast, disposable mirror. The Outbox makes every edit safe. The Analysis engine makes every file self-explanatory. The Bridge is the only door. The UI is a swappable skin — and you are not the one who designs it.**

**Begin now. Do not ask questions. Do not stop until Section 24 is satisfied.**