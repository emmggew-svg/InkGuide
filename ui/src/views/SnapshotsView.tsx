import { useState } from "react";
import { getActiveClient } from "@core/bridge";
import { useSnapshotsStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function SnapshotsView(): React.JSX.Element {
  const t = useT();
  const status = useSnapshotsStore((s) => s.status);
  const errorCode = useSnapshotsStore((s) => s.errorCode);
  const snapshots = useSnapshotsStore((s) => s.snapshots);
  const [message, setMessage] = useState("");

  const create = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("snapshots.create", { message, mode: "pin" });
    }
  };

  return (
    <section className="snapshots" aria-label={t("snapshots.title")}>
      <h2>{t("snapshots.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="snapshots">
        <form
          onSubmit={(e) => {
            e.preventDefault();
            create();
          }}
        >
          <label htmlFor="snapshots-msg">{t("snapshots.messageLabel")}</label>
          <input
            id="snapshots-msg"
            data-testid="snapshots-message"
            data-action="snapshots-message"
            value={message}
            onChange={(e) => {
              setMessage(e.target.value);
            }}
          />
          <button type="submit" data-testid="snapshots-create" data-action="snapshots-create">
            {t("snapshots.create")}
          </button>
        </form>
        <ul className="snapshots__list">
          {snapshots.map((s) => (
            <li key={s.id}>
              <span data-testid={`snapshots-item-${s.id}`} data-action="snapshots-item">
                {s.message}
              </span>
              <button
                type="button"
                data-testid={`snapshots-diff-${s.id}`}
                data-action="snapshots-diff"
                onClick={() => {
                  const c = getActiveClient();
                  if (c) {
                    void c.invoke("snapshots.diff", { id: s.id });
                  }
                }}
              >
                {t("snapshots.diff")}
              </button>
              <button
                type="button"
                data-testid={`snapshots-restore-${s.id}`}
                data-action="snapshots-restore"
                onClick={() => {
                  const c = getActiveClient();
                  if (c) {
                    void c.invoke("snapshots.restore", { id: s.id });
                  }
                }}
              >
                {t("snapshots.restore")}
              </button>
            </li>
          ))}
        </ul>
      </ViewState>
    </section>
  );
}
