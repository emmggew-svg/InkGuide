// Production stub for the fixture bridge. Replaces fixtureBridge.ts in
// production builds so no fixture strings leak into dist/.
import type { MethodName } from "@core/contract";

export const FIXTURE_DEV_ONLY_MARKER = "";

export function fixtureInvoke<T>(method: MethodName, params?: unknown): Promise<T> {
  const detail = params === undefined ? method : `${method} (params omitted)`;
  return Promise.reject(new Error(`fixture bridge unavailable in production: ${detail}`));
}
