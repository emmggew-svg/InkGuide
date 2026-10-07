import {
  BRIDGE_METHODS,
  CONTRACT_MAJOR,
  CONTRACT_MINOR,
  type BridgeError,
  type BridgeEventName,
  type ErrorCode,
  type MethodName,
} from "@core/contract";

export interface BridgeEnvelope {
  v: 1;
  id: string;
  type: "req" | "res" | "evt" | "cancel";
  method?: string;
  params?: unknown;
  result?: unknown;
  error?: BridgeError;
}

export interface InvokeOptions {
  timeoutMs?: number;
  signal?: AbortSignal;
  idempotencyKey?: string;
}

export interface Transport {
  postMessage(msg: BridgeEnvelope): void;
  onMessage(cb: (msg: BridgeEnvelope) => void): () => void;
}

export type ReadyState =
  | "connecting"
  | "ready"
  | "incompatible"
  | "disconnected";

interface Pending {
  resolve: (v: unknown) => void;
  reject: (e: Error) => void;
  timeoutId: ReturnType<typeof setTimeout>;
}

export const DEFAULT_TIMEOUT_MS = 30000;
export const MAX_MESSAGE_BYTES = 1024 * 1024;

function newId(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }
  return `id-${String(Date.now())}-${String(Math.floor(Math.random() * 1000000))}`;
}

type CodedError = BridgeError & Error;

function bridgeError(
  code: ErrorCode,
  message: string,
  retryable = false,
): CodedError {
  const e = new Error(message) as CodedError;
  e.code = code;
  e.message = message;
  e.retryable = retryable;
  return e;
}

function wrapHostError(err: BridgeError): CodedError {
  const e = new Error(err.message) as CodedError;
  e.code = err.code;
  e.message = err.message;
  e.retryable = err.retryable;
  e.details = err.details;
  return e;
}

// Promise-based bridge client: requests, AbortSignal cancellation, events,
// timeouts, hello handshake (major/minor), reconnect/ready state.
export class BridgeClient {
  private pending = new Map<string, Pending>();
  private handlers = new Map<BridgeEventName, Set<(p: unknown) => void>>();
  private ready: ReadyState = "connecting";
  private readyListeners = new Set<(s: ReadyState) => void>();
  private helloResult: unknown = null;
  private reconnectAttempts = 0;

  constructor(private readonly transport: Transport) {
    this.transport.onMessage((msg) => {
      this.handleMessage(msg);
    });
  }

  getReady(): ReadyState {
    return this.ready;
  }

  onReadyChange(cb: (s: ReadyState) => void): () => void {
    this.readyListeners.add(cb);
    return () => {
      this.readyListeners.delete(cb);
    };
  }

  private setReady(s: ReadyState): void {
    this.ready = s;
    for (const cb of this.readyListeners) {
      cb(s);
    }
  }

  subscribe<T>(
    event: BridgeEventName,
    cb: (payload: T) => void,
    validate?: (v: unknown) => v is T,
  ): () => void {
    let set = this.handlers.get(event);
    if (!set) {
      set = new Set();
      this.handlers.set(event, set);
    }
    const wrapped = (p: unknown): void => {
      if (validate !== undefined && !validate(p)) {
        return;
      }
      cb(p as T);
    };
    set.add(wrapped);
    return () => {
      const s = this.handlers.get(event);
      if (s) {
        s.delete(wrapped);
      }
    };
  }

