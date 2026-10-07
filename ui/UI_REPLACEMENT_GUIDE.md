# UI_REPLACEMENT_GUIDE

How a designer or agent builds a new UI against the contract and installs it.

## 1. Contract source
- `src/core/contract.ts` is GENERATED (stub now, overwritten by contract-gen).
  Preserve exports CONTRACT_MAJOR/MINOR, ErrorCode, MethodName, BRIDGE_METHODS.
- `npm run contract:check` fails if `ui-manifest.json` contract != contract.ts version.

## 2. Develop with the fixture bridge (DEV ONLY)
- `npm run dev` then open `http://localhost:5173/?bridge=fixture`.
- In-memory data in `src/core/fixtureBridge.ts` (marked DEV ONLY).
- Production builds alias `@core/fixtureBridge` to `fixtureBridge.prod-stub.ts`
  (see vite.config.ts) so no fixture strings ship. Verified by contract.test.ts.
- Host integration: `--ui-dev-url http://localhost:5173` points WebView2 at Vite.

## 3. Build rules (no-design contract)
- Vite base './' relative, no CDN/fonts/scripts, offline.
- CSP meta in index.html: script-src self; style-src self unsafe-inline ONLY for
  CodeMirror dynamic styles (documented); connect-src none; object-src none.
- theme/tokens.css: every visual value as CSS var, neutral placeholder, header
  PLACEHOLDER-replaced-by-designer. theme/structure.css: layout only.
- No inline style=, no color/font literals outside tokens.css, no UI kits
  (enforced by noDesign.test.ts + eslint).

## 4. Install via ui-override (no recompilation)
- If `%LOCALAPPDATA%/InkGuide/ui-override/` contains index.html + ui-manifest.json
  with compatible contract (same major, host minor >= UI minor), host serves it.
- Schema (`ui-manifest.json`): { name, version, contract "1.x", entry, minHostVersion, features[] }.
- Incompatible/invalid falls back to bundled UI; reason via app.getUiStatus.

## 5. Checklist for replacement UI
1. Implement BridgeClient envelope (v/id/type/method/params/result/error).
2. app.hello handshake + version check + reconnect.
3. Cover all UI_CONTRACT views + states + data-testid/data-action hooks.
4. tr-TR + en-US i18n complete (missing/unused-key tests).
5. `npm run typecheck && npm run lint && npm run test && npm run build`.
