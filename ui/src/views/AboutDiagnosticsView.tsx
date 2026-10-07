import { useEffect, useState } from "react";
import { getActiveClient } from "@core/bridge";
import { APP_VERSION } from "@core/appIdentity";
import { useSettingsStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function AboutDiagnosticsView(): React.JSX.Element {
  const t = useT();
  const status = useSettingsStore((s) => s.status);
  const errorCode = useSettingsStore((s) => s.errorCode);
  const [health, setHealth] = useState("...");

  useEffect(() => {
    const c = getActiveClient();
    if (!c) {
      return;
    }
    void c
      .invoke<{ ok: boolean; details: string }>("diagnostics.getHealth", {})
      .then((h) => {
        setHealth(h.details);
      })
      .catch(() => {
        setHealth("error");
      });
  }, []);

  const exportBundle = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("diagnostics.exportBundle", {});
    }
  };

  return (
    <section className="about" aria-label={t("about.title")}>
      <h2>{t("about.title")}</h2>
      <ViewState status={status === "idle" ? "loading" : status} errorCode={errorCode} t={t} prefix="about">
        <section aria-label={t("about.diagnostics")}>
          <h3>{t("about.diagnostics")}</h3>
          <p data-testid="about-health" data-action="about-health">
            {health}
          </p>
          <p data-testid="about-version" data-action="about-version">
            {`${t("about.version")}: ${APP_VERSION}`}
          </p>
          <button
            type="button"
            data-testid="about-export"
            data-action="diagnostics-export"
            onClick={exportBundle}
          >
            {t("about.exportBundle")}
          </button>
        </section>
      </ViewState>
    </section>
  );
}
