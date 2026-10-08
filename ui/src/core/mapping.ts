// Bridge DTO -> UI view-model mapping (single place).
// The bridge returns bare JSON arrays for list methods and PagedResult
// envelopes ({items, nextCursor}) for paged ones; view-models predate the
// generated contract, so every loader normalizes through here. Both shapes
// are accepted so older mocks keep working.

import type {
  AccountDto,
  ActivityDto,
  ConflictDto,
  DuplicateGroupDto,
  NodeDto,
  OrganizeSuggestionDto,
  OutboxDto,
  ProjectDto,
  RepoDto,
  RepoOverviewDto,
  SearchHitDto,
  SnapshotDto,
  TagDto,
  TransferDto,
} from "@core/contract";
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

/** Accepts a bare array or an {items} envelope; anything else becomes []. */
export function toItems<T>(res: unknown): T[] {
  if (Array.isArray(res)) {
    return res as T[];
  }
  if (typeof res === "object" && res !== null && Array.isArray((res as { items?: unknown }).items)) {
    return (res as { items: T[] }).items;
  }
  return [];
}

export function mapAccount(a: AccountDto): AccountInfo {
  return {
    id: a.id,
    email: a.email,
    state: a.reauthRequired ? "reauthRequired" : "connected",
  };
}

export function mapRepo(r: RepoDto): RepoInfo {
  return { id: r.id, name: r.name, kind: r.kind === "mono" ? "mono" : "single" };
}

export function mapNode(n: NodeDto): NodeItem {
  return {
    id: n.id,
    parentId: n.parentId ?? null,
    name: n.name,
    isFolder: n.isFolder,
    mime: n.mimeType ?? "",
    size: n.size,
    modifiedTime: n.modifiedTime,
    trashed: n.trashed,
  };
}

export function mapTag(t: TagDto): TagInfo {
  return { id: t.id, name: t.name, origin: t.origin === "auto" ? "auto" : "user" };
}

export function mapTransfer(t: TransferDto): TransferJob {
  const total = t.totalBytes > 0 ? t.totalBytes : 0;
  const done = t.transferredBytes;
  const s = t.state.toLowerCase();
  const state: TransferJob["state"] =
    s === "queued" || s === "running" || s === "paused" || s === "done" || s === "failed" || s === "cancelled"
      ? s
      : "queued";
  return {
    id: t.id,
    name: t.fileName,
    state,
    progress: total > 0 ? Math.min(1, done / total) : state === "done" ? 1 : 0,
  };
}

export function mapConflict(c: ConflictDto): ConflictInfo {
  return {
    id: c.id,
    nodeId: c.nodeId,
    reason: c.conflictCopyPath != null && c.conflictCopyPath !== "" ? `${c.kind} (copy kept)` : c.kind,
  };
}

export function mapOutbox(o: OutboxDto): OutboxEntry {
  return { id: o.opId, type: o.operationType, state: o.state };
}

export function mapSearchHit(h: SearchHitDto): SearchResult {
  return { nodeId: h.nodeId, name: h.name, snippet: h.highlight ?? "" };
}

export function mapSnapshot(s: SnapshotDto): SnapshotInfo {
  return { id: s.id, message: s.message, createdAt: s.createdAt };
}

export function mapProject(p: ProjectDto): ProjectInfo {
  return { id: p.id, path: p.path, type: p.projectType, tags: [...p.tags], confirmed: p.confirmed };
}

export function mapActivity(a: ActivityDto): ActivityEntry {
  return {
    id: String(a.id),
    when: a.occurredAt,
    what: a.summary,
    undoable: a.reversible && !a.undone,
  };
}

export function mapRepoOverview(o: RepoOverviewDto, fallbackName: string): RepoOverview {
  const projects: ProjectInfo[] = Array.isArray(o.projects) ? o.projects.map(mapProject) : [];
  const byKind: Record<string, number> = {};
  if (o.bytesByKind != null && typeof o.bytesByKind === "object") {
    for (const [k, v] of Object.entries(o.bytesByKind)) {
      byKind[k] = typeof v === "number" ? v : 0;
    }
  }
  return {
    id: o.id,
    name: fallbackName,
    totalFiles: o.fileCount,
    totalBytes: o.totalBytes,
    readmeExcerpt: o.readmeExcerpt ?? "",
    projects,
    byKind,
  };
}

export function mapSuggestion(s: OrganizeSuggestionDto): { nodeId: string; dest: string; reason: string } {
  const reasons = Array.isArray(s.reasons) ? s.reasons : [];
  return { nodeId: s.nodeId, dest: s.destinationId, reason: reasons.join("; ") };
}

export function mapDuplicateGroup(g: DuplicateGroupDto): { hash: string; nodeIds: string[] } {
  const nodes = Array.isArray(g.nodes) ? g.nodes : [];
  return { hash: g.md5, nodeIds: nodes.map((n) => n.id) };
}
