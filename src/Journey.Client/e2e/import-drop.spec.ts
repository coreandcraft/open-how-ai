import { test, expect, type Page } from "@playwright/test";
const base = process.env.JOURNEY_TEST_URL || "http://127.0.0.1:5080";
const preview = {
  batchId: "drop-preview",
  conversations: [],
  projects: [],
  attachments: [],
  errors: [],
};

async function openImport(page: Page) {
  await page.addInitScript(() => localStorage.setItem("journey.locale", "en"));
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    const json =
      path === "/api/me"
        ? { id: "drop-user", name: "Drop tester" }
        : path === "/api/workspaces"
          ? [{ id: "drop-workspace", name: "Drop workspace", role: "Owner" }]
          : path === "/api/imports/limits"
            ? { maxBytes: 15_000_000_000, maxFiles: 500 }
            : path.endsWith("/library") || path.endsWith("/journeys")
              ? { items: [], nextCursor: null }
              : path.endsWith("/summary")
                ? { conversations: 0, journeys: 0, providers: [] }
                : [];
    return route.fulfill({ json });
  });
  await page.goto(base + "/#library");
  await page
    .getByRole("button", { name: "Import conversations", exact: true })
    .click();
  await expect(
    page.getByRole("button", { name: "Choose files", exact: true }),
  ).toBeEnabled();
}

test("background preview polls until processing finishes", async ({ page }) => {
  await openImport(page);
  const progress = {
    stage: "processing",
    processed: 1,
    total: 3,
    skipped: 0,
    elapsedMilliseconds: 3500,
    timings: {
      sanitization: { milliseconds: 3000, operations: 1, active: 0 },
      duplicateLookup: { milliseconds: 500, operations: 1, active: 0 },
    },
    metrics: { duplicateQueries: 2 },
  };
  let ready = false;
  let polls = 0;
  await page.route("**/imports/progress/*", (route) =>
    route.fulfill({ json: progress }),
  );
  await page.route("**/imports/preview?*", (route) => {
    expect(new URL(route.request().url()).searchParams.get("background")).toBe(
      "true",
    );
    return route.fulfill({
      status: 202,
      json: { status: "processing", previewId: "background-1" },
    });
  });
  await page.route("**/imports/previews/background-1", (route) => {
    polls++;
    return route.fulfill({
      status: ready ? 200 : 202,
      json: ready
        ? {
            ...preview,
            fileProgress: progress,
            conversations: [
              {
                key: "bg",
                title: "Background conversation",
                provider: "Generic",
                messageCount: 1,
                messages: [],
                duplicate: false,
              },
            ],
          }
        : {
            status: "processing",
            previewId: "background-1",
            fileProgress: progress,
          },
    });
  });
  await page
    .locator('input[type="file"]')
    .first()
    .setInputFiles({
      name: "conversation.txt",
      mimeType: "text/plain",
      buffer: Buffer.from("hello"),
    });
  const status = page
    .getByRole("status")
    .filter({ hasText: "Reading your sources…" });
  await expect(status).toContainText("1 of 3 files processed");
  await page.getByText("Processing timings", { exact: true }).click();
  await expect(
    page.getByText("Server elapsed: 3.5 seconds", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Sanitizing content", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Duplicate lookup queries: 2", { exact: true }),
  ).toBeVisible();
  await expect.poll(() => polls).toBeGreaterThanOrEqual(2);
  ready = true;
  await expect(status).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "Import selected", exact: true }),
  ).toBeEnabled();
  await expect(page.getByText("Background conversation").first()).toBeVisible();
});

