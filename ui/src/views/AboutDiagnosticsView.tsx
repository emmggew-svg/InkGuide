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
  const loadSettings = useSettingsStore((s) => s.load);
  const [health, setHealth] = useState("...");

  useEffect(() => {
    if (status === "idle") {
      void loadSettings();
    }
  }, [status, loadSettings]);
  useEffect(() => {
    const c = getActiveClient();
    if (!c) {
      return;
    }
    void c
      .invoke<{ state: string; checks: Record<string, string> }>("diagnostics.getHealth", {})
      .then((h) => {
        setHealth(`${h.state}: ${Object.entries(h.checks ?? {}).map(([k, v]) => `${k}=${v}`).join(", ")}`);
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
      <ViewState status={status} errorCode={errorCode} t={t} prefix="about">
        <button type="button" data-testid="about-refresh" data-action="about-refresh" onClick={() => { void loadSettings(); }}>
          {t("common.refresh")}
        </button>
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
