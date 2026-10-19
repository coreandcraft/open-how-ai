import { accountBase } from "./account-helpers";
import { test, expect } from "@playwright/test";
async function login(page: any) {
  await page.goto(accountBase + "/#login");
  await page.getByRole("button", { name: "alex", exact: true }).click();
  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(
    page.getByRole("button", { name: "New journey", exact: true }),
  ).toBeEnabled();
}
test("share a conversation directly with a redacted anonymous reader", async ({
  page,
  browser,
}) => {
  await login(page);
  const annotationText = "Shared reasoning " + Date.now() + " secret-note";
  const annotationResponse = await page.request.post(
    accountBase + "/api/w/cloudplay/annotations",
    {
      headers: { "X-Journey": "1" },
      data: {
        conversationId: "demo-c9",
        messageId: "demo-m9-0",
        body: annotationText,
      },
    },
  );
  expect(annotationResponse.ok()).toBeTruthy();
  await page.goto(accountBase + "/#explorer/demo-c9");
  await page
    .getByRole("button", { name: "Share conversation", exact: true })
    .click();
  const dialog = page.getByRole("dialog", { name: "Share this conversation" });
  await expect(dialog.getByLabel("Publication visibility")).toHaveValue(
    "Public",
  );
  await dialog.getByLabel("Select messages by role").selectOption("assistant");
  const roles = await dialog
    .locator(".publish-messages .checkbox-row")
    .evaluateAll((rows) =>
      rows.map((row) => ({
        role: row.querySelector("small")?.textContent,
        checked: row.querySelector("input")?.checked,
      })),
    );
  expect(roles.some((row) => row.checked)).toBeTruthy();
  expect(
    roles.every((row) => row.checked === (row.role === "assistant")),
  ).toBeTruthy();
  await expect(
    dialog.getByText("No journey needed.", { exact: false }),
  ).toBeVisible();
  await dialog
    .getByRole("button", { name: "Select all messages", exact: true })
    .click();
  await dialog
    .getByLabel("Redact exact text (one phrase per line)")
    .fill("cloud gaming\nsecret-note");
  await expect(
    dialog.getByLabel("Include selected annotations"),
  ).not.toBeChecked();
  await dialog.getByLabel("Include selected annotations").check();
  await dialog
    .getByRole("button", { name: "Select all annotations", exact: true })
    .click();
  const annotationChecks = dialog.locator(
    ".publication-annotation-options input[type=checkbox]",
  );
  expect(await annotationChecks.count()).toBeGreaterThan(0);
  expect(
    await annotationChecks.evaluateAll((inputs) =>
      inputs.every((input) => (input as HTMLInputElement).checked),
    ),
  ).toBeTruthy();
  await dialog
    .getByRole("button", { name: "Clear annotations", exact: true })
    .click();
  expect(
    await annotationChecks.evaluateAll((inputs) =>
      inputs.every((input) => !(input as HTMLInputElement).checked),
    ),
  ).toBeTruthy();
  await dialog.getByLabel(annotationText, { exact: true }).check();
  await dialog
    .getByRole("button", { name: "Review sanitized preview" })
    .click();
  await expect(
    dialog.getByRole("button", { name: "Create public link" }),
  ).toBeDisabled();
  await dialog
    .getByLabel(
      "I reviewed the included content and explicitly confirm publication.",
    )
    .check();
  await dialog.getByRole("button", { name: "Create public link" }).click();
  await expect(
    dialog.getByRole("heading", { name: "Your snapshot is ready" }),
  ).toBeVisible();
  const link = await dialog.getByLabel("Publication link").inputValue();
  await expect(
    dialog.locator(".import-result").getByRole("button", { name: "Copy link" }),
  ).toBeVisible();
  const visitor = await browser.newContext();
  const publicPage = await visitor.newPage();
  await publicPage.goto(link);
  await expect(publicPage.locator(".standalone-share")).toBeVisible({
    timeout: 15000,
  });
  await expect(
    publicPage.getByText("SHARED CONVERSATION", { exact: true }),
  ).toBeVisible();
  await expect(publicPage.locator(".public-message")).toHaveCount(10);
  await expect(
    publicPage.getByText("Explore journey canvas", { exact: true }),
  ).toHaveCount(0);
  const annotationToggle = publicPage.locator(".shared-annotation > summary");
  await expect(annotationToggle).toHaveText("[!]");
  await expect(
    publicPage.locator(".shared-annotation .published-curation"),
  ).not.toBeVisible();
  await annotationToggle.click();
  await expect(
    publicPage.getByText(annotationText.replace("secret-note", "[REDACTED]"), {
      exact: true,
    }),
  ).toBeVisible();
  await expect(
    publicPage.getByText("SHARED ANNOTATION", { exact: false }),
  ).toBeVisible();
  await publicPage.screenshot({
    path: "../../artifacts/shared-conversation.png",
    fullPage: true,
  });
  await annotationToggle.focus();
  await publicPage.keyboard.press("Enter");
  await expect(
    publicPage.locator(".shared-annotation .published-curation"),
  ).not.toBeVisible();
  await visitor.close();
});
test("comparison produces findings and a persisted decision without a journey", async ({
  page,
}) => {
  await login(page);
  await page.goto(accountBase + "/#compare");
  await expect(
    page.getByRole("heading", { name: "Compare approaches" }),
  ).toBeVisible();
  const question = "Choose a streaming approach " + Date.now();
  await page.getByLabel("What are you deciding?").fill(question);
  await expect(page.locator(".compare-reader")).toHaveCount(2);
  await page
    .locator("#compare-0")
    .getByRole("button", { name: "Use as evidence" })
    .nth(1)
    .click();
  await page
    .locator("#compare-1")
    .getByRole("button", { name: "Use as evidence" })
    .nth(1)
    .click();
  await page.getByLabel("Comparison criterion").selectOption("Tradeoff");
  await page
    .getByLabel("Finding", { exact: true })
    .fill("A retains a lightweight browser path; B focuses on compatibility.");
  await page.getByRole("button", { name: "Add finding", exact: true }).click();
  await page
    .getByRole("button", { name: "Review findings", exact: false })
    .click();
  await expect(page.locator(".finding-card")).toHaveCount(1);
  await page.getByLabel("Chosen approach").selectOption("Combine");
  await page
    .getByLabel("Why this choice?")
    .fill(
      "Keep the lightweight path and validate streaming for demanding workloads.",
    );
  await expect(page.getByLabel("Comparison destination")).toHaveValue("");
  await page
    .getByRole("button", { name: "Save comparison", exact: true })
    .click();
  await expect(
    page.getByText("Saved comparison", { exact: true }),
  ).toBeVisible();
  const id = await page.getByLabel("Saved comparisons").inputValue();
  await page.screenshot({ path: "../../artifacts/comparison-decision.png" });
  await page.reload();
  await page.getByLabel("Saved comparisons").selectOption(id);
  await expect(page.getByLabel("What are you deciding?")).toHaveValue(question);
  await expect(page.getByLabel("Chosen approach")).toHaveValue("Combine");
  await expect(page.getByLabel("Why this choice?")).toHaveValue(
    "Keep the lightweight path and validate streaming for demanding workloads.",
  );
  await expect(page.locator(".finding-card")).toHaveCount(1);
  await expect(page.getByLabel("Comparison destination")).toHaveValue("");
});
