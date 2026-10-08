import { create } from "zustand";
import { getActiveClient } from "@core/bridge";
import { isErrorCode } from "@core/contract";
import {
  mapAccount,
  mapActivity,
  mapConflict,
  mapDuplicateGroup,
  mapNode,
  mapOutbox,
  mapProject,
  mapRepo,
  mapRepoOverview,
  mapSearchHit,
  mapSnapshot,
  mapTag,
  mapTransfer,
  toItems,
} from "@core/mapping";
import type {
  AccountDto,
  ActivityDto,
  ConflictDto,
  DuplicateGroupDto,
  NodeDto,
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
  BridgeError,
  ConflictInfo,
  ErrorCode,
  NodeItem,
  OutboxEntry,
  ProjectInfo,
  RepoInfo,
  RepoOverview,
  SearchResult,
  SnapshotDiff,
  SnapshotInfo,
  TagInfo,
  TransferJob,
} from "@core/contract";

export type StoreStatus =
  | "idle"
  | "loading"
  | "ready"
  | "empty"
  | "error"
  | "offline"
  | "reauth"
  | "not-configured"
  | "denied";

export function statusFromError(code: ErrorCode): StoreStatus {
  switch (code) {
    case "OFFLINE":
      return "offline";
    case "REAUTH_REQUIRED":
    case "AUTH_REQUIRED":
      return "reauth";
    case "OAUTH_NOT_CONFIGURED":
    case "AI_NOT_CONFIGURED":
    case "AI_CONSENT_REQUIRED":
      return "not-configured";
    case "PERMISSION_DENIED":
      return "denied";
    case "NOT_FOUND":
    case "UNKNOWN":
    case "INTERNAL":
    case "INVALID_PARAMS":
    case "ALREADY_EXISTS":
    case "CONFLICT_VERSION":
    case "RATE_LIMITED":
    case "QUOTA_EXCEEDED":
    case "TOO_LARGE_TO_EDIT":
    case "UNSUPPORTED_OPERATION":
    case "CANCELLED":
    case "CONFIRMATION_REQUIRED":
    case "PROVIDER_ERROR":
    case "INTEGRITY_ERROR":
    case "PATH_REJECTED":
    case "SENSITIVE_CONTENT_BLOCKED":
    case "AI_BUDGET_EXCEEDED":
    case "UI_INCOMPATIBLE":
      return "error";
  }
}

function toErrorCode(e: unknown): ErrorCode {
  const err = e as Partial<BridgeError> & { code?: string };
  if (typeof err.code === "string" && isErrorCode(err.code)) {
    return err.code;
  }
  return "UNKNOWN";
}

// --- nav / tree / selection / listing ---

interface NavState {
  route: string;
  status: StoreStatus;
  go: (route: string) => void;
  setStatus: (s: StoreStatus) => void;
}
export const useNavStore = create<NavState>((set) => ({
  route: "onboarding",
  status: "ready",
  go: (route: string) => {
    set({ route, status: "ready" });
  },
  setStatus: (status: StoreStatus) => {
    set({ status });
  },
}));

