import { useEffect } from "react";
import { useAccountsStore } from "@core/stores";
import { useT } from "@core/useT";
import { getActiveClient } from "@core/bridge";
import { ViewState } from "@views/ViewState";

export function OnboardingView(): React.JSX.Element {
  const t = useT();
  const status = useAccountsStore((s) => s.status);
  const errorCode = useAccountsStore((s) => s.errorCode);
  const accounts = useAccountsStore((s) => s.accounts);
  const load = useAccountsStore((s) => s.load);

  useEffect(() => {
    if (status === "idle") {
      void load();
    }
  }, [status, load]);

  const connect = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("accounts.connect", {});
    }
  };
  const disconnect = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("accounts.disconnect", {});
    }
  };
  const importClient = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("accounts.importOAuthClient", {});
    }
  };

  return (
    <section className="onboarding" aria-label={t("onboarding.title")}>
      <h2>{t("onboarding.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="onboarding">
        <button
          type="button"
          data-testid="onboarding-refresh"
          data-action="onboarding-refresh"
          onClick={() => {
            void load();
          }}
        >
          {t("common.refresh")}
        </button>
        <p data-testid="onboarding-desc" data-action="onboarding-desc">
          {t("onboarding.description")}
        </p>
        {errorCode === "OAUTH_NOT_CONFIGURED" ? (
          <p data-testid="onboarding-oauth-missing" data-action="onboarding-oauth-missing">
            {t("onboarding.oauthNotConfigured")}
          </p>
        ) : null}
        <ul className="onboarding__list">
          {accounts.map((a) => (
            <li key={a.id} className="onboarding__row">
              <span data-testid={`onboarding-account-${a.id}`} data-action="onboarding-account">
                {a.email}
              </span>
            </li>
          ))}
        </ul>
        <button
          type="button"
          data-testid="onboarding-connect"
          data-action="accounts-connect"
          onClick={connect}
        >
          {t("onboarding.connect")}
        </button>
        <button
          type="button"
          data-testid="onboarding-disconnect"
          data-action="accounts-disconnect"
          onClick={disconnect}
        >
          {t("onboarding.disconnect")}
        </button>
        <button
          type="button"
          data-testid="onboarding-import-client"
          data-action="accounts-import-client"
          onClick={importClient}
        >
          {t("onboarding.importClient")}
        </button>
      </ViewState>
    </section>
  );
}
