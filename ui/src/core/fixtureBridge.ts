// DEV ONLY — fixture bridge for designer workflow (no host required).
// This file must never ship in production bundles (see vite.config.ts prod
// alias to fixtureBridge.prod-stub.ts and fixture-leak test).
// Select via ?bridge=fixture in dev.

import type { BridgeError, ErrorCode, MethodName } from "@core/contract";
import type {
  AccountInfo,
  ActivityEntry,
  ConflictInfo,
  NodeItem,
  OutboxEntry,
  ProjectInfo,
  RepoInfo,
  RepoOverview,
  SearchResult,
  SnapshotInfo,
  TagInfo,
  TransferJob,
} from "@core/contract";

export const FIXTURE_DEV_ONLY_MARKER = "FIXTURE_DEV_ONLY_MARKER";

type CodedError = BridgeError & Error;

function err(code: ErrorCode, message: string): CodedError {
  const e = new Error(message) as CodedError;
  e.code = code;
  e.message = message;
  e.retryable = false;
  return e;
}

const accounts: AccountInfo[] = [
  { id: "acc-1", email: "owner@example.com", state: "connected" },
];

const repos: RepoInfo[] = [
  { id: "repo-1", name: "website", kind: "single" },
  { id: "repo-2", name: "monorepo", kind: "mono" },
];

const nodes: NodeItem[] = [
  { id: "n-root", parentId: null, name: "website", isFolder: true, mime: "folder", size: 0, modifiedTime: "2026-09-01T10:00:00Z", trashed: false },
  { id: "n-1", parentId: "n-root", name: "index.html", isFolder: false, mime: "text/html", size: 1024, modifiedTime: "2026-09-02T10:00:00Z", trashed: false },
  { id: "n-2", parentId: "n-root", name: "app.ts", isFolder: false, mime: "text/typescript", size: 2048, modifiedTime: "2026-09-03T10:00:00Z", trashed: false },
  { id: "n-3", parentId: "n-root", name: "assets", isFolder: true, mime: "folder", size: 0, modifiedTime: "2026-09-04T10:00:00Z", trashed: false },
];

const tags: TagInfo[] = [
  { id: "t-1", name: "type:web-site", origin: "auto" },
  { id: "t-2", name: "tech:react", origin: "auto" },
];

const snapshots: SnapshotInfo[] = [
  { id: "s-1", message: "initial", createdAt: "2026-09-05T10:00:00Z" },
];

const activity: ActivityEntry[] = [
  { id: "a-1", when: "2026-09-06T10:00:00Z", what: "created index.html", undoable: true },
];

const transfers: TransferJob[] = [
  { id: "x-1", name: "app.ts", state: "done", progress: 1 },
];

const conflicts: ConflictInfo[] = [];
const outbox: OutboxEntry[] = [];
const projects: ProjectInfo[] = [
  { id: "p-1", path: "apps/web", type: "web-nextjs", tags: ["type:web-site"], confirmed: true },
];
const results: SearchResult[] = [
  { nodeId: "n-1", name: "index.html", snippet: "homepage" },
];

function paramId(p: Record<string, unknown>, fallback: string): string {
  const raw = p["id"];
  return typeof raw === "string" ? raw : fallback;
}

