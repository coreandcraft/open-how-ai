import { accountBase } from "./account-helpers";
import { test, expect } from "@playwright/test";

test("English defaults, all locales switch live and persist without losing form data", async ({
  page,
}) => {
  await page.route("**/api/me", (route) =>
    route.fulfill({ status: 401, json: { error: "Unauthorized" } }),
  );
  await page.goto(accountBase + "/#login");
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await expect(
    page.getByRole("heading", { name: "Welcome to openhow" }),
  ).toBeVisible();
  await page.locator('input[type="email"]').fill("localization@example.test");
  for (const [code, heading] of [
    ["es", "Bienvenido a openhow"],
    ["ja", "openhowへようこそ"],
    ["zh-CN", "欢迎使用 openhow"],
    ["ru", "Добро пожаловать в openhow"],
  ]) {
    await page.locator(".language-picker select").selectOption(code);
    await expect(page.getByRole("heading", { name: heading })).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("lang", code);
    await expect(page.locator('input[type="email"]')).toHaveValue(
      "localization@example.test",
    );
  }
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("lang", "ru");
  await expect(
    page.getByRole("heading", { name: "Добро пожаловать в openhow" }),
  ).toBeVisible();
  await page.evaluate(() => localStorage.setItem("journey.locale", "invalid"));
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
});

test("localized library preserves source content and team role values", async ({
  page,
}) => {
  const workspace = {
    id: "localization",
    name: "Source workspace",
    teamId: "team",
    role: "Owner",
  };
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    const json =
      path === "/api/me"
        ? { id: "u", name: "Alex" }
        : path === "/api/workspaces"
          ? [workspace]
          : path.includes("/library")
            ? {
                items: [
                  {
                    id: "source",
                    title: "Original source 日本語 Русский",
                    provider: "ChatGPT",
                    messageCount: 21,
                    sourceFormat: "JSON",
                  },
                ],
                nextCursor: null,
              }
            : path.endsWith("/summary")
              ? {
                  conversations: 1,
                  journeys: 0,
                  providers: [{ provider: "ChatGPT", count: 1 }],
                }
            : path.endsWith("/journeys")
              ? { items: [], nextCursor: null }
            : path.endsWith("/members")
              ? [
                  {
                    id: "u",
                    name: "Alex",
                    email: "alex@example.test",
                    role: "Owner",
                  },
                ]
              : [];
    return route.fulfill({ json });
  });
  await page.goto(accountBase + "/#library");
  await page
    .getByRole("banner")
    .locator(".language-picker select")
    .selectOption("ru");
  await expect(
    page.getByRole("heading", { name: "Библиотека разговоров" }),
  ).toBeVisible();
  await expect(page.getByText("Original source 日本語 Русский")).toBeVisible();
  await expect(page.getByText("21 сообщение", { exact: true })).toBeVisible();
  await page.screenshot({
    path: "../../artifacts/localization-russian.png",
    fullPage: true,
  });
  await page.getByRole("link", { name: "Участники и доступ" }).click();
  const role = page.locator('select[aria-label="Роль участника Alex"]');
  await expect(role).toHaveValue("Owner");
  await expect(role.locator('option[value="Editor"]')).toHaveText("Редактор");
  await page
    .getByRole("banner")
    .locator(".language-picker select")
    .selectOption("ja");
  await expect(
    page.getByRole("heading", { name: "メンバーとアクセス" }),
  ).toBeVisible();
});

test("anonymous public pages offer the saved language", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("journey.locale", "es"));
  await page.goto(accountBase + "/#public/cloudplay-demo");
  await expect(page.locator(".public-page")).toBeVisible();
  await expect(page.locator(".language-picker select")).toHaveValue("es");
  await expect(
    page.getByRole("link", { name: "Abre tu espacio de trabajo ↗" }),
  ).toBeVisible();
  await page.locator(".language-picker select").selectOption("zh-CN");
  await expect(page.getByRole("link", { name: "打开工作区 ↗" })).toBeVisible();
});