test("expired background preview shows recovery message and allows another upload", async ({
  page,
}) => {
  await openImport(page);
  await page.route("**/imports/preview?*", (route) =>
    route.fulfill({
      status: 202,
      json: { status: "processing", previewId: "expired" },
    }),
  );
  await page.route("**/imports/previews/expired", (route) =>
    route.fulfill({
      status: 410,
      json: {
        error:
          "This import preview expired or the server restarted. Please upload the files again.",
      },
    }),
  );
  await page
    .locator('input[type="file"]')
    .first()
    .setInputFiles({
      name: "conversation.txt",
      mimeType: "text/plain",
      buffer: Buffer.from("hello"),
    });
  await expect(
    page
      .getByRole("alert")
      .filter({ hasText: "Please upload the files again." }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Choose files", exact: true }),
  ).toBeEnabled();
  await expect(
    page.getByRole("status").filter({ hasText: "Reading your sources…" }),
  ).toHaveCount(0);
});

test("ZIP drop retains files after the browser clears the drag data store", async ({
  page,
}) => {
  await openImport(page);
  let body = "";
  await page.route("**/imports/preview?*", async (route) => {
    body = route.request().postDataBuffer()?.toString() || "";
    await route.fulfill({ json: preview });
  });
  await page.locator(".dropzone").evaluate((zone) => {
    const transfer = new DataTransfer();
    transfer.items.add(
      new File(["PK\x05\x06"], "sessions.zip", { type: "application/zip" }),
    );
    zone.dispatchEvent(
      new DragEvent("drop", {
        bubbles: true,
        cancelable: true,
        dataTransfer: transfer,
      }),
    );
    transfer.items.clear();
  });
  await expect.poll(() => body).toContain('filename="sessions.zip"');
  await expect(
    page.getByRole("button", { name: "Import selected", exact: true }),
  ).toBeDisabled();
});

test("folder drop visits all batches and preserves deeply nested transcript and image paths", async ({
  page,
}) => {
  await openImport(page);
  let body = "";
  await page.route("**/imports/preview?*", async (route) => {
    body = route.request().postDataBuffer()?.toString() || "";
    await route.fulfill({ json: preview });
  });
  await page.locator(".dropzone").evaluate((zone) => {
    const file = (name: string) => ({
      name,
      isFile: true,
      isDirectory: false,
      file: (done: (value: File) => void) =>
        queueMicrotask(() => done(new File(["source"], name))),
    });
    const folder = (name: string, batches: any[][]) => ({
      name,
      isFile: false,
      isDirectory: true,
      createReader: () => {
        let index = 0;
        return {
          readEntries: (done: (entries: any[]) => void) =>
            queueMicrotask(() => done(batches[index++] || [])),
        };
      },
    });
    const root = folder("backup", [
      [
        folder("sessions", [
          [folder("2026", [[folder("10", [[file("rollout.jsonl")]])]])],
        ]),
      ],
      [
        folder("generated_images", [
          [folder("chat-id", [[file("image.png")]])],
        ]),
      ],
    ]);
    const event = new Event("drop", { bubbles: true, cancelable: true });
    Object.defineProperty(event, "dataTransfer", {
      value: { items: [{ webkitGetAsEntry: () => root }], files: [] },
    });
    zone.dispatchEvent(event);
  });
  await expect
    .poll(() => body)
    .toContain('filename="backup/sessions/2026/10/rollout.jsonl"');
  expect(body).toContain(
    'filename="backup/generated_images/chat-id/image.png"',
  );
});

test("empty folder drop explains why no preview appeared", async ({ page }) => {
  await openImport(page);
  await page.locator(".dropzone").evaluate((zone) => {
    const event = new Event("drop", { bubbles: true, cancelable: true });
    Object.defineProperty(event, "dataTransfer", {
      value: {
        items: [
          {
            webkitGetAsEntry: () => ({
              name: "empty",
              isFile: false,
              isDirectory: true,
              createReader: () => ({
                readEntries: (done: (items: any[]) => void) => done([]),
              }),
            }),
          },
        ],
        files: [],
      },
    });
    zone.dispatchEvent(event);
  });
  await expect(page.getByRole("dialog").getByRole("alert")).toHaveText(
    "No files were found. Choose files or a folder and try again.",
  );
});

test("unreadable preview stays visible in the import dialog", async ({
  page,
}) => {
  await openImport(page);
  await page.route("**/imports/preview?*", (route) =>
    route.fulfill({
      status: 200,
      contentType: "text/html",
      body: "invalid preview response",
    }),
  );
  await page
    .locator('input[type="file"]')
    .first()
    .setInputFiles({
      name: "sessions.zip",
      mimeType: "application/zip",
      buffer: Buffer.from("PK"),
    });
  await expect(page.getByRole("dialog").getByRole("alert")).toHaveText(
    "The import preview could not be read. Please try again.",
  );
  await expect(
    page.getByRole("button", { name: "Choose files", exact: true }),
  ).toBeEnabled();
});

test("completed conversation count shows the final linking phase", async ({
  page,
}) => {
  await openImport(page);
  await page.route("**/imports/preview?*", (route) =>
    route.fulfill({
      json: {
        ...preview,
        conversations: [
          {
            key: "session",
            title: "Local session",
            provider: "Codex",
            messageCount: 1,
            messages: [],
            duplicate: false,
          },
        ],
      },
    }),
  );
  await page.route("**/api/imports/drop-preview**", (route) =>
    route.fulfill({
      json: {
        status: "processing",
        total: 1,
        imported: 1,
        duplicates: 0,
        updated: 0,
        conflicts: 0,
        error: "",
        fileProgress: {
          stage: "recovering",
          total: 2,
          processed: 2,
          skipped: 0,
        },
      },
    }),
  );
  await page
    .locator('input[type="file"]')
    .first()
    .setInputFiles({
      name: "session.jsonl",
      mimeType: "application/json",
      buffer: Buffer.from("{}"),
    });
  await page
    .getByRole("button", { name: "Import selected", exact: true })
    .click();
  const status = page
    .getByRole("status")
    .filter({ hasText: "Finishing import…" });
  await expect(status).toBeVisible();
  await expect(status).toContainText("1 of 1 conversations");
  await expect(status).toContainText(
    "Linking supplied files and recorded tool results.",
  );
});
