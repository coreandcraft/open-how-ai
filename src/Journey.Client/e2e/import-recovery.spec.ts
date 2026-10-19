import { accountBase } from "./account-helpers";
import { test, expect } from "@playwright/test";
import { createHash } from "node:crypto";
import { confirmAndLogin } from "./account-helpers";

test("review a source revision, keep its comment and inspect a supplied private file", async ({
  page,
  playwright,
}) => {
  const base = accountBase;
  const headers = { "X-Journey": "1" };
  const email = `recovery-${Date.now()}@fixture.test`;
  const account = await page.request.post(base + "/api/auth/register", {
    headers,
    data: {
      email,
      password: "RecoveryTest!2026",
      name: "Recovery tester",
    },
  });
  expect(account.ok()).toBeTruthy();
  await confirmAndLogin(page.request, email, "RecoveryTest!2026");
  const [workspace] = await (
    await page.request.get(base + "/api/workspaces")
  ).json();
  const text = "Keep this sentence. Original ending.";
  const conversation = (value: string) => ({
    id: "recovery-conversation",
    title: "Source revision fixture",
    messages: [
      {
        id: "source-message",
        role: "assistant",
        content: [
          { type: "text", text: value },
          { type: "image", source: { url: "image.png" } },
        ],
      },
    ],
  });
  async function importSource(value: string, includeImage: boolean) {
    const multipart: any = {
      files: {
        name: "conversation.json",
        mimeType: "application/json",
        buffer: Buffer.from(JSON.stringify(conversation(value))),
      },
    };
    if (includeImage)
      multipart.image = {
        name: "image.png",
        mimeType: "image/png",
        buffer: Buffer.from(
          "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg==",
          "base64",
        ),
      };
    const response = await page.request.post(
      base + `/api/w/${workspace.id}/imports/preview`,
      { headers, multipart },
    );
    expect(response.ok()).toBeTruthy();
    const preview = await response.json();
    expect(preview.errors).toEqual([]);
    if (includeImage) expect(preview.attachments).toHaveLength(1);
    expect(
      (
        await page.request.post(
          base + `/api/imports/${preview.batchId}/commit`,
          {
            headers,
            data: {
              keys: preview.conversations.map((c: any) => c.key),
              includeAttachments: true,
            },
          },
        )
      ).ok(),
    ).toBeTruthy();
    await expect
      .poll(
        async () =>
          (
            await (
              await page.request.get(base + `/api/imports/${preview.batchId}`)
            ).json()
          ).status,
      )
      .toBe("completed");
  }
  await importSource(text, true);
  const { items } = await (
    await page.request.get(base + `/api/w/${workspace.id}/library`)
  ).json();
  const [summary] = items;
  const c = await (
    await page.request.get(base + `/api/conversations/${summary.id}`)
  ).json();
  const m = c.messages[0],
    block = m.blocks.find((b: any) => b.kind === "text");
  expect(
    (
      await page.request.post(base + `/api/w/${workspace.id}/annotations`, {
        headers,
        data: {
          conversationId: c.id,
          messageId: m.id,
          body: "Preserve this explanation",
          anchor: {
            blockId: block.id,
            start: 0,
            end: 19,
            quote: "Keep this sentence.",
            prefix: "",
            suffix: text.slice(19),
            fingerprint: createHash("sha256")
              .update(text)
              .digest("hex")
              .toUpperCase(),
          },
        },
      })
    ).ok(),
  ).toBeTruthy();
  await importSource(
    "New introduction. Keep this sentence. Revised ending.",
    false,
  );
  await page.goto(base + `/#explorer/${c.id}`);
  await page
    .getByText("Source grouping & import history", { exact: true })
    .click();
  await page
    .getByText("Changed source message · unresolved", { exact: true })
    .click();
  await page
    .getByRole("button", { name: "Review & accept incoming", exact: true })
    .click();
  await page
    .getByRole("button", { name: "Accept revision", exact: true })
    .click();
  await expect(
    page
      .getByRole("status")
      .filter({ hasText: "1 annotations recovered · 0 need review" }),
  ).toBeVisible();
  await page
    .getByText("Recovered files, tools & revisions", { exact: true })
    .click();
  const download = page.getByRole("link", { name: "Download supplied file" });
  await expect(download).toBeVisible();
  await expect(page.getByText(/Previous source version/)).toBeVisible();
  const privateUrl = await download.getAttribute("href");
  const anonymous = await playwright.request.newContext();
  expect((await anonymous.get(base + privateUrl)).status()).toBe(401);
  await anonymous.dispose();
  const outsider = await playwright.request.newContext();
  const outsiderEmail = "outsider-" + Date.now() + "@fixture.test";
  expect(
    (
      await outsider.post(base + "/api/auth/register", {
        headers,
        data: {
          email: outsiderEmail,
          password: "RecoveryTest!2026",
          name: "Other workspace",
        },
      })
    ).ok(),
  ).toBeTruthy();
  await confirmAndLogin(outsider, outsiderEmail, "RecoveryTest!2026");
  expect((await outsider.get(base + privateUrl)).status()).toBe(403);
  expect(
    (
      await outsider.get(base + "/api/conversations/" + c.id + "/versions")
    ).status(),
  ).toBe(403);
  expect(
    (
      await outsider.post(base + "/api/imports/discovery/scan", {
        headers,
        data: { roots: ["codex"], excludeDirectories: [], consent: true },
      })
    ).status(),
  ).toBe(403);
  await outsider.dispose();
  const notes = await (
    await page.request.get(
      base + `/api/w/${workspace.id}/annotations?conversationId=${c.id}`,
    )
  ).json();
  expect(notes[0].orphaned).toBe(false);
  expect(JSON.parse(notes[0].anchor).start).toBe(18);
  await expect(
    page.getByText("Workspace permission denied.", { exact: true }),
  ).toHaveCount(0);
  await page.screenshot({
    path: "../../artifacts/import-recovery-review.png",
    fullPage: true,
  });
});
