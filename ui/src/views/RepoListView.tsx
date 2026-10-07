import { useState } from "react";
import { getActiveClient } from "@core/bridge";
import { useReposStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function RepoListView(): React.JSX.Element {
  const t = useT();
  const status = useReposStore((s) => s.status);
  const errorCode = useReposStore((s) => s.errorCode);
  const repos = useReposStore((s) => s.repos);
  const [name, setName] = useState("");

  const create = (): void => {
    const c = getActiveClient();
    if (c && name.trim() !== "") {
      void c.invoke("repos.create", { name: name.trim(), kind: "single" });
    }
  };

  return (
    <section className="repos" aria-label={t("repos.title")}>
      <h2>{t("repos.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="repos" emptyText={t("repos.empty")}>
        <form
          className="repos__form"
          onSubmit={(e) => {
            e.preventDefault();
            create();
          }}
        >
          <label htmlFor="repos-name">{t("repos.nameLabel")}</label>
          <input
            id="repos-name"
            data-testid="repos-name-input"
            data-action="repos-name-input"
            value={name}
            onChange={(e) => {
              setName(e.target.value);
            }}
          />
          <button type="submit" data-testid="repos-create" data-action="repos-create">
            {t("repos.create")}
          </button>
        </form>
        <ul className="repos__list">
          {repos.map((r) => (
            <li key={r.id} className="repos__row">
              <span data-testid={`repos-item-${r.id}`} data-action="repos-open">
                {r.name}
              </span>
            </li>
          ))}
        </ul>
      </ViewState>
    </section>
  );
}
