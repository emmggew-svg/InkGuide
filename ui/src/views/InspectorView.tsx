import { useEffect, useState } from "react";
import { getActiveClient } from "@core/bridge";
import { useTagsStore, useTreeStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function InspectorView(): React.JSX.Element {
  const t = useT();
  const status = useTreeStore((s) => s.status);
  const errorCode = useTreeStore((s) => s.errorCode);
  const nodes = useTreeStore((s) => s.nodes);
  const loadChildren = useTreeStore((s) => s.loadChildren);
  const tags = useTagsStore((s) => s.tags);
  const loadTags = useTagsStore((s) => s.load);
  const [desc, setDesc] = useState("");
  const selected = nodes[0] ?? null;

  useEffect(() => {
    if (status === "idle") {
      void loadChildren(null);
      void loadTags();
    }
  }, [status, loadChildren, loadTags]);

  const save = (): void => {
    const c = getActiveClient();
    if (c && selected) {
      void c.invoke("insight.setSummary", { id: selected.id, summary: desc });
    }
  };
  const openExternal = (): void => {
    const c = getActiveClient();
    if (c && selected) {
      void c.invoke("nodes.openExternal", { id: selected.id });
    }
  };
  const openInBrowser = (): void => {
    const c = getActiveClient();
    if (c && selected) {
      void c.invoke("nodes.openInBrowser", { id: selected.id });
    }
  };

  return (
    <aside className="inspector" aria-label={t("inspector.title")}>
      <h2>{t("inspector.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="inspector">
        <button type="button" data-testid="inspector-refresh" data-action="inspector-refresh" onClick={() => { void loadChildren(null); void loadTags(); }}>
          {t("common.refresh")}
        </button>
        <section className="inspector__section" aria-label={t("inspector.insight")}>
          <h3>{t("inspector.insight")}</h3>
          <p data-testid="inspector-node" data-action="inspector-node">
            {selected ? selected.name : t("common.empty")}
          </p>
        </section>
        <section className="inspector__section" aria-label={t("inspector.tags")}>
          <h3>{t("inspector.tags")}</h3>
          <ul>
            {tags.map((x) => (
              <li key={x.id} data-testid={`inspector-tag-${x.id}`} data-action="inspector-tag">
                {x.name}
              </li>
            ))}
          </ul>
        </section>
        <section className="inspector__section" aria-label={t("inspector.description")}>
          <h3>{t("inspector.description")}</h3>
          <label htmlFor="inspector-desc">{t("inspector.description")}</label>
          <input
            id="inspector-desc"
            data-testid="inspector-desc-input"
            data-action="inspector-desc-input"
            value={desc}
            onChange={(e) => {
              setDesc(e.target.value);
            }}
          />
          <button type="button" data-testid="inspector-save" data-action="inspector-save" onClick={save}>
            {t("inspector.save")}
          </button>
        </section>
        <section className="inspector__section" aria-label={t("inspector.revisions")}>
          <h3>{t("inspector.revisions")}</h3>
          <p data-testid="inspector-revisions" data-action="inspector-revisions">
            {t("inspector.revisions")}
          </p>
        </section>
        <section className="inspector__section" aria-label={t("inspector.details")}>
          <h3>{t("inspector.details")}</h3>
          <button
            type="button"
            data-testid="inspector-open-external"
            data-action="nodes-open-external"
            onClick={openExternal}
          >
            {t("inspector.openExternal")}
          </button>
          <button
            type="button"
            data-testid="inspector-open-browser"
            data-action="nodes-open-browser"
            onClick={openInBrowser}
          >
            {t("inspector.openInBrowser")}
          </button>
        </section>
      </ViewState>
    </aside>
  );
}
