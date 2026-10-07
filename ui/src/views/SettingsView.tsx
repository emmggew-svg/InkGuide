import { getActiveClient } from "@core/bridge";
import { useAccountsStore, useAiStore, useSettingsStore } from "@core/stores";
import { useT } from "@core/useT";
import { ViewState } from "@views/ViewState";

export function SettingsView(): React.JSX.Element {
  const t = useT();
  const status = useSettingsStore((s) => s.status);
  const errorCode = useSettingsStore((s) => s.errorCode);
  const language = useSettingsStore((s) => s.language);
  const setLanguage = useSettingsStore((s) => s.setLanguage);
  const aiStatus = useAiStore((s) => s.aiStatus);
  const accounts = useAccountsStore((s) => s.accounts);

  const save = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("settings.update", { values: { language } });
    }
  };
  const reset = (): void => {
    const c = getActiveClient();
    if (c) {
      void c.invoke("settings.reset", {});
    }
  };

  return (
    <section className="settings" aria-label={t("settings.title")}>
      <h2>{t("settings.title")}</h2>
      <ViewState status={status} errorCode={errorCode} t={t} prefix="settings">
        <section aria-label={t("settings.general")}>
          <h3>{t("settings.general")}</h3>
          <label htmlFor="settings-lang">{t("common.language")}</label>
          <select
            id="settings-lang"
            data-testid="settings-language"
            data-action="settings-language"
            value={language}
            onChange={(e) => {
              setLanguage(e.target.value);
            }}
          >
            <option value="tr-TR">tr-TR</option>
            <option value="en-US">en-US</option>
          </select>
        </section>
        <section aria-label={t("settings.accounts")}>
          <h3>{t("settings.accounts")}</h3>
          <ul>
            {accounts.map((a) => (
              <li key={a.id} data-testid={`settings-account-${a.id}`} data-action="settings-account">
                {a.email}
              </li>
            ))}
          </ul>
        </section>
        <section aria-label={t("settings.storage")}>
          <h3>{t("settings.storage")}</h3>
          <p data-testid="settings-storage" data-action="settings-storage">
            {t("settings.storage")}
          </p>
        </section>
        <section aria-label={t("settings.sync")}>
          <h3>{t("settings.sync")}</h3>
          <p data-testid="settings-sync" data-action="settings-sync">
            {t("settings.sync")}
          </p>
        </section>
        <section aria-label={t("settings.analysis")}>
          <h3>{t("settings.analysis")}</h3>
          <p data-testid="settings-analysis" data-action="settings-analysis">
            {t("settings.analysis")}
          </p>
        </section>
        <section aria-label={t("settings.ai")}>
          <h3>{t("settings.ai")}</h3>
          <p data-testid="settings-ai-status" data-action="settings-ai-status">
            {aiStatus}
          </p>
        </section>
        <section aria-label={t("settings.advanced")}>
          <h3>{t("settings.advanced")}</h3>
          <p data-testid="settings-advanced" data-action="settings-advanced">
            {t("settings.advanced")}
          </p>
        </section>
        <button type="button" data-testid="settings-save" data-action="settings-save" onClick={save}>
          {t("settings.save")}
        </button>
        <button type="button" data-testid="settings-reset" data-action="settings-reset" onClick={reset}>
          {t("settings.reset")}
        </button>
      </ViewState>
    </section>
  );
}
