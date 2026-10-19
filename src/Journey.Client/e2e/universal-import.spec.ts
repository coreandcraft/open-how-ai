import { accountBase } from "./account-helpers";
import { test, expect } from "@playwright/test";
import path from "node:path";
import { confirmAndLogin } from "./account-helpers";
test("import native sessions into a cross-provider project", async ({
  page,
}) => {
  const email = "universal-" + Date.now() + "@example.test";
  const account = await page.request.post(accountBase + "/api/auth/register", {
    headers: { "X-Journey": "1" },
    data: {
      email,
      password: "ImportTest!2026",
      name: "Universal importer",
    },
  });
  expect(account.ok()).toBeTruthy();
  await confirmAndLogin(page.request, email, "ImportTest!2026");
  await page.goto(accountBase + "/#dashboard");
  await page
    .getByRole("button", { name: "Import conversations", exact: true })
    .click();
  const dialog = page.getByRole("dialog");
  await dialog
    .locator("input[type=file]")
    .first()
    .setInputFiles([
      path.resolve("../../fixtures/claude-code.jsonl"),
      path.resolve("../../fixtures/codex-rollout.jsonl"),
    ]);
  await expect(dialog.getByText("Selected conversations: 3")).toBeVisible();
  await expect(dialog.getByText(/Child session of/).first()).toBeVisible();
  await dialog
    .getByLabel("Or create a project", { exact: true })
    .fill("Cross-provider implementation");
  await page.screenshot({
    path: "../../artifacts/universal-import-preview.png",
    fullPage: true,
  });
  await dialog
    .getByRole("button", { name: "Import selected", exact: true })
    .click();
  await expect(
    dialog.getByRole("heading", { name: "Import complete", exact: true }),
  ).toBeVisible();
  await expect(
    dialog.getByText("Imported: 3 · Duplicates skipped: 0"),
  ).toBeVisible();
  await dialog
    .getByRole("button", { name: "Back to workspace", exact: true })
    .click();
  await page.goto(accountBase + "/#library");
  await page
    .getByLabel("Application project filter")
    .selectOption({ label: "Cross-provider implementation" });
  await expect(page.locator(".table-row")).toHaveCount(3);
  await page.getByRole("link").filter({ hasText: "Subagent agent-b" }).click();
  await page
    .getByText("Source grouping & import history", { exact: true })
    .click();
  await expect(
    page.getByRole("link", { name: "Open linked session" }),
  ).toBeVisible();
});
