import type { ReactNode } from "react";
import type { ErrorCode } from "@core/contract";
import type { StoreStatus } from "@core/stores";

interface Props {
  status: StoreStatus;
  errorCode: ErrorCode | null;
  t: (key: string) => string;
  prefix: string;
  emptyText?: string;
  children: ReactNode;
}

// Shared wireframe state renderer. Every reference view uses it so all
// required states (loading/empty/error-by-code/offline/reauth/not-configured/
// denied) are reachable and testable via data-testid hooks.
export function ViewState({ status, errorCode, t, prefix, emptyText, children }: Props): ReactNode {
  if (status === "idle") {
    return (
      <p data-testid={`${prefix}-idle`} data-action={`${prefix}-idle`}>
        {t("common.idle")}
      </p>
    );
  }
  if (status === "loading") {
    return (
      <p data-testid={`${prefix}-loading`} data-action={`${prefix}-loading`}>
        {t("common.loading")}
      </p>
    );
  }
  if (status === "empty") {
    return (
      <p data-testid={`${prefix}-empty`} data-action={`${prefix}-empty`}>
        {emptyText ?? t("common.empty")}
      </p>
    );
  }
  if (status === "offline") {
    return (
      <p data-testid={`${prefix}-offline`} data-action={`${prefix}-offline`}>
        {t("common.offline")}
      </p>
    );
  }
  if (status === "reauth") {
    return (
      <p data-testid={`${prefix}-reauth`} data-action={`${prefix}-reauth`}>
        {t("common.reauth")}
      </p>
    );
  }
  if (status === "not-configured") {
    return (
      <p data-testid={`${prefix}-not-configured`} data-action={`${prefix}-not-configured`}>
        {t("common.notConfigured")}
      </p>
    );
  }
  if (status === "denied") {
    return (
      <p data-testid={`${prefix}-denied`} data-action={`${prefix}-denied`}>
        {t("common.denied")}
      </p>
    );
  }
  if (status === "error") {
    const code = errorCode ?? "UNKNOWN";
    return (
      <p data-testid={`${prefix}-error`} data-action={`${prefix}-error`}>
        {`${t("common.error")}: ${t(`error.${code}`)}`}
      </p>
    );
  }
  return <>{children}</>;
}
