import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./message-tests",
  outputDir: "../../artifacts/message-tests",
  use: { browserName: "chromium", baseURL: "http://127.0.0.1:5191" },
  timeout: 30_000,
  reporter: "list",
  webServer: {
    command:
      "node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5191 --strictPort",
    url: "http://127.0.0.1:5191",
    reuseExistingServer: !process.env.CI,
  },
});
