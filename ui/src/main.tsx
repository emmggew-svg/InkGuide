import React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./app/App";
import { BridgeClient, setActiveClient, type Transport, type BridgeEnvelope } from "@core/bridge";
import { isErrorCode } from "@core/contract";
import { fixtureInvoke } from "@core/fixtureBridge";
import "./theme/tokens.css";
import "./theme/structure.css";

function isEnvelope(v: unknown): v is BridgeEnvelope {
  return (
    typeof v === "object" &&
    v !== null &&
    typeof (v as { id: unknown }).id === "string" &&
    typeof (v as { type: unknown }).type === "string"
  );
}

function webviewTransport(): Transport | null {  const w = window as unknown as {
    chrome?: { webview?: { postMessage: (m: string) => void; addEventListener: (e: string, cb: (ev: { data: string }) => void) => void } };
  };
  if (!w.chrome?.webview) {
    return null;
  }
  const listeners = new Set<(m: BridgeEnvelope) => void>();
  w.chrome.webview.addEventListener("message", (ev) => {
    try {
      // PostWebMessageAsJson delivers the parsed object; a plain
      // postMessage(string) would arrive as text. Accept both shapes.
      const raw = (ev as unknown as { data: unknown }).data;
      const parsed: unknown = typeof raw === "string" ? JSON.parse(raw) : raw;
      if (!isEnvelope(parsed)) {
        return;
      }
      for (const l of listeners) {
        l(parsed);
      }
    } catch {
      // ignore malformed host messages
    }
  });
  return {
    postMessage: (msg: BridgeEnvelope) => {
      w.chrome?.webview?.postMessage(JSON.stringify(msg));
    },
    onMessage: (cb: (m: BridgeEnvelope) => void) => {
      listeners.add(cb);
      return () => {
        listeners.delete(cb);
      };
    },
  };
}

function fixtureTransport(): Transport {
  const listeners = new Set<(m: BridgeEnvelope) => void>();
  return {
    postMessage: (msg: BridgeEnvelope) => {
      if (msg.type !== "req" || !msg.method) {
        return;
      }
      const method = msg.method as Parameters<typeof fixtureInvoke>[0];
      void fixtureInvoke<unknown>(method, msg.params)
        .then((result) => {
          const res: BridgeEnvelope = { v: 1, id: msg.id, type: "res", result };
          for (const l of listeners) {
            l(res);
          }
        })
        .catch((e: unknown) => {
          const err = e as { code?: unknown; message?: unknown };
          const code = typeof err.code === "string" && isErrorCode(err.code) ? err.code : "UNKNOWN";
          const message = typeof err.message === "string" ? err.message : "fixture error";
          const res: BridgeEnvelope = {
            v: 1,
            id: msg.id,
            type: "res",
            error: {
              code,
              message,
              retryable: false,
            },
          };
          for (const l of listeners) {
            l(res);
          }
        });
    },
    onMessage: (cb: (m: BridgeEnvelope) => void) => {
      listeners.add(cb);
      return () => {
        listeners.delete(cb);
      };
    },
  };
}

async function boot(): Promise<void> {
  const params = new URLSearchParams(window.location.search);
  // DEV ONLY: ?bridge=fixture loads the in-memory fixture bridge (no host).
  // Tree-shaken from production (vite prod alias → stub).
  const wantFixture = params.get("bridge") === "fixture";
  const transport = wantFixture ? null : webviewTransport();
  const active: Transport = transport === null ? fixtureTransport() : transport;
  const client = new BridgeClient(active);
  setActiveClient(client);
  if (transport) {
    try {
      await client.hello();
    } catch {
      // stay disconnected; views render offline/incompatible states
    }
  }
  const rootEl = document.getElementById("root");
  if (!rootEl) {
    throw new Error("missing #root");
  }
  createRoot(rootEl).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>,
  );
}

void boot();
