import { useEffect } from "react";
import { getActiveClient } from "@core/bridge";
import { useTransfersStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function TransfersView(): React.JSX.Element {
  const t = useT();
  const status = useTransfersStore((s) => s.status);
  const errorCode = useTransfersStore((s) => s.errorCode);
  const jobs = useTransfersStore((s) => s.jobs);
  const load = useTransfersStore((s) => s.load);

  useEffect(() => {
    if (status === "idle") {
      void load();
    }
  }, [status, load]);

  const cmd = (method: "transfers.pause" | "transfers.resume" | "transfers.cancel" | "transfers.retry", id?: string): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke(method, id ? { id } : {});
    }
  };

  return (
    <section className="transfers" aria-label={t("transfers.title")}>
      <h2>{t("transfers.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="transfers">
        <button type="button" data-testid="transfers-refresh" data-action="transfers-refresh" onClick={() => { void load(); }}>
          {t("common.refresh")}
        </button>
        <button
          type="button"
          data-testid="transfers-upload"
          data-action="transfers-upload"
          onClick={() => {
            const c = getActiveClient();
            if (c) {
              void c.invoke("transfers.uploadPaths", { paths: [] });
            }
          }}
        >
          {t("transfers.upload")}
        </button>
        <button
          type="button"
          data-testid="transfers-download"
          data-action="transfers-download"
          onClick={() => {
            const c = getActiveClient();
            if (c) {
              void c.invoke("transfers.download", { id: jobs[0]?.id ?? "" });
            }
          }}
        >
          {t("transfers.download")}
        </button>
        <button
          type="button"
          data-testid="transfers-clear"
          data-action="transfers-clear"
          onClick={() => {
            const c = getActiveClient();
            if (c) {
              void c.invoke("transfers.clearFinished", {});
            }
          }}
        >
          {t("transfers.clear")}
        </button>
        <ul className="transfers__list">
          {jobs.map((j) => (
            <li key={j.id}>
              <span data-testid={`transfers-job-${j.id}`} data-action="transfers-job">
                {`${j.name}: ${j.state}`}
              </span>
              <button type="button" data-testid={`transfers-pause-${j.id}`} data-action="transfers-pause" onClick={() => { cmd("transfers.pause", j.id); }}>
                {t("transfers.pause")}
              </button>
              <button type="button" data-testid={`transfers-resume-${j.id}`} data-action="transfers-resume" onClick={() => { cmd("transfers.resume", j.id); }}>
                {t("transfers.resume")}
              </button>
              <button type="button" data-testid={`transfers-cancel-${j.id}`} data-action="transfers-cancel" onClick={() => { cmd("transfers.cancel", j.id); }}>
                {t("transfers.cancel")}
              </button>
              <button type="button" data-testid={`transfers-retry-${j.id}`} data-action="transfers-retry" onClick={() => { cmd("transfers.retry", j.id); }}>
                {t("transfers.retry")}
              </button>
            </li>
          ))}
        </ul>
      </ViewState>
    </section>
  );
}
