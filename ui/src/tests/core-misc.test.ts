import { describe, expect, it } from "vitest";
import { formatBytes, formatDate, formatNumber, formatPercent } from "@core/formatting";
import { SHORTCUTS, findShortcut } from "@core/shortcuts";
import { APP_ID, APP_NAME, DEFAULT_LANGUAGE } from "@core/appIdentity";
import { assertNever, BRIDGE_METHODS, isErrorCode } from "@core/contract";
import { loadLocale, translate, clearLocaleCache, getCachedLocale } from "@core/i18n";
import { translateKey } from "@core/useT";

describe("formatting", () => {
  it("formats bytes", () => {
    expect(formatBytes(0, "en-US")).toContain("0");
    expect(formatBytes(2048, "en-US")).toContain("KB");
    expect(formatBytes(-5, "en-US")).toBeDefined();
    expect(formatBytes(Number.NaN, "en-US")).toBeDefined();
  });
  it("formats dates and numbers", () => {
    expect(formatDate("2026-01-01T00:00:00Z", "en-US")).not.toBe("");
    expect(formatDate("nope", "en-US")).toBe("nope");
    expect(formatNumber(1234, "en-US")).not.toBe("");
    expect(formatPercent(0.5, "en-US")).not.toBe("");
  });
});

describe("shortcuts", () => {
  it("registry has entries", () => {
    expect(SHORTCUTS.length).toBeGreaterThan(0);
    expect(findShortcut("editor-save")?.keys).toBe("Ctrl+S");
    expect(findShortcut("missing")).toBeNull();
  });
});

describe("identity", () => {
  it("single source", () => {
    expect(APP_NAME).toBe("InkGuide");
    expect(APP_ID).toBe("inkguide.desktop");
    expect(DEFAULT_LANGUAGE).toBe("tr-TR");
  });
});

describe("contract helpers", () => {
  it("covers namespaces", () => {
    expect(BRIDGE_METHODS.length).toBeGreaterThan(100);
    expect(isErrorCode("NOT_FOUND")).toBe(true);
    expect(isErrorCode("NOPE")).toBe(false);
    expect(() => assertNever("x" as never)).toThrow();
  });
});

describe("i18n loader", () => {
  it("loads and translates", async () => {
    clearLocaleCache();
    const tr = await loadLocale("tr-TR");
    expect(tr["app.title"]).toBe("InkGuide");
    expect(getCachedLocale("tr-TR")).not.toBeNull();
    const again = await loadLocale("tr-TR");
    expect(again).toBe(tr);
    const en = await loadLocale("en-US");
    expect(translate(tr, en, "app.title")).toBe("InkGuide");
    expect(translate({}, en, "app.title")).toBe("InkGuide");
    expect(translate({}, {}, "missing.key")).toBe("missing.key");
    expect(translate({ k: "a {x}" }, {}, "k", { x: "b" })).toBe("a b");
  });
  it("translateKey falls back", () => {
    expect(translateKey("tr-TR", "app.title")).toBe("InkGuide");
    expect(translateKey("xx", "app.title")).toBe("InkGuide");
  });
});
