import { accountBase } from "./account-helpers";
import { test, expect } from "@playwright/test";
test("explore, annotate, save a canvas, compare and preview publication", async ({
  page,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto(accountBase + "/#login");
  await page.getByRole("button", { name: "alex", exact: true }).click();
  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(
    page.getByRole("heading", { name: "Good to see you, Alex." }),
  ).toBeVisible();
  await expect(page.locator(".journey-card").first()).toBeVisible();
  await page.screenshot({
    path: "../../artifacts/dashboard.png",
    fullPage: true,
  });
  await page.locator('.journey-card[href="#canvas/cloud-gaming"]').click();
  await expect(page.locator(".react-flow__node")).toHaveCount(12);
  await page.screenshot({ path: "../../artifacts/canvas.png" });
  await page
    .locator(".react-flow__node")
    .filter({ hasText: "The browser is the console" })
    .dblclick();
  await expect(
    page.getByRole("heading", { name: "The browser is the console" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Source text", exact: true }).click();
  const block = page.locator(".message-content").first();
  await block.evaluate((el) => {
    const text = el.querySelector("pre")!.firstChild!;
    const r = document.createRange();
    r.setStart(text, 0);
    r.setEnd(text, 34);
    const s = window.getSelection()!;
    s.removeAllRanges();
    s.addRange(r);
    el.dispatchEvent(
      new MouseEvent("mouseup", { bubbles: true, clientX: 650, clientY: 320 }),
    );
  });
  await page.getByRole("button", { name: "Add comment", exact: true }).click();
  await page
    .getByPlaceholder("Add your perspective. Use @ to mention…")
    .fill("Verified source selection. @Sam");
  await page.getByRole("button", { name: "SR Sam Rivera" }).click();
  await page.getByRole("button", { name: "Post comment" }).click();
  await expect(page.locator(".composing")).toHaveCount(0);
  await expect(
    page
      .locator(".comment")
      .filter({ hasText: "Verified source selection. @Sam Rivera" })
      .first(),
  ).toBeVisible();
  await page.screenshot({ path: "../../artifacts/explorer.png" });
  await page.getByRole("button", { name: "Compare", exact: true }).click();
  await expect(page.locator(".compare-reader")).toHaveCount(2);
  await page.screenshot({ path: "../../artifacts/comparison.png" });
  await page.goto(accountBase + "/#public/cloudplay-demo");
  await expect(
    page.getByRole("heading", {
      name: "Building a browser-based cloud gaming platform",
    }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Next chapter" }).click();
  await page.screenshot({ path: "../../artifacts/public.png" });
  expect(errors).toEqual([]);
});

test("create, arrange, save, reload and publish a sanitized journey", async ({
  page,
  request,
}) => {
  await page.goto(accountBase + "/#login");
  await page.getByRole("button", { name: "alex", exact: true }).click();
  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(page.locator(".journey-card").first()).toBeVisible();
  await page.getByRole("button", { name: "New journey", exact: true }).click();
  await page
    .getByPlaceholder("What are you building or exploring?")
    .fill("Browser persistence check");
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "Create journey", exact: true })
    .click();
  await expect(
    page.getByRole("button", { name: "Add to journey" }),
  ).toBeVisible();
  const journeyId = (await page.url()).split("canvas/")[1];
  await page.getByRole("button", { name: "Add to journey" }).click();
  for (const title of [
    "The browser is the console",
    "A stream that feels local",
  ]) {
    await page.locator(".library-option").filter({ hasText: title }).click();
  }
  await page
    .getByRole("button", { name: "Add selected conversations", exact: true })
    .click();
  await page.getByRole("button", { name: "Auto layout" }).click();
  await page.getByRole("button", { name: "Save journey", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Save journey", exact: true }),
  ).toBeDisabled();
  await page.reload();
  await expect(page.locator(".react-flow__node")).toHaveCount(2);
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await page
    .getByRole("button", { name: "Select all messages", exact: true })
    .click();
  await page
    .getByLabel("Redact exact text (one phrase per line)")
    .fill("cloud gaming");
  await page.getByLabel("Publication visibility").selectOption("Unlisted");
  await page.getByRole("button", { name: "Review sanitized preview" }).click();
  await expect(
    page.getByRole("button", { name: "Publish unlisted snapshot" }),
  ).toBeDisabled();
  await page
    .getByLabel(
      "I reviewed the included content and explicitly confirm publication.",
    )
    .check();
  await page.getByRole("button", { name: "Publish unlisted snapshot" }).click();
  await expect(
    page.getByRole("heading", { name: "Your snapshot is ready" }),
  ).toBeVisible();
  const url = await page.getByLabel("Publication link").inputValue();
  const slug = url.split("public/")[1];
  const published = await request.get(accountBase + "/api/public/" + slug);
  expect(published.ok()).toBeTruthy();
  const data = await published.text();
  expect(data).toContain("[REDACTED]");
  expect(data).not.toContain("cloud gaming");
  expect(data).not.toContain("rawPayload");
  expect(data).not.toContain("metadata");
  await page.evaluate(async (id) => {
    await fetch("/api/journeys/" + id, {
      method: "DELETE",
      headers: { "X-Journey": "1" },
    });
  }, journeyId);
});

test("import wizard previews and commits a generic conversation", async ({
  page,
}) => {
  await page.goto(accountBase + "/#login");
  await page.getByRole("button", { name: "alex", exact: true }).click();
  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(page.locator(".journey-card").first()).toBeVisible();
  await page
    .getByRole("button", { name: "Import conversations", exact: true })
    .click();
  await page.getByLabel("Import destination").selectOption("personal");
  const title = "Browser import " + Date.now();
  await page
    .locator("input[type=file]")
    .first()
    .setInputFiles({
      name: "generic.json",
      mimeType: "application/json",
      buffer: Buffer.from(
        JSON.stringify({
          title,
          messages: [
            { role: "user", content: "Keep  two spaces.\nNew line." },
            { role: "assistant", content: "Verified imported source." },
          ],
        }),
      ),
    });
  await expect(page.getByText("Selected conversations: 1")).toBeVisible();
  await page.getByRole("button", { name: "Import selected" }).click();
  await expect(
    page.getByRole("heading", { name: "Import complete", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Imported: 1 · Duplicates skipped: 0"),
  ).toBeVisible();
  await page.getByRole("button", { name: "Back to workspace" }).click();
  await page.getByLabel("Workspace", { exact: true }).selectOption("personal");
  await page
    .getByRole("link", { name: "Conversations", exact: false })
    .first()
    .click();
  await page.getByRole("link").filter({ hasText: title }).click();
  await expect(page.getByRole("heading", { name: title })).toBeVisible();
  await page.getByRole("button", { name: "Source text", exact: true }).click();
  await expect(page.locator(".source-text").first()).toHaveText(
    "Keep  two spaces.\nNew line.",
  );
});
