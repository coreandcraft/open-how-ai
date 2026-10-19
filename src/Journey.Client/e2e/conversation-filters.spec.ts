import { test, expect } from "@playwright/test";
import { accountBase } from "./account-helpers";

for (const width of [1440, 390]) {
  test(`shared conversation filters combine and persist when collapsed (${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.route("**/api/public/filter-demo", route => route.fulfill({ json: {
      kind: "conversation", title: "Filter example", provider: "ChatGPT",
      messages: [
        { id: "m1", role: "user", blocks: [{ text: "Plan the deployment" }] },
        { id: "m2", role: "assistant", blocks: [{ text: "Use regional signaling services" }] },
        { id: "m3", role: "assistant", blocks: [{ text: "Monitor the service" }] },
        { id: "m4", role: "user", blocks: [{ text: "What happens next?" }] }
      ],
      annotations: [
        { messageId: "m2", resolved: false, comments: [{ text: "#architecture Unique annotation evidence" }] },
        { messageId: "m3", resolved: false, comments: [{ text: "#operations Monitor latency" }] }
      ]
    } }));
    await page.route("**/api/public/filter-demo/discussion*", route => route.fulfill({ json: { enabled: false, canManage: false, user: null, entries: [], total: 0 } }));
    await page.goto(accountBase + "/#public/filter-demo");
    const filters = page.getByRole("region", { name: "Conversation filters" });
    const toggle = filters.getByRole("button", { name: /^Filter/ });
    const messages = page.locator(".public-message");
    await expect(messages).toHaveCount(4);
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await expect(filters.getByLabel("Search conversation")).toBeHidden();
    await toggle.click();
    await filters.getByLabel("Message role").selectOption("assistant");
    await expect(messages).toHaveCount(2);
    await filters.getByLabel("Annotation topic").selectOption("architecture");
    await filters.getByLabel("With annotations only").check();
    await expect(messages).toHaveCount(1);
    await expect(messages.first()).toContainText("regional signaling");
    await expect(filters.getByRole("status")).toHaveText("1 of 4 messages");
    await expect(page.locator(".conversation-hidden-gap")).toHaveCount(2);
    await filters.getByLabel("Search conversation").fill("unique ANNOTATION evidence");
    await expect(messages).toHaveCount(1);
    await expect(filters.getByLabel("4 active filters")).toBeVisible();
    await toggle.click();
    await expect(filters.getByLabel("Search conversation")).toBeHidden();
    await expect(messages).toHaveCount(1);
    await filters.getByRole("button", { name: "Clear filters" }).click();
    await expect(messages).toHaveCount(4);
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await toggle.click();
    await filters.getByLabel("Search conversation").fill("no matching content");
    await expect(messages).toHaveCount(0);
    await expect(page.getByText("No messages match these filters.")).toBeVisible();
    await filters.getByRole("button", { name: "Clear filters" }).click();
    await filters.getByLabel("Search conversation").fill("deployment");
    await expect(messages).toHaveCount(1);
    const bounds = await filters.boundingBox();
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    expect(errors).toEqual([]);
  });
}
