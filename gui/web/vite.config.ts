/// <reference types="vitest/config" />
import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// The build lands in dist/, which the GUI project copies to wwwroot/ next to the exe; the window loads it from a
// file or app scheme, so every asset path is relative (`base: "./"`).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: "./",
  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "./src"),
    },
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: false,
  },
  test: {
    environment: "node",
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    // `npm run test:coverage` (task 070): every source file under src/, held at 100% by the thresholds; the
    // report is the text table in the log and coverage/ (lcov, json-summary), CI's artifact.
    coverage: {
      provider: "v8",
      include: ["src/**/*.{ts,tsx}"],
      exclude: [
        // The tests themselves, and their helpers (the DOM steps, the scripted transport).
        "src/**/*.test.{ts,tsx}",
        "src/test/**",
      ],
      reporter: ["text", "text-summary", "lcov", "json-summary"],
      reportsDirectory: "coverage",
      thresholds: { lines: 100, branches: 100, functions: 100, statements: 100 },
    },
  },
});
