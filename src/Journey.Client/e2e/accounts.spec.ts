import { test, expect } from "@playwright/test";
import { accountBase as base, emailContent } from "./account-helpers";
import { createHmac } from "node:crypto";

function totp(key: string) {
  let bits = "";
  for (const c of key)
    bits += "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
      .indexOf(c)
      .toString(2)
      .padStart(5, "0");
  const bytes = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const hash = createHmac("sha1", bytes).update(counter).digest();
  return ((hash.readUInt32BE(hash[19] & 15) & 0x7fffffff) % 1000000)
    .toString()
    .padStart(6, "0");
}

test("register, verify by email, enable MFA, recover login and reset password", async ({
  page,
}) => {
  const email = `browser-${Date.now()}@example.test`;
  const password = "BrowserAccount!2026";
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto(base + "/#register");
  await page.getByLabel("Name", { exact: true }).fill("Browser tester");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page
    .getByRole("button", { name: "Create account", exact: true })
    .click();
  await expect(page.getByRole("status")).toContainText("Check your inbox");
  expect((await page.request.get(base + "/api/me")).status()).toBe(401);
  const confirmation = await emailContent(email);
  await page.goto(confirmation.link);
  await page
    .getByRole("button", { name: "Confirm email", exact: true })
    .click();
  await expect(page.getByRole("status")).toContainText("Email confirmed");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(page.locator(".sidebar")).toBeVisible();
  await page.goto(base + "/#settings");
  await page.getByLabel("Current password").fill(password);
  await page.getByRole("button", { name: "Set up authenticator" }).click();
  const key = (await page.locator(".authenticator-setup code").textContent())!;
  await expect(
    page.getByRole("img", { name: "Authenticator setup QR code" }),
  ).toBeVisible();
  await page.getByLabel("Authenticator code").fill(totp(key));
  await page.getByRole("button", { name: "Enable MFA", exact: true }).click();
  await expect(page.locator(".recovery-codes")).toBeVisible();
  await expect(page.locator(".authenticator-qr")).toHaveCount(0);
  const recovery = (await page
    .locator(".recovery-codes pre")
    .textContent())!.split("\n")[0];
  await page.getByRole("button", { name: "I've saved my codes" }).click();
  await page.screenshot({
    path: "../../artifacts/account-security.png",
    fullPage: true,
  });
  await page.request.post(base + "/api/logout", {
    headers: { "X-Journey": "1" },
    data: {},
  });
  await page.goto(base + "/#login");
  await page.reload();
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(
    page.getByRole("heading", { name: "Two-step verification", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Use a recovery code" }).click();
  await page.getByLabel("Recovery code", { exact: true }).fill(recovery);
  await page.getByRole("button", { name: "Verify and sign in" }).click();
  await expect(page.locator(".sidebar")).toBeVisible();
  await page.request.post(base + "/api/logout", {
    headers: { "X-Journey": "1" },
    data: {},
  });
  await page.goto(base + "/#login");
  await page.reload();
  await page.getByRole("button", { name: "Forgot your password?" }).click();
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByRole("button", { name: "Send email", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Check your inbox");
  const reset = await emailContent(email, "Reset password");
  await page.goto(reset.link);
  await page
    .getByLabel("Password", { exact: true })
    .fill("NewBrowserPassword!2026");
  await page.getByRole("button", { name: "Save password" }).click();
  await expect(page.getByRole("status")).toContainText("Password updated");
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({
    path: "../../artifacts/account-login-mobile.png",
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
  await page.setContent(
    confirmation.html
      .replaceAll(/href="[^"]*"/g, 'href="#"')
      .replace(/http[^<\s]+/g, "https://openhow.example/confirm-email"),
  );
  await page.screenshot({
    path: "../../artifacts/account-email-mobile.png",
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
  await page.setViewportSize({ width: 800, height: 800 });
  await page.screenshot({
    path: "../../artifacts/account-email-desktop.png",
    fullPage: true,
  });
  expect(errors).toEqual([]);
});

test("landing metadata, icons and private route indexing", async ({
  page,
  request,
}) => {
  const response = await request.get(base + "/");
  expect(response.ok()).toBeTruthy();
  const html = await response.text();
  expect(html).toContain('rel="canonical"');
  expect(html).toContain('"@type":"WebSite"');
  expect(html).toContain("Every conversation. Part of a bigger story.");
  for (const asset of [
    "favicon.svg",
    "favicon-32.png",
    "apple-touch-icon.png",
    "social-card.png",
    "site.webmanifest",
  ])
    expect((await request.get(base + "/" + asset)).ok()).toBeTruthy();
  await page.goto(base + "/#login");
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute(
    "content",
    "noindex, nofollow",
  );
  await page.screenshot({
    path: "../../artifacts/account-login-desktop.png",
    fullPage: true,
  });
});
