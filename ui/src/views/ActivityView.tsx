import { useEffect } from "react";
import { getActiveClient } from "@core/bridge";
import { useActivityStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function ActivityView(): React.JSX.Element {
  const t = useT();
  const status = useActivityStore((s) => s.status);
  const errorCode = useActivityStore((s) => s.errorCode);
  const entries = useActivityStore((s) => s.entries);
  const load = useActivityStore((s) => s.load);

  useEffect(() => {
    if (status === "idle") {
      void load();
    }
  }, [status, load]);

  return (
    <section className="activity" aria-label={t("activity.title")}>
      <h2>{t("activity.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="activity">
        <button type="button" data-testid="activity-refresh" data-action="activity-refresh" onClick={() => { void load(); }}>
          {t("common.refresh")}
        </button>
        <ul className="activity__list">
          {entries.map((e) => (
            <li key={e.id}>
              <span data-testid={`activity-item-${e.id}`} data-action="activity-item">
                {e.what}
              </span>
              {e.undoable ? (
                <button
                  type="button"
                  data-testid={`activity-undo-${e.id}`}
                  data-action="activity-undo"
                  onClick={() => {
                    const c = getActiveClient();
                    if (c) {
                      void c.invoke("activity.undo", { id: e.id });
                    }
                  }}
                >
                  {t("activity.undo")}
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      </ViewState>
    </section>
  );
}
