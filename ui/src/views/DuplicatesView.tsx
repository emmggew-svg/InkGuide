import { useEffect } from "react";
import { getActiveClient } from "@core/bridge";
import { useDuplicatesStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function DuplicatesView(): React.JSX.Element {
  const t = useT();
  const status = useDuplicatesStore((s) => s.status);
  const errorCode = useDuplicatesStore((s) => s.errorCode);
  const groups = useDuplicatesStore((s) => s.groups);
  const load = useDuplicatesStore((s) => s.load);

  useEffect(() => {
    if (status === "idle") {
      void load();
    }
  }, [status, load]);

  const scan = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("duplicates.scan", {});
    }
  };

  return (
    <section className="duplicates" aria-label={t("duplicates.title")}>
      <h2>{t("duplicates.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="duplicates">
        <button type="button" data-testid="duplicates-refresh" data-action="duplicates-refresh" onClick={() => { void load(); }}>
          {t("common.refresh")}
        </button>
        <button type="button" data-testid="duplicates-scan" data-action="duplicates-scan" onClick={scan}>
          {t("duplicates.scan")}
        </button>
        <ul className="duplicates__list">
          {groups.map((g) => (
            <li key={g.hash}>
              <span data-testid={`duplicates-group-${g.hash}`} data-action="duplicates-group">
                {g.hash}
              </span>
              <button
                type="button"
                data-testid={`duplicates-resolve-${g.hash}`}
                data-action="duplicates-resolve"
                onClick={() => {
                  const c = getActiveClient();
                  const [keep, ...rest] = g.nodeIds;
                  if (c && keep !== undefined) {
                    void c.invoke("duplicates.resolve", { keepNodeId: keep, trashNodeIds: rest });
                  }
                }}
              >
                {t("duplicates.resolve")}
              </button>
            </li>
          ))}
        </ul>
      </ViewState>
    </section>
  );
}
