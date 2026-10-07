# UI_CONTRACT (reference UI)

For every view: purpose, bridge calls, store slices, events, states,
destructive confirmations, shortcuts. Wireframe-grade, unstyled.

## Shell (src/app/App.tsx)
- Purpose: nav + main wiring only.
- Stores: nav.route.
- States: always ready; each child view handles its own states.

## 1. Onboarding (OnboardingView, prefix onboarding)
- Bridge: accounts.list, accounts.connect, accounts.disconnect, accounts.importOAuthClient, accounts.getOAuthStatus.
- Stores: accounts.
- Events: account.stateChanged.
- States: all via ViewState; OAUTH_NOT_CONFIGURED shows import control.
- Destructive: disconnect requires host confirmation token flow.
- Shortcut: none.

## 2. Repo list (RepoListView, prefix repos)
- Bridge: repos.list, repos.create.
- Stores: repos.
- States: all; empty uses repos.empty.

## 3. Repo overview (RepoOverviewView, prefix overview)
- Bridge: repos.getOverview, repos.get.
- Stores: repos.overview.
- Shows stats/readme/projects sections.

## 4. Browser (BrowserView, prefix browser)
- Bridge: nodes.list, nodes.createFolder, nodes.move, transfers.uploadPaths.
- Stores: tree, listing, selection.
- Features: tree + list, breadcrumb nav, sort/filter, multiselect checkboxes, drop upload via file input.
- Events: node.changed.

## 5. Inspector (InspectorView, prefix inspector)
- Bridge: insight.get, insight.setSummary, nodes.revisions, nodes.openExternal, nodes.openInBrowser.
- Stores: tree (selected = first node), tags.
- Sections: insight/tags/description/revisions/details.

## 6. Editor (EditorView, prefix editor)
- Bridge: nodes.readText, nodes.writeText (baseVersion, conflict-safe).
- Stores: editor (nodeId/text/version/dirty).
- CodeMirror 6 via CSS vars only (.editor__cm). Ctrl+S saves. TOO_LARGE_TO_EDIT state.
- Shortcut: Ctrl+S (editor-save).

## 7. Previewer (PreviewerView, prefix previewer)
- Bridge: nodes.getPreview (returns cache URLs, never bytes).
- Kinds: text, markdown (marked+DOMPurify), image (img cache URL, SVG as img only), pdf (cache URL), audio/video (cache URL), csv (table), json, archive (listing), html (sandboxed iframe without allow-same-origin+allow-scripts), other fallback.
- CSP: frame-src self; usercontent isolated host in production.

## 8. Search (SearchView, prefix search)
- Bridge: search.query, search.suggest.
- Stores: search.
- Shortcut: Ctrl+F (search-focus).

## 9. Tags (TagsView, prefix tags)
- Bridge: tags.* (list/create/assign/listSuggestions/acceptSuggestion/dismissSuggestion).
- Stores: tags.

## 10. Inbox & Organizer (InboxOrganizerView, prefix inbox)
- Bridge: organize.listInbox/suggest/apply.
- Stores: organizer.

## 11. Duplicates (DuplicatesView, prefix duplicates)
- Bridge: duplicates.scan/list/resolve.
- Stores: duplicates. Never automatic (manual resolve only).

## 12. Snapshots (SnapshotsView, prefix snapshots)
- Bridge: snapshots.create/list/diff/textDiff/restore/delete.
- Stores: snapshots.

## 13. Activity (ActivityView, prefix activity)
- Bridge: activity.list/undo.
- Stores: activity. Undo only for reversible ops.
- Shortcut: Ctrl+Z (activity-undo).

## 14. Sync (SyncView, prefixes sync/conflicts)
- Bridge: sync.getStatus/syncNow/pause/resume/listOutbox/retryOutbox/discardOutbox/listConflicts/resolveConflict.
- Stores: sync, conflicts.
- Events: sync.stateChanged, sync.progress, conflict.created.
- Shortcut: Ctrl+R (sync-now).

## 15. Transfers (TransfersView, prefix transfers)
- Bridge: transfers.uploadPaths/download/list/pause/resume/cancel/retry/clearFinished.
- Stores: transfers.
- Events: transfer.progress.

## 16. Trash (TrashView, prefix trash)
- Bridge: nodes.listTrash/restore/emptyTrash/deletePermanent (guarded confirmation token).
- Stores: trash.

## 17. Settings (SettingsView, prefix settings)
- Bridge: settings.get/update/reset/getUiState/setUiState, storage.*, rules.*, ai.*.
- Stores: settings, accounts, ai.
- Groups: general/accounts/storage/sync/analysis/AI/advanced.

## 18. About & diagnostics (AboutDiagnosticsView, prefix about)
- Bridge: diagnostics.getHealth/exportBundle/log, app.getInfo/getUiStatus.
- Stores: settings (outer ViewState).
- Export log bundle button.

All views: loading/empty/error-by-code/offline/reauth/not-configured/denied via ViewState (data-testid prefix-state). Interactive elements carry accessible name + data-testid + data-action. Class names structural only.
