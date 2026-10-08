import { useEffect } from "react";
import { getActiveClient } from "@core/bridge";
import { useConflictsStore, useSyncStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function SyncView(): React.JSX.Element {
  const t = useT();
  const status = useSyncStore((s) => s.status);
  const errorCode = useSyncStore((s) => s.errorCode);
  const syncState = useSyncStore((s) => s.syncState);
  const outbox = useSyncStore((s) => s.outbox);
  const cStatus = useConflictsStore((s) => s.status);
  const cError = useConflictsStore((s) => s.errorCode);
  const conflicts = useConflictsStore((s) => s.conflicts);
  const loadSync = useSyncStore((s) => s.load);
  const loadConflicts = useConflictsStore((s) => s.load);

  useEffect(() => {
    if (status === "idle") {
      void loadSync();
    }
  }, [status, loadSync]);
  useEffect(() => {
    if (cStatus === "idle") {
      void loadConflicts();
    }
  }, [cStatus, loadConflicts]);

  const act = (method: "sync.syncNow" | "sync.retryOutbox" | "sync.discardOutbox" | "sync.pause" | "sync.resume"): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke(method, {});
    }
  };
  const resolve = (id: string, strategy: "keepLocal" | "keepRemote" | "keepBoth"): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("sync.resolveConflict", { id, strategy });
    }
  };

  return (
    <section className="sync" aria-label={t("sync.title")}>
      <h2>{t("sync.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="sync">
        <button
          type="button"
          data-testid="sync-refresh"
          data-action="sync-refresh"
          onClick={() => {
            void loadSync();
            void loadConflicts();
          }}
        >
          {t("common.refresh")}
        </button>
        <p data-testid="sync-state" data-action="sync-state">
          {`${t("sync.status")}: ${syncState}`}
        </p>
        <button type="button" data-testid="sync-now" data-action="sync-now" onClick={() => { act("sync.syncNow"); }}>
          {t("sync.syncNow")}
        </button>
        <section aria-label={t("sync.outbox")}>
          <h3>{t("sync.outbox")}</h3>
          <ul>
            {outbox.map((o) => (
              <li key={o.id} data-testid={`sync-outbox-${o.id}`} data-action="sync-outbox">
                {o.type}
              </li>
            ))}
          </ul>
          <button type="button" data-testid="sync-retry" data-action="sync-retry" onClick={() => { act("sync.retryOutbox"); }}>
            {t("sync.retry")}
          </button>
          <button type="button" data-testid="sync-discard" data-action="sync-discard" onClick={() => { act("sync.discardOutbox"); }}>
            {t("sync.discard")}
          </button>
        </section>
        <section aria-label={t("sync.conflicts")}>
          <h3>{t("sync.conflicts")}</h3>
          <ViewState status={cStatus} errorCode={cError} t={t} prefix="conflicts">
            <ul>
              {conflicts.map((c) => (
                <li key={c.id}>
                  <span data-testid={`conflict-item-${c.id}`} data-action="conflict-item">
                    {c.reason}
                  </span>
                  <button
                    type="button"
                    data-testid={`conflict-keep-local-${c.id}`}
                    data-action="conflict-keep-local"
                    onClick={() => { resolve(c.id, "keepLocal"); }}
                  >
                    {t("sync.resolveKeepLocal")}
                  </button>
                  <button
                    type="button"
                    data-testid={`conflict-keep-remote-${c.id}`}
                    data-action="conflict-keep-remote"
                    onClick={() => { resolve(c.id, "keepRemote"); }}
                  >
                    {t("sync.resolveKeepRemote")}
                  </button>
                  <button
                    type="button"
                    data-testid={`conflict-keep-both-${c.id}`}
                    data-action="conflict-keep-both"
                    onClick={() => { resolve(c.id, "keepBoth"); }}
                  >
                    {t("sync.resolveKeepBoth")}
                  </button>
                </li>
              ))}
            </ul>
          </ViewState>
        </section>
      </ViewState>
    </section>
  );
}
