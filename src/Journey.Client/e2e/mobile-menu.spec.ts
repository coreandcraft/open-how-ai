import { test, expect } from "@playwright/test";
test.use({ viewport: { width: 390, height: 844 } });
test("mobile menu fits and closes after navigation and Escape", async ({
  page,
}) => {
  await page.request.post("http://127.0.0.1:5080/api/auth/login", {
    headers: { "X-Journey": "1" },
    data: { email: "alex@journey.local", password: "JourneyDemo!2026" },
  });
  await page.goto("http://127.0.0.1:5080/#dashboard");
  const toggle = page.getByRole("button", { name: "Open menu", exact: true });
  await toggle.click();
  const menu = page.getByRole("dialog", { name: "Navigation menu" });
  await expect(menu).toBeVisible();
  expect(
    await menu.evaluate((el) => el.scrollWidth <= el.clientWidth),
  ).toBeTruthy();
  expect(
    await page
      .locator(".topbar")
      .evaluate((el) => el.scrollWidth <= el.clientWidth),
  ).toBeTruthy();
  await expect(menu.getByLabel("Mobile workspace", { exact: true })).toBeVisible();
  await menu.getByRole("link", { name: "Workspaces", exact: true }).click();
  await expect(menu).not.toBeVisible();
  await expect(page).toHaveURL(/#workspaces$/);
  await toggle.click();
  await page.keyboard.press("Escape");
  await expect(menu).not.toBeVisible();
  await expect(toggle).toBeFocused();
});
