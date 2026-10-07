import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@core": path.resolve(here, "src/core"),
      "@views": path.resolve(here, "src/views"),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/tests/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    coverage: {
      provider: "v8",
      reporter: ["text", "lcov"],
      include: ["src/core/**/*.ts"],
      exclude: [
        "src/core/fixtureBridge.ts",
        "src/core/fixtureBridge.prod-stub.ts",
        "src/**/*.test.{ts,tsx}",
      ],
      thresholds: {
        lines: 80,
        functions: 80,
        branches: 70,
        statements: 80,
      },
    },
  },
});
