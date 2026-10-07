# InkGuide — RULES_AUTHORING

> How to change what InkGuide detects and how it describes files — all data,
> no code. Shipped catalogs live in `config/`; your overrides live in
> `%LOCALAPPDATA%\InkGuide\rules\` and win on equal `id`.

## Files

| File | Purpose | Schema hint |
|---|---|---|
| `config/kind-map.json` | extension/MIME → kind id (Appendix A taxonomy) + Google-native MIME fallback | `{ version, map: {".ext": kind}, mimeFallback: {mime: kind} }` |
| `config/file-roles.json` | role rules: glob patterns + priority + flags + summary templates | `{ version, rulesetVersion, roles: [{ id, patterns[], priority, sensitive?, generated?, summaryTemplate }] }` |
| `config/project-markers.json` | project detectors + mono-root markers | `{ version, projects: [{ id, name, tags[], markers[{ path-glob, contains?, jsonPath? }], weight, excludes[] }], monoRoots: [...] }` |
| `config/ignore-patterns.json` | default import ignores (`node_modules`, `.git`, `bin`, …) | `{ version, patterns[] }` |

`rulesetVersion` (in `file-roles.json`) stamps every rule-sourced insight; when
it changes, only `rule`-sourced insights are lazily re-analyzed in background.
User-authored summaries are never overwritten (precedence user > ai > rule).

## Editing project detectors

1. Copy the shipped entry shape: `id` (stable, lowercase-hyphen), display
   `name`, `tags` to auto-apply (`type:*`, `tech:*` namespaces), `markers`
   (glob + optional `contains` content cue / `jsonPath` check), `weight`,
   `excludes`.
2. Scoring: each directory is scored from marker evidence (depth-limited,
   default 4). **Nearest root wins**, except recognized mono-roots (npm/pnpm
   workspaces, `nx.json`, `turbo.json`, `lerna.json`, multi-project `.sln`,
   …) which become Mono repos with individually detected child projects.
3. Tags auto-apply at confidence ≥ 0.6 (configurable); below that they surface
   as suggestions (`tags.listSuggestions` → accept/dismiss; dismissals are
   recorded as suppressions and never re-applied).
4. Place your file at `%LOCALAPPDATA%\InkGuide\rules\project-markers.json`
   containing only your entries — same schema, matched by `id`, yours wins.

## Editing file roles

Roles map glob patterns to a `role` id plus a `summaryTemplate` with
`{slots}` filled from inspected facts (title, line count, exports, …).
Higher `priority` wins on overlap. Mark secrets with `"sensitive": true`
(excluded from indexing, logs, AI) and build artifacts with
`"generated": true`. Templates are localizable data (`tr-TR`, `en-US`).

## Adding a detector (checklist)

1. Add the `project-markers.json` entry (shipped catalog or user override).
2. Add/extend `file-roles.json` entries for the stack's characteristic files.
3. Extend `kind-map.json` if new extensions appear.
4. Validate: `rules.validate` (bridge) checks JSON shape and glob sanity.
5. Reload live: `rules.reload` (no restart). `rules.getCatalogInfo` shows the
   active versions and which files won each `id`.
6. Test with `seed-fixtures` trees (`InkGuide.Tools seed-fixtures --out …`)
   plus your own positive/negative folders; the analysis workstream owns the
   fixture-test harness that consumes them.

## Guardrails

- Keep `appProperties` payloads tiny (Drive limits: ~124 bytes key+value,
  ~30 entries/file) — the app stores only compact codes there.
- Never put secrets, absolute paths, or account ids in rule files; they ship
  with the product and roam via Drive manifests.