interface TreeState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  nodes: NodeItem[];
  expanded: Record<string, boolean>;
  setNodes: (n: NodeItem[]) => void;
  toggle: (id: string) => void;
  loadChildren: (parentId: string | null) => Promise<void>;
}
export const useTreeStore = create<TreeState>((set) => ({
  status: "idle",
  errorCode: null,
  nodes: [],
  expanded: {},
  setNodes: (nodes: NodeItem[]) => {
    set({ nodes, status: nodes.length === 0 ? "empty" : "ready", errorCode: null });
  },
  toggle: (id: string) => {
    set((s) => ({ expanded: { ...s.expanded, [id]: !(s.expanded[id] ?? false) } }));
  },
  loadChildren: async (parentId: string | null) => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("nodes.list", {
        parentId,
        limit: 200,
      });
      const nodes = toItems<NodeDto>(res).map(mapNode);
      set({
        nodes,
        status: nodes.length === 0 ? "empty" : "ready",
      });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

interface SelectionState {
  ids: string[];
  toggle: (id: string) => void;
  clear: () => void;
  selectAll: (ids: string[]) => void;
}
export const useSelectionStore = create<SelectionState>((set) => ({
  ids: [],
  toggle: (id: string) => {
    set((s) => ({
      ids: s.ids.includes(id) ? s.ids.filter((x) => x !== id) : [...s.ids, id],
    }));
  },
  clear: () => {
    set({ ids: [] });
  },
  selectAll: (ids: string[]) => {
    set({ ids });
  },
}));

interface ListingState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  items: NodeItem[];
  sortAsc: boolean;
  filter: string;
  setSort: (asc: boolean) => void;
  setFilter: (f: string) => void;
  setItems: (items: NodeItem[]) => void;
}
export const useListingStore = create<ListingState>((set) => ({
  status: "idle",
  errorCode: null,
  items: [],
  sortAsc: true,
  filter: "",
  setSort: (sortAsc: boolean) => {
    set({ sortAsc });
  },
  setFilter: (filter: string) => {
    set({ filter });
  },
  setItems: (items: NodeItem[]) => {
    set({ items, status: items.length === 0 ? "empty" : "ready", errorCode: null });
  },
}));

export function visibleListing(items: NodeItem[], filter: string, asc: boolean): NodeItem[] {
  const f = filter.trim().toLowerCase();
  const filtered =
    f === "" ? [...items] : items.filter((n) => n.name.toLowerCase().includes(f));
  filtered.sort((a, b) => (asc ? a.name.localeCompare(b.name) : b.name.localeCompare(a.name)));
  return filtered;
}

// --- repos ---

interface ReposState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  repos: RepoInfo[];
  overview: RepoOverview | null;
  load: () => Promise<void>;
  loadOverview: (id: string) => Promise<void>;
}
export const useReposStore = create<ReposState>((set, get) => ({
  status: "idle",
  errorCode: null,
  repos: [],
  overview: null,
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("repos.list", {});
      const repos = toItems<RepoDto>(res).map(mapRepo);
      set({ repos, status: repos.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
  loadOverview: async (id: string) => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<RepoOverviewDto>("repos.getOverview", { id });
      const known = get().repos.find((r) => r.id === id);
      set({ overview: mapRepoOverview(res, known?.name ?? id), status: "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- transfers ---

interface TransfersState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  jobs: TransferJob[];
  load: () => Promise<void>;
}
export const useTransfersStore = create<TransfersState>((set) => ({
  status: "idle",
  errorCode: null,
  jobs: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("transfers.list", {});
      const jobs = toItems<TransferDto>(res).map(mapTransfer);
      set({ jobs, status: jobs.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- sync / outbox / conflicts ---

interface SyncState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  syncState: string;
  outbox: OutboxEntry[];
  load: () => Promise<void>;
}
export const useSyncStore = create<SyncState>((set) => ({
  status: "idle",
  errorCode: null,
  syncState: "idle",
  outbox: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const s = await client.invoke<{ state: string }>("sync.getStatus", {});
      const o = await client.invoke<unknown>("sync.listOutbox", {});
      const outbox = toItems<OutboxDto>(o).map(mapOutbox);
      set({
        syncState: s.state,
        outbox,
        status: "ready",
      });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

interface ConflictsState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  conflicts: ConflictInfo[];
  load: () => Promise<void>;
}
export const useConflictsStore = create<ConflictsState>((set) => ({
  status: "idle",
  errorCode: null,
  conflicts: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("sync.listConflicts", {});
      const conflicts = toItems<ConflictDto>(res).map(mapConflict);
      set({ conflicts, status: conflicts.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- tags ---

interface TagsState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  tags: TagInfo[];
  suggestions: TagInfo[];
  load: () => Promise<void>;
}
export const useTagsStore = create<TagsState>((set) => ({
  status: "idle",
  errorCode: null,
  tags: [],
  suggestions: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const t = await client.invoke<unknown>("tags.list", {});
      const s = await client.invoke<unknown>("tags.listSuggestions", {});
      const tags = toItems<TagDto>(t).map(mapTag);
      const suggestions = toItems<TagDto>(s).map(mapTag);
      set({
        tags,
        suggestions,
        status: tags.length === 0 ? "empty" : "ready",
      });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- search ---

interface SearchState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  query: string;
  results: SearchResult[];
  setQuery: (q: string) => void;
  run: () => Promise<void>;
}
export const useSearchStore = create<SearchState>((set, get) => ({
  status: "idle",
  errorCode: null,
  query: "",
  results: [],
  setQuery: (query: string) => {
    set({ query });
  },
  run: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("search.query", {
        text: get().query,
      });
      const results = toItems<SearchHitDto>(res).map(mapSearchHit);
      set({ results, status: results.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- settings ---

interface SettingsState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  language: string;
  settings: Record<string, string>;
  load: () => Promise<void>;
  setLanguage: (l: string) => void;
}
export const useSettingsStore = create<SettingsState>((set) => ({
  status: "idle",
  errorCode: null,
  language: "tr-TR",
  settings: {},
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "ready", settings: {}, language: "tr-TR" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<{ values: Record<string, string> }>("settings.get", {});
      const language = res.values["language"] ?? "tr-TR";
      set({ settings: res.values, language, status: "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
  setLanguage: (language: string) => {
    set({ language });
  },
}));

// --- activity ---

interface ActivityState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  entries: ActivityEntry[];
  load: () => Promise<void>;
}
export const useActivityStore = create<ActivityState>((set) => ({
  status: "idle",
  errorCode: null,
  entries: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("activity.list", {});
      const entries = toItems<ActivityDto>(res).map(mapActivity);
      set({ entries, status: entries.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- editor ---

interface EditorState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  nodeId: string | null;
  text: string;
  dirty: boolean;
  version: string | null;
  open: (nodeId: string, text: string, version: string) => void;
  edit: (text: string) => void;
  save: () => Promise<void>;
  reload: () => Promise<void>;
}
export const useEditorStore = create<EditorState>((set, get) => ({
  status: "idle",
  errorCode: null,
  nodeId: null,
  text: "",
  dirty: false,
  version: null,
  open: (nodeId: string, text: string, version: string) => {
    set({ nodeId, text, version, dirty: false, status: "ready", errorCode: null });
  },
  edit: (text: string) => {
    set({ text, dirty: true });
  },
  save: async () => {
    const client = getActiveClient();
    const { nodeId, text, version } = get();
    if (!client || !nodeId || !version) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      await client.invoke("nodes.writeText", { id: nodeId, text, baseVersion: version });
      set({ dirty: false, status: "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
  reload: async () => {
    const client = getActiveClient();
    const { nodeId } = get();
    if (!client || !nodeId) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<{ text: string; version: string }>("nodes.readText", { id: nodeId });
      set({ text: res.text, version: res.version, dirty: false, status: "ready", errorCode: null });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- organizer / inbox ---

interface OrganizerState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  inbox: NodeItem[];
  suggestions: { nodeId: string; dest: string; reason: string }[];
  load: () => Promise<void>;
}
export const useOrganizerStore = create<OrganizerState>((set) => ({
  status: "idle",
  errorCode: null,
  inbox: [],
  suggestions: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("organize.listInbox", {});
      const inbox = toItems<NodeDto>(res).map(mapNode);
      set({ inbox, status: inbox.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- snapshots ---

interface SnapshotsState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  snapshots: SnapshotInfo[];
  diff: SnapshotDiff | null;
  load: () => Promise<void>;
}
export const useSnapshotsStore = create<SnapshotsState>((set) => ({
  status: "idle",
  errorCode: null,
  snapshots: [],
  diff: null,
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("snapshots.list", {});
      const snapshots = toItems<SnapshotDto>(res).map(mapSnapshot);
      set({ snapshots, status: snapshots.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- ai ---

interface AiState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  aiStatus: string;
  load: () => Promise<void>;
}
export const useAiStore = create<AiState>((set) => ({
  status: "idle",
  errorCode: null,
  aiStatus: "notConfigured",
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "not-configured", aiStatus: "notConfigured", errorCode: "AI_NOT_CONFIGURED" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<{ status: string }>("ai.getStatus", {});
      set({ aiStatus: res.status, status: "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

// --- accounts / projects / duplicates / trash (light slices for views) ---

interface AccountsState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  accounts: AccountInfo[];
  load: () => Promise<void>;
}
export const useAccountsStore = create<AccountsState>((set) => ({
  status: "idle",
  errorCode: null,
  accounts: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("accounts.list", {});
      const accounts = toItems<AccountDto>(res).map(mapAccount);
      set({ accounts, status: accounts.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

interface ProjectsState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  projects: ProjectInfo[];
  load: () => Promise<void>;
}
export const useProjectsStore = create<ProjectsState>((set) => ({
  status: "idle",
  errorCode: null,
  projects: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("projects.list", {});
      const projects = toItems<ProjectDto>(res).map(mapProject);
      set({ projects, status: projects.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

interface DuplicatesState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  groups: { hash: string; nodeIds: string[] }[];
  load: () => Promise<void>;
}
export const useDuplicatesStore = create<DuplicatesState>((set) => ({
  status: "idle",
  errorCode: null,
  groups: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>(
        "duplicates.list",
        {},
      );
      const groups = toItems<DuplicateGroupDto>(res).map(mapDuplicateGroup);
      set({ groups, status: groups.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));

interface TrashState {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  items: NodeItem[];
  load: () => Promise<void>;
}
export const useTrashStore = create<TrashState>((set) => ({
  status: "idle",
  errorCode: null,
  items: [],
  load: async () => {
    const client = getActiveClient();
    if (!client) {
      set({ status: "offline", errorCode: "OFFLINE" });
      return;
    }
    set({ status: "loading", errorCode: null });
    try {
      const res = await client.invoke<unknown>("nodes.listTrash", {});
      const items = toItems<NodeDto>(res).map(mapNode);
      set({ items, status: items.length === 0 ? "empty" : "ready" });
    } catch (e) {
      const code = toErrorCode(e);
      set({ status: statusFromError(code), errorCode: code });
    }
  },
}));