  invoke<T>(
    method: MethodName,
    params?: unknown,
    opts?: InvokeOptions & { validate?: (v: unknown) => v is T },
  ): Promise<T> {
    const id = newId();
    const timeoutMs = opts?.timeoutMs ?? DEFAULT_TIMEOUT_MS;
    const payload: Record<string, unknown> =
      typeof params === "object" && params !== null
        ? { ...(params as Record<string, unknown>) }
        : { value: params };
    if (opts?.idempotencyKey !== undefined) {
      payload["idempotencyKey"] = opts.idempotencyKey;
    }
    const envelope: BridgeEnvelope = {
      v: 1,
      id,
      type: "req",
      method,
      params: payload,
    };
    const encoded = JSON.stringify(envelope);
    if (encoded.length > MAX_MESSAGE_BYTES) {
      return Promise.reject(
        bridgeError("INVALID_PARAMS", "message exceeds max size"),
      );
    }
    return new Promise<T>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        this.pending.delete(id);
        this.sendCancel(id);
        reject(bridgeError("UNKNOWN", `request timed out: ${method}`, true));
      }, timeoutMs);
      this.pending.set(id, {
        resolve: (v: unknown) => {
          const typed = v as T;
          const validate = opts?.validate;
          if (validate !== undefined && !validate(typed)) {
            reject(bridgeError("INTEGRITY_ERROR", "bridge payload failed validation"));
            return;
          }
          resolve(typed);
        },
        reject,
        timeoutId,
      });
      const onAbort = (): void => {
        const p = this.pending.get(id);
        if (p) {
          clearTimeout(p.timeoutId);
          this.pending.delete(id);
          this.sendCancel(id);
          reject(bridgeError("CANCELLED", "request cancelled"));
        }
      };
      if (opts?.signal) {
        if (opts.signal.aborted) {
          onAbort();
          return;
        }
        opts.signal.addEventListener("abort", onAbort, { once: true });
      }
      this.transport.postMessage(envelope);
    });
  }

  private sendCancel(id: string): void {
    this.transport.postMessage({ v: 1, id, type: "cancel" });
  }

  private handleMessage(msg: BridgeEnvelope): void {
    if (msg.type === "res") {
      const p = this.pending.get(msg.id);
      if (!p) {
        return;
      }
      clearTimeout(p.timeoutId);
      this.pending.delete(msg.id);
      if (msg.error) {
        p.reject(wrapHostError(msg.error));
      } else {
        p.resolve(msg.result);
      }
      return;
    }
    if (msg.type === "evt" && typeof msg.method === "string") {
      const set = this.handlers.get(msg.method as BridgeEventName);
      if (set) {
        const payload = msg.result ?? msg.params;
        for (const h of set) {
          h(payload);
        }
      }
    }
  }

  // Handshake: declares UI contract, checks host compatibility.
  // Compatible = same major, host minor >= UI minor.
  async hello(timeoutMs = DEFAULT_TIMEOUT_MS): Promise<unknown> {
    this.setReady("connecting");
    const controller = new AbortController();
    const timer = setTimeout(() => {
      controller.abort();
    }, timeoutMs);
    try {
      const res = await this.invoke<{
        contract: { major: number; minor: number };
      }>("app.hello", { contract: { major: CONTRACT_MAJOR, minor: CONTRACT_MINOR } }, {
        timeoutMs,
        signal: controller.signal,
      });
      const hostMajor: number = res.contract.major;
      const hostMinor: number = res.contract.minor;
      if (hostMajor !== CONTRACT_MAJOR || hostMinor < CONTRACT_MINOR) {
        this.setReady("incompatible");
        throw bridgeError(
          "UI_INCOMPATIBLE",
          `contract mismatch host ${String(hostMajor)}.${String(hostMinor)} ui ${String(CONTRACT_MAJOR)}.${String(CONTRACT_MINOR)}`,
        );
      }
      this.helloResult = res;
      this.reconnectAttempts = 0;
      this.setReady("ready");
      return res;
    } catch (e) {
      const err = e as Partial<CodedError>;
      if (err.code === "UI_INCOMPATIBLE" && e instanceof Error) {
        throw e;
      }
      this.setReady("disconnected");
      if (e instanceof Error) {
        throw e;
      }
      throw bridgeError("UNKNOWN", "hello failed");
    } finally {
      clearTimeout(timer);
    }
  }

  getHelloResult(): unknown {
    return this.helloResult;
  }

  async reconnect(delayMs?: number): Promise<unknown> {
    this.reconnectAttempts += 1;
    const backoff = delayMs ?? Math.min(1000 * this.reconnectAttempts, 10000);
    await new Promise((r) => {
      setTimeout(r, backoff);
    });
    return this.hello();
  }

  pendingCount(): number {
    return this.pending.size;
  }

  supportedMethods(): readonly MethodName[] {
    return BRIDGE_METHODS;
  }
}

let activeClient: BridgeClient | null = null;

export function setActiveClient(c: BridgeClient | null): void {
  activeClient = c;
}

export function getActiveClient(): BridgeClient | null {
  return activeClient;
}
