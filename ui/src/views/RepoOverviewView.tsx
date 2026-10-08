import { useEffect } from "react";
import { useReposStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function RepoOverviewView(): React.JSX.Element {
  const t = useT();
  const status = useReposStore((s) => s.status);
  const errorCode = useReposStore((s) => s.errorCode);
  const overview = useReposStore((s) => s.overview);
  const repos = useReposStore((s) => s.repos);
  const load = useReposStore((s) => s.load);
  const loadOverview = useReposStore((s) => s.loadOverview);

  useEffect(() => {
    if (status === "idle") {
      void load();
    }
  }, [status, load]);
  useEffect(() => {
    if (overview === null && repos.length > 0) {
      const first = repos[0];
      if (first) {
        void loadOverview(first.id);
      }
    }
  }, [overview, repos, loadOverview]);

  return (
    <section className="overview" aria-label={t("overview.title")}>
      <h2>{t("overview.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="overview">
        <button type="button" data-testid="overview-refresh" data-action="overview-refresh" onClick={() => { void load(); }}>
          {t("common.refresh")}
        </button>
        <section className="overview__stats" aria-label={t("overview.stats")}>
          <h3>{t("overview.stats")}</h3>
          <p data-testid="overview-stats" data-action="overview-stats">
            {overview ? `${String(overview.totalFiles)} / ${String(overview.totalBytes)}` : t("common.empty")}
          </p>
        </section>
        <section className="overview__readme" aria-label={t("overview.readme")}>
          <h3>{t("overview.readme")}</h3>
          <p data-testid="overview-readme" data-action="overview-readme">
            {overview?.readmeExcerpt ?? t("common.empty")}
          </p>
        </section>
        <section className="overview__projects" aria-label={t("overview.projects")}>
          <h3>{t("overview.projects")}</h3>
          <ul className="overview__list">
            {(overview?.projects ?? []).map((p) => (
              <li key={p.id} data-testid={`overview-project-${p.id}`} data-action="overview-project">
                {p.path}
              </li>
            ))}
          </ul>
        </section>
      </ViewState>
    </section>
  );
}
