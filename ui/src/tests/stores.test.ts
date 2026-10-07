import { beforeEach, describe, expect, it } from "vitest";
import {
  BridgeClient,
  setActiveClient,
  type BridgeEnvelope,
  type Transport,
} from "@core/bridge";
import {
  statusFromError,
  useAccountsStore,
  useActivityStore,
  useAiStore,
  useConflictsStore,
  useDuplicatesStore,
  useEditorStore,
  useListingStore,
  useNavStore,
  useOrganizerStore,
  useProjectsStore,
  useReposStore,
  useSearchStore,
  useSelectionStore,
  useSettingsStore,
  useSnapshotsStore,
  useSyncStore,
  useTagsStore,
  useTransfersStore,
  useTrashStore,
  useTreeStore,
  visibleListing,
  type StoreStatus,
} from "@core/stores";
import type { MethodName } from "@core/contract";

function testTransport(handlers: Partial<Record<MethodName, (p: unknown) => unknown>>): Transport {
  const listeners = new Set<(m: BridgeEnvelope) => void>();
  return {
    postMessage: (msg: BridgeEnvelope) => {
      if (msg.type !== "req" || !msg.method) {
        return;
      }
      const h = handlers[msg.method as MethodName];
      queueMicrotask(() => {
        if (!h) {
          const res: BridgeEnvelope = {
            v: 1,
            id: msg.id,
            type: "res",
            error: { code: "NOT_FOUND", message: "no handler", retryable: false },
          };
          for (const l of listeners) {
            l(res);
          }
          return;
        }
        try {
          const result = h(msg.params);
          const res: BridgeEnvelope = { v: 1, id: msg.id, type: "res", result };
          for (const l of listeners) {
            l(res);
          }
        } catch (e) {
          const err = e as { code?: unknown };
          const code = err.code === "OFFLINE" ? "OFFLINE" : "UNKNOWN";
          const res: BridgeEnvelope = {
            v: 1,
            id: msg.id,
            type: "res",
            error: { code, message: "err", retryable: false },
          };
          for (const l of listeners) {
            l(res);
          }
        }
      });
    },
    onMessage: (cb) => {
      listeners.add(cb);
      return () => {
        listeners.delete(cb);
      };
    },
  };
}

const OK_HANDLERS: Partial<Record<MethodName, (p: unknown) => unknown>> = {
  "nodes.list": () => ({ items: [{ id: "n1", parentId: null, name: "a", isFolder: true, mime: "folder", size: 0, modifiedTime: "2026-01-01T00:00:00Z", trashed: false }], cursor: null }),
  "repos.list": () => ({ items: [{ id: "r1", name: "repo", kind: "single" }] }),
  "repos.getOverview": () => ({ id: "r1", name: "repo", totalFiles: 1, totalBytes: 10, readmeExcerpt: "hi", projects: [], byKind: {} }),
  "transfers.list": () => ({ items: [] }),
  "sync.getStatus": () => ({ state: "idle" }),
  "sync.listOutbox": () => ({ items: [] }),
  "sync.listConflicts": () => ({ items: [] }),
  "tags.list": () => ({ items: [] }),
  "tags.listSuggestions": () => ({ items: [] }),
  "search.query": () => ({ items: [] }),
  "settings.get": () => ({ values: { language: "en-US" } }),
  "activity.list": () => ({ items: [] }),
  "nodes.writeText": () => ({}),
  "organize.listInbox": () => ({ items: [] }),
  "snapshots.list": () => ({ items: [] }),
  "ai.getStatus": () => ({ status: "ready" }),
  "accounts.list": () => ({ items: [] }),
  "projects.list": () => ({ items: [] }),
  "duplicates.list": () => ({ items: [] }),
  "nodes.listTrash": () => ({ items: [] }),
};

beforeEach(() => {
  setActiveClient(null);
  useTreeStore.setState({ status: "idle", errorCode: null, nodes: [], expanded: {} });
  useReposStore.setState({ status: "idle", errorCode: null, repos: [], overview: null });
  useSelectionStore.setState({ ids: [] });
  useListingStore.setState({ status: "idle", errorCode: null, items: [], sortAsc: true, filter: "" });
});

describe("statusFromError", () => {
  it("maps codes", () => {
    const cases: [Parameters<typeof statusFromError>[0], StoreStatus][] = [
      ["OFFLINE", "offline"],
      ["REAUTH_REQUIRED", "reauth"],
      ["AUTH_REQUIRED", "reauth"],
      ["OAUTH_NOT_CONFIGURED", "not-configured"],
      ["AI_NOT_CONFIGURED", "not-configured"],
      ["AI_CONSENT_REQUIRED", "not-configured"],
      ["PERMISSION_DENIED", "denied"],
      ["NOT_FOUND", "error"],
      ["UNKNOWN", "error"],
      ["CANCELLED", "error"],
    ];
    for (const [code, want] of cases) {
      expect(statusFromError(code)).toBe(want);
    }
  });
});

