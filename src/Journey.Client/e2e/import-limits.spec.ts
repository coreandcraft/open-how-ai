import { test, expect } from "@playwright/test";
const base = "http://127.0.0.1:5080";

for (const gb of [15, 20]) {
  test(`import dialog displays the server-authorized ${gb} GB tier`, async ({
    page,
  }) => {
    await page.request.post(`${base}/api/auth/login`, {
      headers: { "X-Journey": "1" },
      data: { email: "alex@journey.local", password: "JourneyDemo!2026" },
    });
    await page.route("**/api/imports/limits", (route) =>
      route.fulfill({ json: { maxBytes: gb * 1_000_000_000, maxFiles: 500 } }),
    );
    await page.goto(`${base}/#library`);
    await page
      .getByRole("button", { name: "Import conversations", exact: true })
      .click();
    await expect(
      page.getByText(
        `Up to ${gb} GB per import, including extracted archive contents.`,
        { exact: true },
      ),
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Choose files", exact: true }),
    ).toBeEnabled();
    let uploads = 0;
    await page.route("**/imports/preview?*", (route) => {
      uploads++;
      return route.fulfill({
        json: { conversations: [], projects: [], batchId: "test" },
      });
    });
    await page
      .locator('input[type="file"]')
      .first()
      .evaluate((input, limit) => {
        const file = new File(["{}"], "too-large.json", {
          type: "application/json",
        });
        Object.defineProperty(file, "size", { value: limit + 1 });
        const transfer = new DataTransfer();
        transfer.items.add(file);
        (input as HTMLInputElement).files = transfer.files;
        // Shadow only the File object's size; no multi-gigabyte allocation/upload.
        Object.defineProperty((input as HTMLInputElement).files![0], "size", {
          value: limit + 1,
        });
        input.dispatchEvent(new Event("change", { bubbles: true }));
      }, gb * 1_000_000_000);
    await expect(
      page.getByText(`Conversation import exceeds your ${gb} GB limit.`, {
        exact: true,
      }),
    ).toBeVisible();
    expect(uploads).toBe(0);
  });
}
