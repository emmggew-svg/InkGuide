import type { SupportedLanguage } from "@core/appIdentity";

export type LocaleDict = Record<string, string>;

let cache = new Map<SupportedLanguage, LocaleDict>();

async function loadJson(lang: SupportedLanguage): Promise<LocaleDict> {
  const mod = (await import(`../i18n/${lang}.json`)) as unknown as { default: LocaleDict };
  return mod.default;
}

export async function loadLocale(lang: SupportedLanguage): Promise<LocaleDict> {
  const hit = cache.get(lang);
  if (hit) {
    return hit;
  }
  const dict = await loadJson(lang);
  cache.set(lang, dict);
  return dict;
}

export function getCachedLocale(lang: SupportedLanguage): LocaleDict | null {
  return cache.get(lang) ?? null;
}

export function clearLocaleCache(): void {
  cache = new Map();
}

export function translate(
  dict: LocaleDict,
  fallback: LocaleDict,
  key: string,
  vars?: Record<string, string | number>,
): string {
  const raw = dict[key] ?? fallback[key] ?? key;
  if (!vars) {
    return raw;
  }
  let out = raw;
  for (const [k, v] of Object.entries(vars)) {
    out = out.split(`{${k}}`).join(String(v));
  }
  return out;
}
