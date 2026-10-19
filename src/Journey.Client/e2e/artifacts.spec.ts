import { test, expect } from "@playwright/test";
test.use({
  launchOptions: {
    args: [
      "--use-fake-ui-for-media-stream",
      "--use-fake-device-for-media-stream",
    ],
  },
});
test("upload and preview a private annotation image", async ({ page }) => {
  const login = await page.request.post(
    "http://127.0.0.1:5080/api/auth/login",
    {
      headers: { "X-Journey": "1" },
      data: { email: "alex@journey.local", password: "JourneyDemo!2026" },
    },
  );
  expect(login.ok()).toBeTruthy();
  const created = await page.request.post(
    "http://127.0.0.1:5080/api/w/cloudplay/annotations",
    {
      headers: { "X-Journey": "1" },
      data: {
        conversationId: "demo-c9",
        messageId: "demo-m9-0",
        body: "Image artifact browser check",
      },
    },
  );
  expect(created.ok()).toBeTruthy();
  const annotation = await created.json();
  await page.goto("http://127.0.0.1:5080/#explorer/demo-c9");
  const thread = page.locator("#thread-" + annotation.id);
  await expect(thread.getByText("Choose files", { exact: true })).toHaveCount(
    0,
  );
  await thread
    .getByRole("button", { name: "Attach artifact", exact: true })
    .click();
  await thread.getByLabel("Choose artifact files").setInputFiles({
    name: "evidence.png",
    mimeType: "image/png",
    buffer: Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=",
      "base64",
    ),
  });
  await expect(
    thread.getByRole("link", { name: "evidence.png" }),
  ).toBeVisible();
  await expect(thread.getByRole("img", { name: "evidence.png" })).toBeVisible();
  const dropzone = thread.locator(".artifact-dropzone");
  for (const kind of ["drop", "paste"]) {
    await dropzone.evaluate((el, kind) => {
      const dt = new DataTransfer();
      const bytes = Uint8Array.from(
        atob(
          "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=",
        ),
        (c) => c.charCodeAt(0),
      );
      dt.items.add(new File([bytes], kind + ".png", { type: "image/png" }));
      el.dispatchEvent(
        kind === "drop"
          ? new DragEvent("drop", { bubbles: true, dataTransfer: dt })
          : new ClipboardEvent("paste", { bubbles: true, clipboardData: dt }),
      );
    }, kind);
    await expect(
      thread.getByRole("link", { name: kind + ".png", exact: true }),
    ).toBeVisible();
  }
  await page.context().grantPermissions(["microphone"]);
  await thread
    .getByRole("button", { name: "Record audio", exact: true })
    .click();
  await expect(
    thread.getByRole("button", { name: /Stop recording.*1s/ }),
  ).toBeVisible();
  await thread.getByRole("button", { name: /Stop recording/ }).click();
  await expect(thread.locator(".recording-preview audio")).toBeVisible();
  await thread
    .getByRole("button", { name: "Attach recording", exact: true })
    .click();
  await expect(thread.locator(".annotation-artifact audio")).toHaveCount(1);
  await page.reload();
  await expect(
    thread.getByRole("link", { name: "evidence.png" }),
  ).toBeVisible();
});
