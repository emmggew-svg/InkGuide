import { describe, expect, it, beforeEach } from "vitest";
import { BridgeClient, setActiveClient, type Transport, type BridgeEnvelope } from "@core/bridge";
import {
  useAccountsStore,
  useActivityStore,
  useAiStore,
  useConflictsStore,
  useDuplicatesStore,
  useEditorStore,
  useOrganizerStore,
  useProjectsStore,
  useReposStore,
  useSearchStore,
  useSettingsStore,
  useSnapshotsStore,
  useSyncStore,
  useTagsStore,
  useTransfersStore,
  useTrashStore,
  useTreeStore,
} from "@core/stores";

function failingTransport(code: "OFFLINE" | "NOT_FOUND" | "UNKNOWN"): Transport {
  const listeners = new Set<(m: BridgeEnvelope) => void>();
  return {
    postMessage: (msg: BridgeEnvelope) => {
      queueMicrotask(() => {
        for (const l of listeners) {
          l({ v: 1, id: msg.id, type: "res", error: { code, message: "boom", retryable: false } });
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

beforeEach(() => {
  setActiveClient(null);
});

describe("stores offline and error branches", () => {
  it("all loads go offline without client", async () => {
    await useTreeStore.getState().loadChildren(null);
    await useReposStore.getState().load();
    await useReposStore.getState().loadOverview("x");
    await useTagsStore.getState().load();
    await useSearchStore.getState().run();
    await useSyncStore.getState().load();
    await useConflictsStore.getState().load();
    await useTransfersStore.getState().load();
    await useActivityStore.getState().load();
    await useOrganizerStore.getState().load();
    await useSnapshotsStore.getState().load();
    await useAccountsStore.getState().load();
    await useProjectsStore.getState().load();
    await useDuplicatesStore.getState().load();
    await useTrashStore.getState().load();
    await useEditorStore.getState().save();
    await useAiStore.getState().load();
    await useSettingsStore.getState().load();
    expect(useTreeStore.getState().status).toBe("offline");
    expect(useAiStore.getState().status).toBe("not-configured");
  });

  it("all loads surface NOT_FOUND as error", async () => {
    setActiveClient(new BridgeClient(failingTransport("NOT_FOUND")));
    await useTreeStore.getState().loadChildren(null);
    await useReposStore.getState().load();
    await useReposStore.getState().loadOverview("x");
    await useTagsStore.getState().load();
    await useSearchStore.getState().run();
    await useSyncStore.getState().load();
    await useConflictsStore.getState().load();
    await useTransfersStore.getState().load();
    await useActivityStore.getState().load();
    await useOrganizerStore.getState().load();
    await useSnapshotsStore.getState().load();
    await useAccountsStore.getState().load();
    await useProjectsStore.getState().load();
    await useDuplicatesStore.getState().load();
    await useTrashStore.getState().load();
    useEditorStore.getState().open("n", "t", "v");
    await useEditorStore.getState().save();
    await useAiStore.getState().load();
    await useSettingsStore.getState().load();
    expect(useTreeStore.getState().status).toBe("error");
    expect(useReposStore.getState().errorCode).toBe("NOT_FOUND");
  });

  it("unknown errors map to UNKNOWN", async () => {
    setActiveClient(new BridgeClient(failingTransport("UNKNOWN")));
    await useTreeStore.getState().loadChildren(null);
    expect(useTreeStore.getState().errorCode).toBe("UNKNOWN");
    // non-bridge error shapes also map safely
    const listeners = new Set<(m: BridgeEnvelope) => void>();
    const weird: Transport = {
      postMessage: (msg) => {
        queueMicrotask(() => {
          const bad = {
            v: 1,
            id: msg.id,
            type: "res",
            error: { code: "WEIRD", message: "weird", retryable: false },
          } as unknown as BridgeEnvelope;
          for (const l of listeners) {
            l(bad);
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
    setActiveClient(new BridgeClient(weird));
    await useTreeStore.getState().loadChildren(null);
    expect(useTreeStore.getState().errorCode).toBe("UNKNOWN");
  });
});
