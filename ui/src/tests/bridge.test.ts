import { describe, expect, it, vi, beforeEach } from "vitest";
import { BridgeClient, type BridgeEnvelope, type Transport } from "@core/bridge";

function loopback(respond: (req: BridgeEnvelope) => BridgeEnvelope | null): Transport {
  const listeners = new Set<(m: BridgeEnvelope) => void>();
  return {
    postMessage: (msg: BridgeEnvelope) => {
      const res = respond(msg);
      if (res) {
        queueMicrotask(() => {
          for (const l of listeners) {
            l(res);
          }
        });
      }
    },
    onMessage: (cb: (m: BridgeEnvelope) => void) => {
      listeners.add(cb);
      return () => {
        listeners.delete(cb);
      };
    },
  };
}

describe("BridgeClient", () => {
  it("resolves requests", async () => {
    const t = loopback((req) => ({ v: 1, id: req.id, type: "res", result: { ok: true } }));
    const c = new BridgeClient(t);
    const r = await c.invoke<{ ok: boolean }>("app.getInfo", {});
    expect(r.ok).toBe(true);
  });

  it("rejects on error code", async () => {
    const t = loopback((req) => ({
      v: 1,
      id: req.id,
      type: "res",
      error: { code: "NOT_FOUND", message: "x", retryable: false },
    }));
    const c = new BridgeClient(t);
    await expect(c.invoke("nodes.get", {})).rejects.toMatchObject({ code: "NOT_FOUND" });
  });

  it("supports AbortSignal cancel", async () => {
    const seen: BridgeEnvelope[] = [];
    const t: Transport = {
      postMessage: (m) => {
        seen.push(m);
      },
      onMessage: () => () => undefined,
    };
    const c = new BridgeClient(t);
    const ctrl = new AbortController();
    const p = c.invoke("nodes.get", {}, { signal: ctrl.signal, timeoutMs: 5000 });
    ctrl.abort();
    await expect(p).rejects.toMatchObject({ code: "CANCELLED" });
    expect(seen.some((m) => m.type === "cancel")).toBe(true);
  });

  it("times out", async () => {
    const t: Transport = {
      postMessage: () => undefined,
      onMessage: () => () => undefined,
    };
    const c = new BridgeClient(t);
    await expect(c.invoke("nodes.get", {}, { timeoutMs: 10 })).rejects.toMatchObject({
      code: "UNKNOWN",
    });
  });

  it("delivers events", () => {
    const listeners = new Set<(m: BridgeEnvelope) => void>();
    const t: Transport = {
      postMessage: () => undefined,
      onMessage: (cb) => {
        listeners.add(cb);
        return () => {
          listeners.delete(cb);
        };
      },
    };
    const c = new BridgeClient(t);
    const cb = vi.fn();
    const unsub = c.subscribe("node.changed", cb);
    for (const l of listeners) {
      l({ v: 1, id: "e1", type: "evt", method: "node.changed", result: { id: "n1" } });
    }
    expect(cb).toHaveBeenCalledWith({ id: "n1" });
    unsub();
  });

  it("drops events that fail validation", () => {
    const listeners = new Set<(m: BridgeEnvelope) => void>();
    const t: Transport = {
      postMessage: () => undefined,
      onMessage: (cb) => {
        listeners.add(cb);
        return () => {
          listeners.delete(cb);
        };
      },
    };
    const c = new BridgeClient(t);
    const cb = vi.fn();
    const isNode = (v: unknown): v is { id: string } =>
      typeof v === "object" && v !== null && "id" in v;
    const unsub = c.subscribe("node.changed", cb, isNode);
    for (const l of listeners) {
      l({ v: 1, id: "e1", type: "evt", method: "node.changed", result: { id: "n1" } });
      l({ v: 1, id: "e2", type: "evt", method: "node.changed", result: 42 });
    }
    expect(cb).toHaveBeenCalledTimes(1);
    expect(cb).toHaveBeenCalledWith({ id: "n1" });
    unsub();
  });

  it("hello accepts compatible host", async () => {
    const t = loopback((req) => ({
      v: 1,
      id: req.id,
      type: "res",
      result: { contract: { major: 1, minor: 0 } },
    }));
    const c = new BridgeClient(t);
    await c.hello(1000);
    expect(c.getReady()).toBe("ready");
  });

  it("hello rejects incompatible major", async () => {
    const t = loopback((req) => ({
      v: 1,
      id: req.id,
      type: "res",
      result: { contract: { major: 2, minor: 0 } },
    }));
    const c = new BridgeClient(t);
    await expect(c.hello(1000)).rejects.toMatchObject({ code: "UI_INCOMPATIBLE" });
    expect(c.getReady()).toBe("incompatible");
  });

  it("hello marks disconnected on transport failure", async () => {
    const t: Transport = {
      postMessage: () => undefined,
      onMessage: () => () => undefined,
    };
    const c = new BridgeClient(t);
    await expect(c.hello(20)).rejects.toBeDefined();
    expect(c.getReady()).toBe("disconnected");
  });

  it("reconnect retries hello", async () => {
    const t = loopback((req) => ({
      v: 1,
      id: req.id,
      type: "res",
      result: { contract: { major: 1, minor: 5 } },
    }));
    const c = new BridgeClient(t);
    await c.reconnect(1);
    expect(c.getReady()).toBe("ready");
  });

  it("tracks ready listeners and pending count", async () => {
    const t = loopback((req) => ({ v: 1, id: req.id, type: "res", result: { contract: { major: 1, minor: 0 } } }));
    const c = new BridgeClient(t);
    const states: string[] = [];
    const off = c.onReadyChange((s) => {
      states.push(s);
    });
    await c.hello(1000);
    expect(states).toContain("ready");
    off();
    expect(c.pendingCount()).toBe(0);
    expect(c.supportedMethods().length).toBeGreaterThan(100);
  });
});

describe("bridge misc", () => {
  it("ignores unknown res ids", () => {
    const listeners = new Set<(m: BridgeEnvelope) => void>();
    const t: Transport = {
      postMessage: () => undefined,
      onMessage: (cb) => {
        listeners.add(cb);
        return () => {
          listeners.delete(cb);
        };
      },
    };
    const c = new BridgeClient(t);
    for (const l of listeners) {
      l({ v: 1, id: "nope", type: "res", result: {} });
    }
    expect(c.pendingCount()).toBe(0);
  });

  beforeEach(() => {
    vi.unstubAllGlobals();
  });
});
