import { test, expect } from "@playwright/test";
import { accountBase } from "./account-helpers";

for (const width of [1440, 390]) {
  test(`import selection toggles groups, rows and all conversations including duplicates (${width}px)`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 1000 });
    const conversation = (
      key: string,
      sourcePath: string,
      duplicate = false,
    ) => ({
      key,
      title: key,
      provider: "Codex",
      sourcePath,
      duplicate,
      messageCount: 1,
      messages: [],
    });
    let committed: any;
    await page.route("**/api/**", (route) => {
      const path = new URL(route.request().url()).pathname;
      let json: any = [];
      if (path === "/api/me")
        json = { id: "selection-user", name: "Selection tester" };
      else if (path === "/api/workspaces")
        json = [{ id: "cloudplay", name: "Workspace", role: "Owner" }];
      else if (path === "/api/imports/limits")
        json = { maxBytes: 1000000, maxFiles: 500 };
      else if (path.endsWith("/library-groups")) json = [];
      else if (path.endsWith("/library") || path.endsWith("/journeys"))
        json = { items: [], nextCursor: null };
      else if (path.endsWith("/summary"))
        json = { conversations: 0, journeys: 0, providers: [] };
      else if (path.endsWith("/imports/preview"))
        json = {
          batchId: "selection-preview",
          errors: [],
          attachments: [],
          projects: [
            { key: "metadata", provider: "Codex", name: "Project metadata" },
          ],
          conversations: [
            conversation("First chat", "A"),
            conversation("Second chat", "A"),
            conversation("Duplicate in A", "A", true),
            conversation("Third chat", "B"),
            conversation("Already imported", "C", true),
          ],
        };
      else if (path.endsWith("/commit")) {
        committed = route.request().postDataJSON();
        json = {};
      } else if (path.endsWith("/imports/selection-preview"))
        json = { status: "completed", total: 3, imported: 3, duplicates: 0 };
      return route.fulfill({ json });
    });
    await page.route("**/hubs/**", (route) => route.fulfill({ status: 503 }));
    await page.goto(accountBase + "/#library");
    await page
      .getByRole("button", { name: "Import conversations", exact: true })
      .click();
    const dialog = page.getByRole("dialog");
    await dialog
      .locator("input[type=file]")
      .first()
      .setInputFiles({
        name: "sessions.jsonl",
        mimeType: "application/json",
        buffer: Buffer.from("{}"),
      });
    const group = dialog.getByRole("checkbox", {
      name: "Select group Codex / A",
      exact: true,
    });
    const first = dialog
      .locator(".import-item")
      .filter({ has: page.getByRole("button", { name: /^First chat/ }) })
      .getByRole("checkbox");
    const groupDetails = dialog
      .locator("details")
      .filter({
        has: page.getByRole("checkbox", {
          name: "Select group Codex / A",
          exact: true,
        }),
      });
    const groupRows = groupDetails.locator(".import-item input[type=checkbox]");
    await expect(group).toBeChecked({ indeterminate: true });
    await expect(groupRows).toHaveCount(3);
    await group.click();
    await expect(
      groupDetails.locator(".import-item input:checked"),
    ).toHaveCount(3);
    await group.uncheck();
    await expect(
      groupDetails.locator(".import-item input:checked"),
    ).toHaveCount(0);
    await expect(first).not.toBeChecked();
    await expect(group).not.toBeChecked();
    await group.check();
    await expect(first).toBeChecked();
    await first.uncheck();
    await expect(first).not.toBeChecked();
    await expect(group).toBeChecked({ indeterminate: true });
    await group.check();
    await expect(first).toBeChecked();
    const duplicateGroup = dialog.getByRole("checkbox", {
      name: "Select group Codex / C",
      exact: true,
    });
    await expect(duplicateGroup).toBeEnabled();
    await expect(duplicateGroup).not.toBeChecked();
    const duplicateRow = dialog.getByRole("checkbox", {
      name: "Already imported",
      exact: true,
    });
    await duplicateGroup.check();
    await expect(duplicateRow).toBeChecked();
    await duplicateRow.uncheck();
    await expect(duplicateGroup).not.toBeChecked();
    await duplicateRow.check();
    await expect(duplicateGroup).toBeChecked();
    await duplicateGroup.uncheck();
    await expect(duplicateRow).not.toBeChecked();
    await duplicateGroup.check();
    const metadata = dialog
      .locator("label")
      .filter({ hasText: "Source project metadata" })
      .getByRole("checkbox");
    await metadata.uncheck();
    await expect(metadata).not.toBeChecked();
    await metadata.check();
    const all = dialog.getByRole("checkbox", {
      name: "Select all",
      exact: true,
    });
    await expect(all).toBeChecked();
    for (let i = 0; i < 3; i++) {
      await all.uncheck();
      await expect(dialog.locator(".import-item input:checked")).toHaveCount(0);
      await all.check();
      await expect(dialog.locator(".import-item input:checked")).toHaveCount(5);
    }
    await all.uncheck();
    await expect(
      dialog.getByRole("button", { name: "Import selected", exact: true }),
    ).toBeDisabled();
    await expect(dialog.locator("input[type=checkbox]:checked")).toHaveCount(1); // Append updates remains checked.
    await all.focus();
    await page.keyboard.press("Space");
    await expect(all).toBeChecked();
    await expect(metadata).toBeChecked();
    await expect(
      dialog.getByText("Selected conversations: 5", { exact: true }),
    ).toBeVisible();
    await dialog
      .getByRole("button", { name: "Import selected", exact: true })
      .click();
    await expect
      .poll(() => committed?.keys?.slice().sort())
      .toEqual(
        [
          "First chat",
          "Second chat",
          "Duplicate in A",
          "Third chat",
          "Already imported",
          "metadata",
        ].sort(),
      );
  });
}
