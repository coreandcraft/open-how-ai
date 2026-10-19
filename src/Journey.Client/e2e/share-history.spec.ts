import { test, expect } from "@playwright/test";
test("copy and revoke an existing shared link before creating another", async ({
  page,
}) => {
  await page.request.post("http://127.0.0.1:5080/api/auth/login", {
    headers: { "X-Journey": "1" },
    data: { email: "alex@journey.local", password: "JourneyDemo!2026" },
  });
  const published = await page.request.post(
    "http://127.0.0.1:5080/api/conversations/demo-c9/publish",
    {
      headers: { "X-Journey": "1" },
      data: {
        messageIds: ["demo-m9-0"],
        redactions: [],
        visibility: "Unlisted",
        confirm: true,
      },
    },
  );
  expect(published.ok()).toBeTruthy();
  const { slug } = await published.json();
  await page.goto("http://127.0.0.1:5080/#explorer/demo-c9");
  await page
    .getByRole("button", { name: "Share conversation", exact: true })
    .click();
  const row = page
    .locator(".share-history-row")
    .filter({ has: page.locator('a[href="#public/' + slug + '"]') });
  await expect(row).toBeVisible();
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await row.getByRole("button", { name: "Copy link", exact: true }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain(
    "#public/" + slug,
  );
  await row.getByRole("button", { name: "Delete link", exact: true }).click();
  await row
    .getByRole("button", { name: "Confirm delete", exact: true })
    .click();
  await expect(row).toHaveCount(0);
  expect(
    (
      await page.request.get("http://127.0.0.1:5080/api/public/" + slug)
    ).status(),
  ).toBe(404);
  await expect(
    page.getByRole("heading", { name: "Create a new shared link" }),
  ).toBeVisible();
});
