import { useEffect, useState } from "react";
import { getActiveClient } from "@core/bridge";
import { useTagsStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function TagsView(): React.JSX.Element {
  const t = useT();
  const status = useTagsStore((s) => s.status);
  const errorCode = useTagsStore((s) => s.errorCode);
  const tags = useTagsStore((s) => s.tags);
  const suggestions = useTagsStore((s) => s.suggestions);
  const load = useTagsStore((s) => s.load);
  const [name, setName] = useState("");

  useEffect(() => {
    if (status === "idle") {
      void load();
    }
  }, [status, load]);

  const create = (): void => {
    const c = getActiveClient();
    if (c && name.trim() !== "") {
      void c.invoke("tags.create", { name: name.trim() });
    }
  };
  const accept = (id: string): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("tags.acceptSuggestion", { id });
    }
  };
  const dismiss = (id: string): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("tags.dismissSuggestion", { id });
    }
  };

  return (
    <section className="tags" aria-label={t("tags.title")}>
      <h2>{t("tags.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="tags">
        <button type="button" data-testid="tags-refresh" data-action="tags-refresh" onClick={() => { void load(); }}>
          {t("common.refresh")}
        </button>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            create();
          }}
        >
          <label htmlFor="tags-name">{t("tags.create")}</label>
          <input
            id="tags-name"
            data-testid="tags-name-input"
            data-action="tags-name-input"
            value={name}
            onChange={(e) => {
              setName(e.target.value);
            }}
          />
          <button type="submit" data-testid="tags-create" data-action="tags-create">
            {t("tags.create")}
          </button>
        </form>
        <ul className="tags__list">
          {tags.map((x) => (
            <li key={x.id}>
              <span data-testid={`tags-item-${x.id}`} data-action="tags-item">
                {x.name}
              </span>
              <button
                type="button"
                data-testid={`tags-assign-${x.id}`}
                data-action="tags-assign"
                onClick={() => {
                  const c = getActiveClient();
                  if (c) {
                    void c.invoke("tags.assign", { id: x.id });
                  }
                }}
              >
                {t("tags.assign")}
              </button>
            </li>
          ))}
        </ul>
        <section aria-label={t("tags.suggestions")}>
          <h3>{t("tags.suggestions")}</h3>
          <ul>
            {suggestions.map((s) => (
              <li key={s.id}>
                <span data-testid={`tags-suggestion-${s.id}`} data-action="tags-suggestion">
                  {s.name}
                </span>
                <button
                  type="button"
                  data-testid={`tags-accept-${s.id}`}
                  data-action="tags-accept"
                  onClick={() => {
                    accept(s.id);
                  }}
                >
                  {t("tags.accept")}
                </button>
                <button
                  type="button"
                  data-testid={`tags-dismiss-${s.id}`}
                  data-action="tags-dismiss"
                  onClick={() => {
                    dismiss(s.id);
                  }}
                >
                  {t("tags.dismiss")}
                </button>
              </li>
            ))}
          </ul>
        </section>
      </ViewState>
    </section>
  );
}
