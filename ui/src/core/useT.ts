import { useSettingsStore } from "@core/stores";
import enUS from "../i18n/en-US.json";
import trTR from "../i18n/tr-TR.json";

type Dict = Record<string, string>;

const DICTS: Record<string, Dict> = {
  "tr-TR": trTR,
  "en-US": enUS,
};

export function translateKey(lang: string, key: string): string {
  const dict = DICTS[lang] ?? DICTS["tr-TR"];
  const fallback = DICTS["en-US"];
  if (!dict || !fallback) {
    return key;
  }
  return dict[key] ?? fallback[key] ?? key;
}

export function useT(): (key: string) => string {
  const language = useSettingsStore((s) => s.language);
  return (key: string) => translateKey(language, key);
}
