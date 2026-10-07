import { getActiveClient } from "@core/bridge";
import { useTrashStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function TrashView(): React.JSX.Element {
  const t = useT();
  const status = useTrashStore((s) => s.status);
  const errorCode = useTrashStore((s) => s.errorCode);
  const items = useTrashStore((s) => s.items);

  return (
    <section className="trash" aria-label={t("trash.title")}>
      <h2>{t("trash.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="trash">
        <button
          type="button"
          data-testid="trash-empty"
          data-action="trash-empty"
          onClick={() => {
            const c = getActiveClient();
            if (c) {
              void c.invoke("nodes.emptyTrash", {});
            }
          }}
        >
          {t("trash.empty")}
        </button>
        <ul className="trash__list">
          {items.map((n) => (
            <li key={n.id}>
              <span data-testid={`trash-item-${n.id}`} data-action="trash-item">
                {n.name}
              </span>
              <button
                type="button"
                data-testid={`trash-restore-${n.id}`}
                data-action="trash-restore"
                onClick={() => {
                  const c = getActiveClient();
                  if (c) {
                    void c.invoke("nodes.restore", { id: n.id });
                  }
                }}
              >
                {t("trash.restore")}
              </button>
              <button
                type="button"
                data-testid={`trash-delete-${n.id}`}
                data-action="trash-delete-permanent"
                onClick={() => {
                  const c = getActiveClient();
                  if (c) {
                    void c.invoke("nodes.deletePermanent", { id: n.id });
                  }
                }}
              >
                {t("trash.deletePermanent")}
              </button>
            </li>
          ))}
        </ul>
      </ViewState>
    </section>
  );
}