// If-chain (not switch) returning canned results per method family.
export function fixtureInvoke<T>(method: MethodName, params?: unknown): Promise<T> {
  const p = (params ?? {}) as Record<string, unknown>;
  if (method === "app.hello") {
    return Promise.resolve({ contract: { major: 1, minor: 0 }, hostVersion: "0.1.0-fixture", capabilities: [], features: {}, settingsSnapshot: {}, uiStatus: "ok" } as unknown as T);
  }
  if (method === "accounts.list") {
    return Promise.resolve({ items: accounts } as unknown as T);
  }
  if (method === "accounts.getOAuthStatus") {
    return Promise.resolve({ configured: true } as unknown as T);
  }
  if (method === "repos.list") {
    return Promise.resolve({ items: repos } as unknown as T);
  }
  if (method === "repos.getOverview") {
    const id = paramId(p, "repo-1");
    const found = repos.find((r) => r.id === id) ?? repos[0];
    if (!found) {
      return Promise.reject(err("NOT_FOUND", "repo not found"));
    }
    const overview: RepoOverview = {
      id: found.id,
      name: found.name,
      totalFiles: 42,
      totalBytes: 123456,
      readmeExcerpt: "# fixture readme",
      projects,
      byKind: { "source-code": 20, image: 5 },
    };
    return Promise.resolve(overview as unknown as T);
  }
  if (method === "nodes.list") {
    const rawParent = p["parentId"];
    const parentId: string | null = typeof rawParent === "string" ? rawParent : null;
    const items = nodes.filter((n) => n.parentId === parentId);
    return Promise.resolve({ items, cursor: null } as unknown as T);
  }
  if (method === "nodes.get") {
    const id = paramId(p, "");
    const n = nodes.find((x) => x.id === id);
    if (!n) {
      return Promise.reject(err("NOT_FOUND", "node not found"));
    }
    return Promise.resolve(n as unknown as T);
  }
  if (method === "nodes.readText") {
    return Promise.resolve({ text: "<h1>hello</h1>", encoding: "utf-8", eol: "lf", bom: false, version: "v1" } as unknown as T);
  }
  if (method === "nodes.writeText") {
    return Promise.resolve({} as unknown as T);
  }
  if (method === "nodes.getPreview") {
    return Promise.resolve({ kind: "text", cacheUrl: null, text: "hello", rows: null, json: null, entries: null } as unknown as T);
  }
  if (method === "tags.list") {
    return Promise.resolve({ items: tags } as unknown as T);
  }
  if (method === "tags.listSuggestions") {
    return Promise.resolve({ items: [] } as unknown as T);
  }
  if (method === "search.query") {
    return Promise.resolve({ items: results } as unknown as T);
  }
  if (method === "search.suggest") {
    return Promise.resolve({ items: ["index.html"] } as unknown as T);
  }
  if (method === "transfers.list") {
    return Promise.resolve({ items: transfers } as unknown as T);
  }
  if (method === "sync.getStatus") {
    return Promise.resolve({ state: "idle" } as unknown as T);
  }
  if (method === "sync.listOutbox") {
    return Promise.resolve({ items: outbox } as unknown as T);
  }
  if (method === "sync.listConflicts") {
    return Promise.resolve({ items: conflicts } as unknown as T);
  }
  if (method === "snapshots.list") {
    return Promise.resolve({ items: snapshots } as unknown as T);
  }
  if (method === "snapshots.diff") {
    return Promise.resolve({ added: ["a"], removed: [], modified: [], moved: [] } as unknown as T);
  }
  if (method === "activity.list") {
    return Promise.resolve({ items: activity } as unknown as T);
  }
  if (method === "organize.listInbox") {
    return Promise.resolve({ items: [] } as unknown as T);
  }
  if (method === "projects.list") {
    return Promise.resolve({ items: projects } as unknown as T);
  }
  if (method === "duplicates.list") {
    return Promise.resolve({ items: [] } as unknown as T);
  }
  if (method === "nodes.listTrash") {
    return Promise.resolve({ items: [] } as unknown as T);
  }
  if (method === "settings.get") {
    return Promise.resolve({ values: { language: "tr-TR" } } as unknown as T);
  }
  if (method === "ai.getStatus") {
    return Promise.resolve({ status: "notConfigured" } as unknown as T);
  }
  if (method === "diagnostics.getHealth") {
    return Promise.resolve({ ok: true, details: "fixture healthy" } as unknown as T);
  }
  if (method === "insight.get") {
    return Promise.resolve({ nodeId: "n-1", kind: "web-page", role: "entry-point", summary: "Homepage.", confidence: 0.9, sensitivity: "normal", source: "rule" } as unknown as T);
  }
  return Promise.resolve({} as unknown as T);
}
