import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "..");
const manifest = JSON.parse(readFileSync(resolve(root, "ui-manifest.json"), "utf8"));
const contractSrc = readFileSync(resolve(root, "src/core/contract.ts"), "utf8");

const major = /CONTRACT_MAJOR\s*=\s*(\d+)/.exec(contractSrc)?.[1];
const minor = /CONTRACT_MINOR\s*=\s*(\d+)/.exec(contractSrc)?.[1];
if (!major || !minor) {
  console.error("contract:check: CONTRACT_MAJOR/MINOR not found in contract.ts");
  process.exit(1);
}
const expected = `${major}.${minor}`;
if (manifest.contract !== expected) {
  console.error(`contract:check FAILED: ui-manifest contract ${manifest.contract} != ${expected}`);
  process.exit(1);
}
if (!manifest.entry || !manifest.name || !manifest.version) {
  console.error("contract:check FAILED: ui-manifest missing name/version/entry");
  process.exit(1);
}
console.log(`contract:check OK contract=${expected}`);
