# InkGuide — UI_REPLACEMENT_GUIDE (stub)

> **STUB — owned by the UI workstream.** This file will give a designer/agent
> the step-by-step: build a new UI against `ui/src/core/contract.ts`, test it
> with the fixture bridge (`?bridge=fixture`, dev-only, tree-shaken from prod),
> validate the `ui-manifest.json` contract field, and install via
> `%LOCALAPPDATA%\InkGuide\ui-override\` or `--ui-dev-url`.
>
> Mechanisms already shipped in the host: `UiOverride` validation with
> compatible-contract check + fallback, `app.getUiStatus` reporting, dev-URL
> loading (Debug), `window.setBackground` flash avoidance. See
> `docs/ARCHITECTURE.md` and `docs/BRIDGE_PROTOCOL.md`.