describe("stores", () => {
  it("nav navigates", () => {
    useNavStore.getState().go("browser");
    expect(useNavStore.getState().route).toBe("browser");
    useNavStore.getState().setStatus("ready");
    expect(useNavStore.getState().status).toBe("ready");
  });

  it("selection toggles", () => {
    const s = useSelectionStore.getState();
    s.selectAll(["a", "b"]);
    expect(useSelectionStore.getState().ids).toEqual(["a", "b"]);
    s.toggle("a");
    expect(useSelectionStore.getState().ids).toEqual(["b"]);
    s.clear();
    expect(useSelectionStore.getState().ids).toEqual([]);
  });

  it("visibleListing filters and sorts", () => {
    const items = [
      { id: "1", parentId: null, name: "b", isFolder: false, mime: "x", size: 0, modifiedTime: "", trashed: false },
      { id: "2", parentId: null, name: "a", isFolder: false, mime: "x", size: 0, modifiedTime: "", trashed: false },
    ];
    expect(visibleListing(items, "", true).map((x) => x.name)).toEqual(["a", "b"]);
    expect(visibleListing(items, "b", true).map((x) => x.name)).toEqual(["b"]);
    expect(visibleListing(items, "", false).map((x) => x.name)).toEqual(["b", "a"]);
  });

  it("tree offline without client", async () => {
    await useTreeStore.getState().loadChildren(null);
    expect(useTreeStore.getState().status).toBe("offline");
  });

  it("tree loads via bridge", async () => {
    setActiveClient(new BridgeClient(testTransport(OK_HANDLERS)));
    await useTreeStore.getState().loadChildren(null);
    expect(useTreeStore.getState().status).toBe("ready");
    useTreeStore.getState().toggle("n1");
    expect(useTreeStore.getState().expanded["n1"]).toBe(true);
    useTreeStore.getState().setNodes([]);
    expect(useTreeStore.getState().status).toBe("empty");
  });

  it("tree surfaces bridge errors", async () => {
    setActiveClient(
      new BridgeClient(
        testTransport({
          "nodes.list": () => {
            throw Object.assign(new Error("offline"), { code: "OFFLINE" });
          },
        }),
      ),
    );
    await useTreeStore.getState().loadChildren(null);
    expect(useTreeStore.getState().status).toBe("offline");
  });

  it("repos/tags/search/sync/conflicts/transfers/activity/organizer/snapshots/ai/accounts/projects/duplicates/trash load", async () => {
    setActiveClient(new BridgeClient(testTransport(OK_HANDLERS)));
    await useReposStore.getState().load();
    expect(useReposStore.getState().status).toBe("ready");
    await useReposStore.getState().loadOverview("r1");
    expect(useReposStore.getState().overview?.id).toBe("r1");
    await useTagsStore.getState().load();
    await useSearchStore.getState().run();
    await useSyncStore.getState().load();
    expect(useSyncStore.getState().syncState).toBe("idle");
    await useConflictsStore.getState().load();
    await useTransfersStore.getState().load();
    await useActivityStore.getState().load();
    await useOrganizerStore.getState().load();
    await useSnapshotsStore.getState().load();
    await useAiStore.getState().load();
    await useAccountsStore.getState().load();
    await useProjectsStore.getState().load();
    await useDuplicatesStore.getState().load();
    await useTrashStore.getState().load();
    expect(useTrashStore.getState().status).toBe("empty");
  });

  it("settings loads language and editor saves", async () => {
    setActiveClient(new BridgeClient(testTransport(OK_HANDLERS)));
    await useSettingsStore.getState().load();
    expect(useSettingsStore.getState().language).toBe("en-US");
    useSettingsStore.getState().setLanguage("tr-TR");
    expect(useSettingsStore.getState().language).toBe("tr-TR");
    useEditorStore.getState().open("n1", "hi", "v1");
    useEditorStore.getState().edit("hello");
    expect(useEditorStore.getState().dirty).toBe(true);
    await useEditorStore.getState().save();
    expect(useEditorStore.getState().dirty).toBe(false);
  });

  it("listing setters work", () => {
    useListingStore.getState().setFilter("x");
    useListingStore.getState().setSort(false);
    useListingStore.getState().setItems([]);
    expect(useListingStore.getState().status).toBe("empty");
  });

  it("search query setter", () => {
    useSearchStore.getState().setQuery("q");
    expect(useSearchStore.getState().query).toBe("q");
  });
});
