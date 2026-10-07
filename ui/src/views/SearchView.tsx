import { useSearchStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function SearchView(): React.JSX.Element {
  const t = useT();
  const status = useSearchStore((s) => s.status);
  const errorCode = useSearchStore((s) => s.errorCode);
  const query = useSearchStore((s) => s.query);
  const results = useSearchStore((s) => s.results);
  const setQuery = useSearchStore((s) => s.setQuery);
  const run = useSearchStore((s) => s.run);

  return (
    <section className="search" aria-label={t("search.title")}>
      <h2>{t("search.title")}</h2>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void run();
        }}
      >
        <label htmlFor="search-q">{t("search.placeholder")}</label>
        <input
          id="search-q"
          data-testid="search-input"
          data-action="search-input"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
          }}
        />
        <button type="submit" data-testid="search-run" data-action="search-run">
          {t("common.search")}
        </button>
      </form>
      <section aria-label={t("search.filters")}>
        <h3>{t("search.filters")}</h3>
        <ViewState status={status} errorCode={errorCode} t={t} prefix="search">
          <ul className="search__list">
            {results.map((r) => (
              <li key={r.nodeId} data-testid={`search-item-${r.nodeId}`} data-action="search-open">
                {r.name}
              </li>
            ))}
          </ul>
        </ViewState>
      </section>
    </section>
  );
}
