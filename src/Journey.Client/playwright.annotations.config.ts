import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./annotation-tests",
  testMatch: "annotation-topics.spec.ts",
  use: { browserName: "chromium" },
  webServer: {
    command: "npx vite --host 127.0.0.1 --port 5188 --strictPort",
    url: "http://127.0.0.1:5188",
    reuseExistingServer: !process.env.CI,
  },
});
