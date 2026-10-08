import { useEffect } from "react";
import { getActiveClient } from "@core/bridge";
import { useListingStore, useSelectionStore, useTreeStore, visibleListing } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function BrowserView(): React.JSX.Element {
  const t = useT();
  const status = useTreeStore((s) => s.status);
  const errorCode = useTreeStore((s) => s.errorCode);
  const nodes = useTreeStore((s) => s.nodes);
  const loadChildren = useTreeStore((s) => s.loadChildren);
  const toggle = useTreeStore((s) => s.toggle);
  const expanded = useTreeStore((s) => s.expanded);
  const items = useListingStore((s) => s.items);
  const sortAsc = useListingStore((s) => s.sortAsc);
  const filter = useListingStore((s) => s.filter);
  const setSort = useListingStore((s) => s.setSort);
  const setFilter = useListingStore((s) => s.setFilter);
  const sel = useSelectionStore((s) => s.ids);
  const toggleSel = useSelectionStore((s) => s.toggle);

  useEffect(() => {
    if (status === "idle") {
      void loadChildren(null);
    }
  }, [status, loadChildren]);

  const shown = visibleListing(items.length > 0 ? items : nodes, filter, sortAsc);

  const upload = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("transfers.uploadPaths", { paths: [] });
    }
  };
  const createFolder = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("nodes.createFolder", { parentId: null, name: "new-folder" });
    }
  };
  const move = (): void => {
    const c = getActiveClient();
    if (c && sel.length > 0) {
      void c.invoke("nodes.move", { ids: sel, parentId: null });
    }
  };

  return (
    <section className="browser" aria-label={t("browser.title")}>
      <h2>{t("browser.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="browser">
        <button type="button" data-testid="browser-refresh" data-action="browser-refresh" onClick={() => { void loadChildren(null); }}>
          {t("common.refresh")}
        </button>
        <nav className="browser__breadcrumb" aria-label={t("browser.breadcrumb")}>
          <span data-testid="browser-breadcrumb" data-action="browser-breadcrumb">
            {t("browser.breadcrumb")}
          </span>
        </nav>
        <div className="browser__toolbar">
          <button
            type="button"
            data-testid="browser-sort"
            data-action="browser-sort"
            onClick={() => {
              setSort(!sortAsc);
            }}
          >
            {t("browser.sort")}
          </button>
          <label htmlFor="browser-filter">{t("browser.filter")}</label>
          <input
            id="browser-filter"
            data-testid="browser-filter"
            data-action="browser-filter"
            value={filter}
            onChange={(e) => {
              setFilter(e.target.value);
            }}
          />
          <button type="button" data-testid="browser-upload" data-action="transfers-upload" onClick={upload}>
            {t("browser.upload")}
          </button>
          <button
            type="button"
            data-testid="browser-create-folder"
            data-action="browser-new-folder"
            onClick={createFolder}
          >
            {t("browser.createFolder")}
          </button>
          <button type="button" data-testid="browser-move" data-action="browser-move" onClick={move}>
            {t("browser.move")}
          </button>
        </div>
        <div className="browser__panes">
          <nav className="tree" aria-label={t("browser.title")}>
            <ul className="tree__list">
              {nodes.map((n) => (
                <li key={n.id} className="tree__row">
                  <button
                    type="button"
                    data-testid={`tree-toggle-${n.id}`}
                    data-action="tree-toggle"
                    onClick={() => {
                      toggle(n.id);
                    }}
                  >
                    {expanded[n.id] === true ? "-" : "+"}
                  </button>
                  <span data-testid={`tree-node-${n.id}`} data-action="tree-select">
                    {n.name}
                  </span>
                </li>
              ))}
            </ul>
          </nav>
          <main className="browser__list">
            <p data-testid="browser-multiselect" data-action="browser-multiselect">
              {`${t("browser.multiselect")}: ${String(sel.length)}`}
            </p>
            <table className="browser__table">
              <tbody>
                {shown.map((n) => (
                  <tr key={n.id} className="browser__row">
                    <td>
                      <input
                        type="checkbox"
                        data-testid={`browser-select-${n.id}`}
                        data-action="browser-select"
                        aria-label={n.name}
                        checked={sel.includes(n.id)}
                        onChange={() => {
                          toggleSel(n.id);
                        }}
                      />
                    </td>
                    <td data-testid={`browser-item-${n.id}`} data-action="browser-open">
                      {n.name}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </main>
        </div>
      </ViewState>
    </section>
  );
}
