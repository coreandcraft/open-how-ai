import { accountBase } from "./account-helpers";
import { test, expect } from "@playwright/test";
const base = accountBase;

test("landing preview, navigation, registration, and browser history", async ({
  page,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.route("**/api/me", (route) =>
    route.fulfill({ status: 401, json: { error: "Unauthorized" } }),
  );
  await page.goto(base);
  await expect(page.getByRole("heading", { level: 1 })).toContainText(
    "Every conversation.",
  );
  await page.getByRole("button", { name: "The initial idea Markdown" }).click();
  await expect(page.locator(".oh-evidence")).toContainText(
    "Could we make a browser-based",
  );
  await expect(
    page.getByRole("button", { name: "The initial idea Markdown" }),
  ).toHaveAttribute("aria-pressed", "true");
  await page
    .locator(".oh-desktop-nav")
    .getByRole("button", { name: "How it works" })
    .click();
  await expect(page.locator("#how-it-works")).toBeInViewport();
  await page
    .locator(".oh-header-actions")
    .getByRole("link", { name: "Start your journey" })
    .click();
  await expect(
    page.getByRole("heading", { name: "Create your account" }),
  ).toBeVisible();
  await expect(page.getByLabel("Name", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Email", { exact: true })).toHaveValue("");
  await expect(page.getByLabel("Password", { exact: true })).toHaveValue("");
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Create your account" }),
  ).toBeVisible();
  await page.goBack();
  await expect(page.getByRole("heading", { level: 1 })).toContainText(
    "Every conversation.",
  );
  await page
    .locator(".oh-header-actions")
    .getByRole("link", { name: "Sign in", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "Welcome to openhow" }),
  ).toBeVisible();
  await page
    .locator(".login-story")
    .getByRole("link", { name: "openhow" })
    .click();
  await expect(page.locator(".oh-landing")).toBeVisible();
  await page
    .locator(".oh-header-actions")
    .getByRole("link", { name: "Sign in", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "Welcome to openhow" }),
  ).toBeVisible();
  await page.getByRole("link", { name: "Back to openhow" }).click();
  await expect(page.locator(".oh-landing")).toBeVisible();
  expect(errors).toEqual([]);
});

test("landing languages and mobile layout", async ({ page }) => {
  await page.route("**/api/me", (route) =>
    route.fulfill({ status: 401, json: { error: "Unauthorized" } }),
  );
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(base);
  await page.getByRole("button", { name: "Navigation menu" }).click();
  await expect(page.locator("#oh-mobile-nav")).toBeVisible();
  await page
    .locator("#oh-mobile-nav")
    .getByRole("button", { name: "How it works" })
    .click();
  await expect(page.locator("#oh-mobile-nav")).toHaveCount(0);
  for (const locale of ["es", "ja", "zh-CN", "ru", "en"]) {
    await page.locator(".language-picker select").selectOption(locale);
    await expect(page.locator("html")).toHaveAttribute("lang", locale);
    await expect(page.locator(".oh-landing")).not.toContainText("landing.");
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
  }
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({
    path: "../../artifacts/design/landing-mobile.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.screenshot({
    path: "../../artifacts/design/landing-desktop.png",
    fullPage: true,
  });
});

test("public demo opens without authentication", async ({ page }) => {
  await page.goto(base);
  await page
    .locator(".oh-hero-actions")
    .getByRole("link", { name: "Explore the demo" })
    .click();
  await expect(page).toHaveURL(/#public\/cloudplay-demo/);
  await expect(page.locator(".oh-landing")).toHaveCount(0);
  await expect(
    page.getByRole("heading", {
      name: "Building a browser-based cloud gaming platform",
      exact: true,
    }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Welcome to openhow" }),
  ).toHaveCount(0);
});

test("signed-in visitors can preview the landing and return to their workspace", async ({
  page,
}) => {
  await page.request.post(`${base}/api/auth/login`, {
    headers: { "X-Journey": "1" },
    data: { email: "alex@journey.local", password: "JourneyDemo!2026" },
  });
  await page.goto(`${base}/#landing`);
  await expect(page.locator(".oh-landing")).toBeVisible();
  await page
    .locator(".oh-header-actions")
    .getByRole("link", { name: "Sign in", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "Good to see you, Alex." }),
  ).toBeVisible();
  await page.goto(base);
  await expect(
    page.getByRole("heading", { name: "Good to see you, Alex." }),
  ).toBeVisible();
});
