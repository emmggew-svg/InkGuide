// Single source of app identity for the UI layer.
// Never hard-code the name elsewhere (MASTER_PROMPT Sec 0.1).
export const APP_NAME = "InkGuide";
export const APP_ID = "inkguide.desktop";
export const DEFAULT_LANGUAGE = "tr-TR";
export const SUPPORTED_LANGUAGES = ["tr-TR", "en-US"] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];
export const APP_VERSION = "0.1.0";
