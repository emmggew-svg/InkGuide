import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { resolve, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "..");
const srcRoot = resolve(here, "..");

function walk(dir: string, out: string[] = []): string[] {
  for (const e of readdirSync(dir)) {
    const p = join(dir, e);
    const st = statSync(p);
    if (st.isDirectory()) {
      walk(p, out);
    } else if (p.endsWith(".ts") || p.endsWith(".tsx") || p.endsWith(".json")) {
      out.push(p);
    }
  }
  return out;
}

function allSourceText(): string {
  const files = walk(srcRoot).filter((f) => !f.includes("tests"));
  return files.map((f) => readFileSync(f, "utf8")).join("\n");
}

describe("i18n completeness", () => {
  it("tr-TR and en-US have identical keys", () => {
    const tr = JSON.parse(readFileSync(resolve(root, "i18n/tr-TR.json"), "utf8")) as Record<string, string>;
    const en = JSON.parse(readFileSync(resolve(root, "i18n/en-US.json"), "utf8")) as Record<string, string>;
    const tk = Object.keys(tr).sort();
    const ek = Object.keys(en).sort();
    expect(tk).toEqual(ek);
    expect(tk.length).toBeGreaterThan(50);
  });

  it("no missing keys", () => {
    const tr = JSON.parse(readFileSync(resolve(root, "i18n/tr-TR.json"), "utf8")) as Record<string, string>;
    const text = allSourceText();
    const re = /\bt\(\s*["'`]([^"'`]+)["'`]\s*\)/g;
    const used = new Set<string>();
    let m: RegExpExecArray | null;
    while ((m = re.exec(text)) !== null) {
      const k = m[1];
      if (k) {
        used.add(k);
      }
    }
    // label htmlFor/aria keys referenced via translateKey in tests are covered;
    // every literal t() key must exist (dynamic templates like error.${code}
    // are covered by the error.* family, skip ${} keys).
    for (const k of used) {
      if (k.includes("${")) {
        continue;
      }
      expect(tr[k], `missing i18n key: ${k}`).toBeDefined();
    }
    expect(used.size).toBeGreaterThan(20);
  });

  it("no unused keys", () => {
    const tr = JSON.parse(readFileSync(resolve(root, "i18n/tr-TR.json"), "utf8")) as Record<string, string>;
    const text = allSourceText();
    const missing: string[] = [];
    for (const k of Object.keys(tr)) {
      if (k.startsWith("error.")) {
        if (!text.includes("error.")) {
          missing.push(k);
        }
        continue;
      }
      if (!text.includes(k)) {
        missing.push(k);
      }
    }
    expect(missing).toEqual([]);
  });
});
