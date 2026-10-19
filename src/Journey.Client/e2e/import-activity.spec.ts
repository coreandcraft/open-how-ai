import { test, expect } from "@playwright/test";
const base = "http://127.0.0.1:5080";

for (const reducedMotion of ["no-preference", "reduce"] as const) {
  test(`slow source previews animate with ${reducedMotion} motion`, async ({
    page,
  }) => {
    await page.emulateMedia({ reducedMotion });
    await page.request.post(`${base}/api/auth/login`, {
      headers: { "X-Journey": "1" },
      data: { email: "alex@journey.local", password: "JourneyDemo!2026" },
    });
    await page.addInitScript(() =>
      localStorage.setItem("journey.locale", "en"),
    );
    let finish!: () => void;
    const pending = new Promise<void>((resolve) => {
      finish = resolve;
    });
    let processed = 2;
    await page.route("**/imports/progress/*", (route) =>
      route.fulfill({
        json: {
          stage: "processing",
          processed,
          total: 5,
          skipped: 1,
          compressedBytes: 1_200_000_000,
          uncompressedBytes: 8_400_000_000,
          usedBytes: 9_600_000_000,
          limitBytes: 15_000_000_000,
        },
      }),
    );
    await page.route("**/imports/preview?*", async (route) => {
      await pending;
      await route.fulfill({
        json: {
          batchId: "preview",
          conversations: [],
          projects: [],
          errors: [],
        },
      });
    });
    await page.goto(`${base}/#library`);
    await page
      .getByRole("button", { name: "Import conversations", exact: true })
      .click();
    await expect(
      page.getByRole("button", { name: "Choose files", exact: true }),
    ).toBeEnabled();
    await page
      .locator('input[type="file"]')
      .first()
      .setInputFiles({
        name: "conversation.json",
        mimeType: "application/json",
        buffer: Buffer.from("{}"),
      });
    const status = page
      .getByRole("status")
      .filter({ hasText: "Reading your sources…" });
    await expect(status).toBeVisible();
    await expect(status).toContainText("2 of 5 files processed");
    await expect(status).toContainText("Compressed: 1.2 GB");
    await expect(status).toContainText("Uncompressed: 8.4 GB");
    await expect(status).toContainText(
      "9.6 GB of 15 GB toward your import limit",
    );
    processed = 3;
    await expect(status).toContainText("3 of 5 files processed");
    await expect(status).toContainText("1 files skipped");
    await expect(status.locator("svg")).toHaveCSS(
      "animation-name",
      "import-activity-spin",
    );
    const initialTransform = await status
      .locator("svg")
      .evaluate((el) => getComputedStyle(el).transform);
    await expect
      .poll(() =>
        status.locator("svg").evaluate((el) => getComputedStyle(el).transform),
      )
      .not.toBe(initialTransform);
    await expect(status).toContainText(/Elapsed: [1-9]\d* seconds/);
    await expect(
      page.getByRole("button", { name: "Choose files", exact: true }),
    ).toBeDisabled();
    finish();
    await expect(status).toHaveCount(0);
  });
}

test("import step shows a progress indicator", async ({ page }) => {
  await page.request.post(`${base}/api/auth/login`, {
    headers: { "X-Journey": "1" },
    data: { email: "alex@journey.local", password: "JourneyDemo!2026" },
  });
  await page.addInitScript(() => localStorage.setItem("journey.locale", "en"));
  const sizes = {
    stage: "ready",
    processed: 1,
    total: 1,
    skipped: 0,
    compressedBytes: 1_200_000_000,
    uncompressedBytes: 8_400_000_000,
    usedBytes: 9_600_000_000,
    limitBytes: 15_000_000_000,
  };
  await page.route("**/imports/progress/*", (route) =>
    route.fulfill({ json: sizes }),
  );
  await page.route("**/imports/preview?*", (route) =>
    route.fulfill({
      json: {
        batchId: "batch-1",
        conversations: [
          {
            key: "c1",
            title: "Hello",
            provider: "ChatGPT",
            messageCount: 1,
            messages: [],
            duplicate: false,
          },
        ],
        projects: [],
        errors: [],
        fileProgress: sizes,
      },
    }),
  );
  let finishImport = false;
  await page.route("**/api/imports/batch-1**", async (route) => {
    if (route.request().url().includes("/commit")) {
      await route.fulfill({ json: { status: "queued" } });
      return;
    }
    await route.fulfill({
      json: finishImport
        ? {
            status: "completed",
            total: 1,
            imported: 1,
            duplicates: 0,
            updated: 0,
            conflicts: 0,
            error: "",
            fileProgress: {
              stage: "completed",
              processed: 1,
              total: 1,
              skipped: 0,
            },
          }
        : {
            status: "processing",
            total: 1,
            imported: 0,
            duplicates: 0,
            updated: 0,
            conflicts: 0,
            error: "",
            fileProgress: {
              stage: "processing",
              processed: 0,
              total: 1,
              skipped: 0,
            },
          },
    });
  });
  await page.goto(`${base}/#library`);
  await page
    .getByRole("button", { name: "Import conversations", exact: true })
    .click();
  await expect(
    page.getByRole("button", { name: "Choose files", exact: true }),
  ).toBeEnabled();
  await page
    .locator('input[type="file"]')
    .first()
    .setInputFiles({
      name: "conversation.json",
      mimeType: "application/json",
      buffer: Buffer.from("{}"),
    });
  await expect(
    page.getByRole("button", { name: "Import selected" }),
  ).toBeEnabled();
  await expect(page.getByText("Compressed: 1.2 GB")).toBeVisible();
  await page.getByRole("button", { name: "Import selected" }).click();
  const status = page.getByRole("status").filter({
    hasText: "Importing conversations…",
  });
  await expect(status).toBeVisible();
  await expect(status).toContainText("0 of 1 conversations");
  await expect(status).toContainText("Uncompressed: 8.4 GB");
  await expect(status).toContainText(
    "9.6 GB of 15 GB toward your import limit",
  );
  await expect(status.getByRole("progressbar").first()).toBeVisible();
  await expect(status).toContainText(/Elapsed: \d+ seconds/);
  finishImport = true;
  await expect(
    page.getByRole("heading", { name: "Import complete" }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Back to workspace", exact: true }),
  ).toBeEnabled();
});
