import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const isProd = process.env["NODE_ENV"] === "production";

// In production the fixture bridge is aliased to a stub so no fixture
// strings can leak into the shipped bundle. Dev uses the real in-memory
// implementation via dynamic import guarded by import.meta.env.DEV.
export default defineConfig({
  base: "./",
  plugins: [react()],
  resolve: {
    alias: isProd
      ? {
          "@core": path.resolve(here, "src/core"),
          "@views": path.resolve(here, "src/views"),
          "@core/fixtureBridge": path.resolve(
            here,
            "src/core/fixtureBridge.prod-stub.ts",
          ),
        }
      : {
          "@core": path.resolve(here, "src/core"),
          "@views": path.resolve(here, "src/views"),
        },
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: false,
    target: "es2022",
  },
});
