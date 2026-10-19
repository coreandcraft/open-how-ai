import { accountBase } from "./account-helpers";
import { test, expect } from "@playwright/test";
const base = accountBase;
const login = { email: "alex@journey.local", password: "JourneyDemo!2026" };
for (const source of ["journeys/cloud-gaming", "conversations/demo-c9"]) {
  test(`discussion access, questions, replies and disabling: ${source}`, async ({
    page,
    request,
  }) => {
    await request.post(`${base}/api/auth/login`, {
      headers: { "X-Journey": "1" },
      data: login,
    });
    const published = await request.post(`${base}/api/${source}/publish`, {
      headers: { "X-Journey": "1" },
      data: {
        messageIds: ["demo-m9-0"],
        redactions: [],
        visibility: "Unlisted",
        confirm: true,
        discussionEnabled: true,
      },
    });
    expect(published.ok()).toBeTruthy();
    const { slug } = await published.json();
    const endpoint = `${base}/api/public/${slug}/discussion`;
    try {
      await page.goto(`${base}/#public/${slug}`);
      await page
        .getByRole("button", { name: "Discussion", exact: true })
        .click();
      const discussion = page.getByRole("region", {
        name: "Comments & questions",
      });
      await expect(
        discussion.getByText("Join the conversation", { exact: true }),
      ).toBeVisible();
      await expect(discussion.locator("textarea")).toHaveCount(0);
      await discussion
        .getByRole("link", { name: "Sign in", exact: true })
        .click();
      await page
        .getByLabel("Email", { exact: true })
        .fill("riley@journey.local");
      await page
        .getByLabel("Password", { exact: true })
        .fill("JourneyDemo!2026");
      await page
        .getByRole("button", { name: "Open workspace", exact: true })
        .click();
      await expect(page).toHaveURL(new RegExp(`#public/${slug}$`));
      await expect(discussion.getByRole("switch")).toHaveCount(0);
      await discussion
        .getByRole("button", { name: "Question", exact: true })
        .click();
      await discussion
        .getByLabel("Question", { exact: true })
        .fill("Why did you choose WebRTC?");
      await discussion
        .getByRole("button", { name: "Post question", exact: true })
        .click();
      await expect(
        discussion.getByText("Why did you choose WebRTC?", { exact: true }),
      ).toBeVisible();
      await discussion
        .getByRole("button", { name: "Reply", exact: true })
        .click();
      await discussion
        .getByLabel("Reply", { exact: true })
        .fill("Especially for mobile browsers?");
      await discussion
        .getByRole("button", { name: "Post reply", exact: true })
        .click();
      await expect(
        discussion.getByText("Especially for mobile browsers?", {
          exact: true,
        }),
      ).toBeVisible();
      await page.reload();
      await expect(
        discussion.getByText("Why did you choose WebRTC?", { exact: true }),
      ).toBeVisible();
      await discussion
        .getByRole("button", { name: "Comments", exact: true })
        .click();
      await expect(
        discussion.getByText("Why did you choose WebRTC?", { exact: true }),
      ).toHaveCount(0);
      await discussion
        .getByRole("button", { name: "All", exact: true })
        .click();
      await expect(
        discussion.getByText("Why did you choose WebRTC?", { exact: true }),
      ).toBeVisible();
      if (source.startsWith("journeys")) {
        await discussion.screenshot({
          path: "../../artifacts/discussion-desktop.png",
        });
        await page.setViewportSize({ width: 390, height: 844 });
        await expect(discussion).toBeVisible();
        expect(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
          ),
        ).toBeTruthy();
        await discussion.screenshot({
          path: "../../artifacts/discussion-mobile.png",
        });
      }
      await page.request.post(`${base}/api/logout`, {
        headers: { "X-Journey": "1" },
        data: {},
      });
      await page.request.post(`${base}/api/auth/login`, {
        headers: { "X-Journey": "1" },
        data: login,
      });
      await page.reload();
      await discussion
        .getByRole("switch", { name: "Allow comments & questions" })
        .click();
      await expect(
        discussion.getByRole("heading", { name: "Discussion is turned off" }),
      ).toBeVisible();
      await expect(discussion.locator("textarea")).toHaveCount(0);
      await page.reload();
      await expect(
        discussion.getByRole("heading", { name: "Discussion is turned off" }),
      ).toBeVisible();
      await discussion
        .getByRole("switch", { name: "Allow comments & questions" })
        .click();
      await expect(
        discussion.getByText("Why did you choose WebRTC?", { exact: true }),
      ).toBeVisible();
    } finally {
      const links = await (
        await request.get(`${base}/api/${source}/publications`)
      ).json();
      const id = links.find((p: { slug: string }) => p.slug === slug)?.id;
      if (id)
        await request.delete(`${base}/api/${source}/publications/${id}`, {
          headers: { "X-Journey": "1" },
        });
    }
  });
}
