import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const srcRoot = resolve(here, "..");

function walk(dir: string, out: string[] = []): string[] {
  for (const e of readdirSync(dir)) {
    const p = join(dir, e);
    const st = statSync(p);
    if (st.isDirectory()) {
      walk(p, out);
    } else {
      out.push(p);
    }
  }
  return out;
}

describe("no-design contract", () => {
  it("no inline style attributes", () => {
    const files = walk(srcRoot).filter(
      (f) => (f.endsWith(".tsx") || f.endsWith(".ts")) && !f.includes("tests"),
    );
    const bad: string[] = [];
    for (const f of files) {
      const text = readFileSync(f, "utf8");
      // Dynamic geometry via style prop is forbidden here (none used);
      // every occurrence must be justified — we use none.
      if (/style\s*=\s*\{/.test(text) || /style\s*=\s*"/.test(text)) {
        bad.push(f);
      }
    }
    expect(bad).toEqual([]);
  });

  it("no color literals or font-family outside tokens.css, no UI kits", () => {
    const files = walk(srcRoot).filter(
      (f) => (f.endsWith(".tsx") || f.endsWith(".ts")) && !f.includes("tests"),
    );
    const badColor: string[] = [];
    const badKit: string[] = [];
    for (const f of files) {
      const text = readFileSync(f, "utf8");
      if (/#[0-9a-fA-F]{3,8}\b/.test(text) && !f.endsWith("tokens.css")) {
        // allow comments mentioning hex? none in ts/tsx expected
        badColor.push(`${f}:hex`);
      }
      if (/font-family/.test(text)) {
        badColor.push(`${f}:font-family`);
      }
      if (
        /from\s+["'](@mui|@chakra|@mantine|antd|bootstrap|tailwind|styled-components|@emotion)/.test(text)
      ) {
        badKit.push(f);
      }
      if (/from\s+["'](react-icons|lucide|@fortawesome)/.test(text)) {
        badKit.push(f);
      }
    }
    expect(badColor).toEqual([]);
    expect(badKit).toEqual([]);
  });

  it("tokens.css is placeholder-only, structure.css is layout-only", () => {
    const tokens = readFileSync(join(srcRoot, "theme/tokens.css"), "utf8");
    expect(tokens).toMatch(/PLACEHOLDER/);
    expect(tokens).toMatch(/system-ui/);
    const raw = readFileSync(join(srcRoot, "theme/structure.css"), "utf8");
    const structure = raw.replace(/\/\*[\s\S]*?\*\//g, "");
    const lower = structure.toLowerCase();
    for (const banned of ["color:", "font-family", "box-shadow", "border-radius", "transition", "animation", "gradient"]) {
      expect(lower.includes(banned), `structure.css must not contain ${banned}`).toBe(false);
    }
  });
});
