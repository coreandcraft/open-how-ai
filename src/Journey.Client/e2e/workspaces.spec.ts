import { accountBase } from "./account-helpers";
import { test, expect } from "@playwright/test";
import { confirmAndLogin } from "./account-helpers";
test("create and manage personal and team workspaces from the top-level workspace screen", async ({
  page,
}) => {
  const email = "workspaces-" + Date.now() + "@example.test";
  const registered = await page.request.post(
    accountBase + "/api/auth/register",
    {
      headers: { "X-Journey": "1" },
      data: {
        email,
        password: "WorkspaceTest!2026",
        name: "Workspace tester",
      },
    },
  );
  expect(registered.ok()).toBeTruthy();
  await confirmAndLogin(page.request, email, "WorkspaceTest!2026");
  await page.goto(accountBase + "/#workspaces");
  await expect(
    page.getByRole("heading", { name: "Workspaces", exact: true }),
  ).toBeVisible();
  await expect(page.locator(".workspace-card")).toHaveCount(1);
  await page
    .locator(".page")
    .getByRole("button", { name: "Create workspace", exact: true })
    .click();
  let dialog = page.getByRole("dialog", {
    name: "Create workspace",
    exact: true,
  });
  await dialog.getByLabel("Workspace name").fill("Independent research");
  await expect(
    dialog.getByRole("radio", { name: "Personal", exact: false }),
  ).toBeChecked();
  await dialog
    .getByRole("button", { name: "Create workspace", exact: true })
    .click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator(".workspace-card")).toHaveCount(2);
  const personal = page.locator(".workspace-card").filter({
    has: page.getByRole("heading", {
      name: "Independent research",
      exact: true,
    }),
  });
  await expect(personal.getByText("Current workspace")).toBeVisible();
  await expect(
    personal.getByRole("button", { name: "Members & access" }),
  ).toHaveCount(0);
  await personal.getByRole("button", { name: "Rename", exact: true }).click();
  const rename = page.getByRole("dialog", { name: "Rename workspace" });
  await rename.getByLabel("Workspace name").fill("Private experiments");
  await rename.getByRole("button", { name: "Save name" }).click();
  await expect(
    page.getByRole("heading", { name: "Private experiments", exact: true }),
  ).toBeVisible();
  await page.locator(".workspace-create").click();
  dialog = page.getByRole("dialog", { name: "Create workspace", exact: true });
  await dialog.getByLabel("Workspace name").fill("Product team");
  await dialog.getByRole("radio", { name: "Team", exact: false }).check();
  await dialog
    .getByRole("button", { name: "Create workspace", exact: true })
    .click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator(".workspace-card")).toHaveCount(3);
  const selector = page.getByLabel("Workspace", { exact: true });
  await expect(selector.locator("option")).toHaveCount(3);
  const arrowHitsSelect = await page
    .locator(".workspace-picker > svg")
    .evaluate((el) => {
      const rect = el.getBoundingClientRect();
      return (
        document.elementFromPoint(
          rect.x + rect.width / 2,
          rect.y + rect.height / 2,
        )?.tagName === "SELECT"
      );
    });
  expect(arrowHitsSelect).toBeTruthy();
  await page.screenshot({ path: "../../artifacts/workspaces.png" });
  const team = page.locator(".workspace-card").filter({
    has: page.getByRole("heading", { name: "Product team", exact: true }),
  });
  await team.getByRole("button", { name: "Members & access" }).click();
  await expect(
    page.getByRole("heading", { name: "Members & access", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Invite a teammate" }),
  ).toBeVisible();
  await page.getByRole("link", { name: "Workspaces", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Workspaces", exact: true }),
  ).toBeVisible();
  await page.reload();
  await expect(page.locator(".workspace-card")).toHaveCount(3);
  await expect(
    page.getByRole("heading", { name: "Private experiments", exact: true }),
  ).toBeVisible();
});
