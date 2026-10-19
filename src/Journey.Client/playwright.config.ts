import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e",
  use: { browserName: "chromium", viewport: { width: 1440, height: 1000 } },
  timeout: 60000,
  reporter: "list",
});
