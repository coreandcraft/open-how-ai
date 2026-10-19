import { test, expect } from "@playwright/test";
import { accountBase } from "./account-helpers";

for (const width of [1440, 390]) {
  test(`source groups collapse, filter unloaded groups and keep a single scrollbar (${width}px)`, async ({
    page,
  }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", (e) => errors.push(e.message));
    const rows = (start: number, count: number, key = "path-a") =>
      Array.from({ length: count }, (_, i) => ({
        id: "chat-" + (start + i),
        title: "Imported chat " + (start + i),
        provider: "Codex",
        sourceIdentity: "default",
        sourceGroupKey: key,
        sourcePath: key === "path-a" ? "C:/Dev/JourneyAI" : "C:/Dev/Other",
        sourceFormat: "codex-rollout-v1",
        messageCount: 20,
        applicationProjectId: "app",
        projectName: "Imported work",
      }));
    const groups = [
      {
        key: "path-a",
        provider: "Codex",
        sourceIdentity: "default",
        name: "C:/Dev/JourneyAI",
        count: 100,
      },
      {
        key: "path-b",
        provider: "Codex",
        sourceIdentity: "default",
        name: "C:/Dev/Other",
        count: 1,
      },
    ];
    await page.route("**/api/**", (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      let json: any = [];
      if (path === "/api/me")
        json = { id: "alex", name: "Alex Morgan", email: "alex@example.test" };
      else if (path === "/api/workspaces")
        json = [{ id: "cloudplay", name: "Workspace", role: "Owner" }];
      else if (path.endsWith("/summary"))
        json = {
          conversations: 101,
          journeys: 0,
          providers: [{ provider: "Codex", count: 101 }],
        };
      else if (path.endsWith("/library-groups")) json = groups;
      else if (path.endsWith("/projects"))
        json = [{ id: "app", name: "Imported work" }];
      else if (path.endsWith("/journeys"))
        json = { items: [], nextCursor: null };
      else if (path.endsWith("/library")) {
        const filtered = url.searchParams.get("sourceGroup") === "path-b";
        const more = url.searchParams.has("cursor");
        json = {
          items: filtered ? rows(100, 1, "path-b") : rows(more ? 50 : 0, 50),
          nextCursor: filtered || more ? null : "page-two",
        };
      }
      return route.fulfill({ json });
    });
    await page.route("**/hubs/**", (route) => route.fulfill({ status: 503 }));
    await page.goto(accountBase + "/#library");
    const group = page
      .locator(".library-source-group")
      .filter({ hasText: "C:/Dev/JourneyAI" });
    await expect(group.locator(".table-row")).toHaveCount(50);
    const singleScroll = async () => {
      const dimensions = await page.evaluate(() => {
        const panel = document.querySelector(".page")!;
        return {
          document: document.documentElement.scrollHeight,
          viewport: innerHeight,
          bodyWidth: document.body.scrollWidth,
          width: innerWidth,
          panelHeight: panel.clientHeight,
          contentHeight: panel.scrollHeight,
        };
      });
      expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport + 1);
      expect(dimensions.bodyWidth).toBeLessThanOrEqual(dimensions.width);
      expect(dimensions.contentHeight).toBeGreaterThan(dimensions.panelHeight);
    };
    await singleScroll();
    await group.locator("summary").click();
    await expect(group).not.toHaveAttribute("open");
    await expect(group.locator(".conversation-table")).toBeHidden();
    await page.getByRole("button", { name: "Load more", exact: true }).click();
    await expect(group.locator(".table-row")).toHaveCount(100);
    await expect(group).not.toHaveAttribute("open");
    await group.locator("summary").click();
    await singleScroll();
    await expect(
      page.getByRole("button", { name: "Load more", exact: true }),
    ).toBeHidden();
    await page.getByLabel("Source group filter").selectOption("path-b");
    await expect(page.locator(".table-row")).toHaveCount(1);
    await expect(page.locator(".library-source-group summary")).toContainText(
      "C:/Dev/Other",
    );
    await page.getByLabel("Application project filter").selectOption("app");
    await expect(page.locator(".table-row")).toHaveCount(1);
    await page.screenshot({
      path: testInfo.outputPath("library-source-groups-" + width + ".png"),
    });
    expect(errors).toEqual([]);
  });
}
