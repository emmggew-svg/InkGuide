// DEV ONLY — fixture bridge for designer workflow (no host required).
// This file must never ship in production bundles (see vite.config.ts prod
// alias to fixtureBridge.prod-stub.ts and fixture-leak test).
// Select via ?bridge=fixture in dev. All payloads mirror the REAL generated
// bridge DTOs (contract.ts) so fixture mode matches production behavior.

import type { BridgeError, ErrorCode, MethodName } from "@core/contract";
import type {
  AccountDto,
  ActivityDto,
  ConflictDto,
  NodeDto,
  OutboxDto,
  ProjectDto,
  RepoDto,
  SearchHitDto,
  SnapshotDto,
  TagDto,
  TransferDto,
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

const accounts: AccountDto[] = [
  { id: "acc-1", email: "owner@example.com", displayName: "Owner", isActive: true, reauthRequired: false, scopeMode: "AppFolderOnly" },
];

const repos: RepoDto[] = [
  { id: "repo-1", name: "website", kind: "single" },
  { id: "repo-2", name: "monorepo", kind: "mono" },
];

const nodes: NodeDto[] = [
  { id: "n-root", parentId: null, name: "website", isFolder: true, mimeType: "application/vnd.inkguide.folder", size: 0, md5: null, version: "1", modifiedTime: "2026-09-01T10:00:00Z", trashed: false, path: "/website", description: null, previewUrl: null, downloadUrl: null },
  { id: "n-1", parentId: "n-root", name: "index.html", isFolder: false, mimeType: "text/html", size: 1024, md5: "m1", version: "1", modifiedTime: "2026-09-02T10:00:00Z", trashed: false, path: "/website/index.html", description: "Homepage.", previewUrl: null, downloadUrl: null },
  { id: "n-2", parentId: "n-root", name: "app.ts", isFolder: false, mimeType: "text/typescript", size: 2048, md5: "m2", version: "1", modifiedTime: "2026-09-03T10:00:00Z", trashed: false, path: "/website/app.ts", description: null, previewUrl: null, downloadUrl: null },
  { id: "n-3", parentId: "n-root", name: "assets", isFolder: true, mimeType: "application/vnd.inkguide.folder", size: 0, md5: null, version: "1", modifiedTime: "2026-09-04T10:00:00Z", trashed: false, path: "/website/assets", description: null, previewUrl: null, downloadUrl: null },
];

const tags: TagDto[] = [
  { id: "t-1", name: "type:web-site", namespace: "type", description: null, origin: "auto", colorToken: null, isSystem: false, usageCount: 1 },
  { id: "t-2", name: "tech:react", namespace: "tech", description: null, origin: "auto", colorToken: null, isSystem: false, usageCount: 0 },
];

const snapshots: SnapshotDto[] = [
  { id: "s-1", repositoryId: "repo-1", projectId: null, message: "initial", mode: "pin", createdAt: "2026-09-05T10:00:00Z" },
];

const activity: ActivityDto[] = [
  { id: 1, kind: "nodes.createFile", nodeId: "n-1", summary: "created index.html", reversible: true, undone: false, occurredAt: "2026-09-06T10:00:00Z" },
];

const transfers: TransferDto[] = [
  { id: "x-1", direction: "Upload", nodeId: "n-2", fileName: "app.ts", totalBytes: 2048, transferredBytes: 2048, state: "Done", error: null },
];

const conflicts: ConflictDto[] = [];
const outbox: OutboxDto[] = [];
const projects: ProjectDto[] = [
  { id: "p-1", repositoryId: "repo-2", path: "apps/web", name: "web", projectType: "web-nextjs", tags: ["type:web-site"], detected: true, confirmed: true },
];
const results: SearchHitDto[] = [
  { nodeId: "n-1", name: "index.html", path: "/website/index.html", isFolder: false, highlight: "homepage" },
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
    return Promise.resolve(accounts as unknown as T);
  }
  if (method === "accounts.getOAuthStatus") {
    return Promise.resolve({ configured: true, source: "fixture" } as unknown as T);
  }
  if (method === "repos.list") {
    return Promise.resolve(repos as unknown as T);
  }
  if (method === "repos.getOverview") {
    const id = paramId(p, "repo-1");
    const found = repos.find((r) => r.id === id) ?? repos[0];
    if (!found) {
      return Promise.reject(err("NOT_FOUND", "repo not found"));
    }
    return Promise.resolve({
      id: found.id,
      totalBytes: 123456,
      fileCount: 42,
      folderCount: 3,
      bytesByKind: { "source-code": 20 },
      projects,
      readmeExcerpt: "# fixture readme",
    } as unknown as T);
  }
  if (method === "nodes.list") {
    const rawParent = p["parentId"];
    const parentId: string | null = typeof rawParent === "string" ? rawParent : null;
    const items = nodes.filter((n) => n.parentId === parentId);
    return Promise.resolve({ items, nextCursor: null } as unknown as T);
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
    return Promise.resolve({ text: "<h1>hello</h1>", encoding: "utf-8", eol: "\n", bom: false, version: "v1" } as unknown as T);
  }
  if (method === "nodes.writeText") {
    return Promise.resolve({ text: "<h1>hello</h1>", encoding: "utf-8", eol: "\n", bom: false, version: "v2" } as unknown as T);
  }
  if (method === "nodes.getPreview") {
    return Promise.resolve({ kind: "text", previewUrl: null, htmlSnippet: null, tablePreview: null, metadataJson: "{\"text\":\"hello\"}" } as unknown as T);
  }
  if (method === "tags.list") {
    return Promise.resolve(tags as unknown as T);
  }
  if (method === "tags.listSuggestions") {
    return Promise.resolve([] as unknown as T);
  }
  if (method === "search.query") {
    return Promise.resolve({ items: results, nextCursor: null } as unknown as T);
  }
  if (method === "search.suggest") {
    return Promise.resolve({ suggestions: ["index.html"] } as unknown as T);
  }
  if (method === "transfers.list") {
    return Promise.resolve(transfers as unknown as T);
  }
  if (method === "sync.getStatus") {
    return Promise.resolve({ state: "idle", lastError: null, lastSyncAt: null } as unknown as T);
  }
  if (method === "sync.listOutbox") {
    return Promise.resolve(outbox as unknown as T);
  }
  if (method === "sync.listConflicts") {
    return Promise.resolve(conflicts as unknown as T);
  }
  if (method === "snapshots.list") {
    return Promise.resolve(snapshots as unknown as T);
  }
  if (method === "snapshots.diff") {
    return Promise.resolve({ added: ["a"], removed: [], modified: [], moved: [] } as unknown as T);
  }
  if (method === "activity.list") {
    return Promise.resolve({ items: activity, nextCursor: null } as unknown as T);
  }
  if (method === "organize.listInbox") {
    return Promise.resolve({ items: [], nextCursor: null } as unknown as T);
  }
  if (method === "projects.list") {
    return Promise.resolve(projects as unknown as T);
  }
  if (method === "duplicates.list") {
    return Promise.resolve([] as unknown as T);
  }
  if (method === "nodes.listTrash") {
    return Promise.resolve({ items: [], nextCursor: null } as unknown as T);
  }
  if (method === "settings.get") {
    return Promise.resolve({ values: { language: "tr-TR" }, schemaVersion: 1 } as unknown as T);
  }
  if (method === "ai.getStatus") {
    return Promise.resolve({ status: "notConfigured", usedToday: 0, budgetPerDay: 500, model: "claude-haiku-5-5" } as unknown as T);
  }
  if (method === "diagnostics.getHealth") {
    return Promise.resolve({ state: "healthy", checks: { database: "ok", provider: "ok" } } as unknown as T);
  }
  if (method === "insight.get") {
    return Promise.resolve({ nodeId: "n-1", kind: "web-page", role: "entry-point", summary: "Homepage.", confidence: 0.9, source: "rule", sensitive: false, analyzedAt: "2026-09-06T10:00:00Z" } as unknown as T);
  }
  return Promise.resolve({} as unknown as T);
}
