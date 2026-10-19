import { test, expect } from "@playwright/test";
import { accountBase } from "./account-helpers";

test("build selected conversations across filters, edit and reload the timeline draft", async ({
  page,
}) => {
  await page.goto(accountBase + "/#login");
  await page.getByRole("button", { name: "alex", exact: true }).click();
  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(page.locator(".journey-card").first()).toBeVisible();
  await page.goto(accountBase + "/#library");
  const build = page.getByRole("button", {
    name: "Build journey",
    exact: true,
  });
  await expect(build).toBeDisabled();
  const search = page.getByPlaceholder(
    "Search conversations, messages, code, comments…",
  );
  await search.fill("The browser is the console");
  await page
    .getByRole("checkbox", {
      name: "Select The browser is the console",
      exact: true,
    })
    .check();
  await search.fill("A stream that feels local");
  await page
    .getByRole("checkbox", {
      name: "Select A stream that feels local",
      exact: true,
    })
    .check();
  await expect(
    page.getByText("2 conversations selected", { exact: true }),
  ).toBeVisible();
  await build.click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Journey title").fill("Generated browser draft");
  await dialog.getByRole("button", { name: "Create editable draft" }).click();
  await expect(
    page.getByText("Generated timeline draft", { exact: true }),
  ).toBeVisible();
  const journeyId = page.url().split("canvas/")[1];
  try {
    await expect(page.locator(".react-flow__node")).toHaveCount(4);
    await expect(page.getByLabel("Journey visibility")).toHaveValue("Private");
    await page.locator(".journey-node.conversation").first().click();
    await page
      .locator(".inspector")
      .getByLabel("Title", { exact: true })
      .fill("Edited timeline node");
    await page.getByTitle("Close inspector").click();
    await page
      .getByRole("button", { name: "Auto layout", exact: true })
      .click();
    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await page
      .getByRole("button", { name: "Save journey", exact: true })
      .click();
    await expect(
      page.getByRole("button", { name: "Save journey", exact: true }),
    ).toBeDisabled();
    await page.reload();
    await expect(
      page.getByText("Generated timeline draft", { exact: true }),
    ).toBeVisible();
    await expect(
      page.getByText("Edited timeline node", { exact: true }),
    ).toBeVisible();
    await expect(page.locator(".react-flow__node")).toHaveCount(4);
    await page.screenshot({ path: "../../artifacts/generated-journey.png" });
  } finally {
    await page.request.delete(accountBase + "/api/journeys/" + journeyId, {
      headers: { "X-Journey": "1" },
    });
  }
});
