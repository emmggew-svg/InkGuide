import { useEffect } from "react";
import { getActiveClient } from "@core/bridge";
import { useOrganizerStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function InboxOrganizerView(): React.JSX.Element {
  const t = useT();
  const status = useOrganizerStore((s) => s.status);
  const errorCode = useOrganizerStore((s) => s.errorCode);
  const inbox = useOrganizerStore((s) => s.inbox);
  const load = useOrganizerStore((s) => s.load);

  useEffect(() => {
    if (status === "idle") {
      void load();
    }
  }, [status, load]);

  const suggest = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("organize.suggest", { nodeIds: inbox.map((n) => n.id) });
    }
  };
  const apply = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("organize.apply", { moves: [] });
    }
  };

  return (
    <section className="inbox" aria-label={t("inbox.title")}>
      <h2>{t("inbox.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="inbox">
        <button type="button" data-testid="inbox-refresh" data-action="inbox-refresh" onClick={() => { void load(); }}>
          {t("common.refresh")}
        </button>
        <ul className="inbox__list">
          {inbox.map((n) => (
            <li key={n.id} data-testid={`inbox-item-${n.id}`} data-action="inbox-item">
              {n.name}
            </li>
          ))}
        </ul>
        <button type="button" data-testid="inbox-suggest" data-action="organize-suggest" onClick={suggest}>
          {t("inbox.suggest")}
        </button>
        <button type="button" data-testid="inbox-apply" data-action="organize-apply" onClick={apply}>
          {t("inbox.apply")}
        </button>
      </ViewState>
    </section>
  );
}
