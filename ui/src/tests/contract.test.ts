import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { BRIDGE_METHODS, CONTRACT_MAJOR, CONTRACT_MINOR } from "@core/contract";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "../..");

describe("contract freshness", () => {
  it("manifest matches contract version", () => {
    const manifest = JSON.parse(readFileSync(resolve(root, "ui-manifest.json"), "utf8")) as {
      contract: string;
      entry: string;
    };
    expect(manifest.contract).toBe(`${String(CONTRACT_MAJOR)}.${String(CONTRACT_MINOR)}`);
    expect(manifest.entry).toBe("index.html");
  });
  it("method list covers all namespaces", () => {
    const namespaces = [
      "app.", "window.", "settings.", "accounts.", "workspace.", "repos.", "projects.",
      "nodes.", "transfers.", "import.", "insight.", "tags.", "search.", "sync.",
      "snapshots.", "activity.", "organize.", "duplicates.", "external.", "ai.",
      "storage.", "rules.", "diagnostics.",
    ];
    for (const ns of namespaces) {
      expect(BRIDGE_METHODS.some((m) => m.startsWith(ns)), `missing ${ns}`).toBe(true);
    }
  });
});

describe("fixture leak guard", () => {
  it("fixture is DEV ONLY and prod alias excludes it", () => {
    const fixture = readFileSync(resolve(root, "src/core/fixtureBridge.ts"), "utf8");
    expect(fixture).toMatch(/DEV ONLY/);
    expect(fixture).toMatch(/FIXTURE_DEV_ONLY_MARKER/);
    const vite = readFileSync(resolve(root, "vite.config.ts"), "utf8");
    expect(vite).toMatch(/fixtureBridge\.prod-stub/);
    const main = readFileSync(resolve(root, "src/main.tsx"), "utf8");
    expect(main).toMatch(/bridge=fixture/);
  });
});
