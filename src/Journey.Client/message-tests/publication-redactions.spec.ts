import { test, expect, type Page } from "@playwright/test";

const findings = [
  "private@example.test",
  "reviewer@example.test",
  "C:\\Users\\Example\\AppData\\Roaming\\Project\\workspaceStorage\\" +
    "0123456789abcdef".repeat(8) +
    ".png",
];
const sourceText = ["private note", ...findings].join("\n\n");
const conversation = {
  id: "redaction-demo",
  workspaceId: "workspace",
  title: "Redaction review",
  provider: "Cursor",
  messages: [
    {
      id: "message",
      role: "user",
      blocks: [{ id: "block", kind: "text", text: sourceText }],
    },
  ],
};
type PreviewRequest = {
  title?: string;
  redactions: string[];
  confirm: boolean;
};
function preview(request: PreviewRequest) {
  const text = request.redactions.reduce(
    (value, phrase) => value.replaceAll(phrase, "[REDACTED]"),
    sourceText,
  );
  return {
    findings: findings.filter((finding) => text.includes(finding)),
    snapshot: {
      ...conversation,
      kind: "conversation",
      title: request.title || conversation.title,
      messages: [{ ...conversation.messages[0], blocks: [{ text }] }],
    },
  };
}

async function mount(page: Page) {
  const requests: PreviewRequest[] = [];
  await page.route("**/api/**", (route) => {
    if (route.request().url().endsWith("/publication-preview")) {
      const body = route.request().postDataJSON();
      requests.push(body);
      return route.fulfill({ json: preview(body) });
    }
    return route.fulfill({ json: [] });
  });
  await page.route("**/publication-test", (route) =>
    route.fulfill({
      contentType: "text/html",
      body: [
        '<html><head><meta name="viewport" content="width=device-width, initial-scale=1" /></head><body><div id="test"></div><script type="module">',
        "import RefreshRuntime from '/@react-refresh';",
        "RefreshRuntime.injectIntoGlobalHook(window);",
        "window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => (type) => type;",
        "window.__vite_plugin_react_preamble_installed__ = true;",
        "await import('/src/app/styles.css'); await import('/src/app/blueprint.css');",
        "const { default: React } = await import('/node_modules/.vite/deps/react.js');",
        "const { default: { createRoot } } = await import('/node_modules/.vite/deps/react-dom_client.js');",
        "const { PublishDialog } = await import('/src/features/publications/Publish.tsx');",
        "createRoot(document.getElementById('test')).render(React.createElement(PublishDialog, { conversation: " +
          JSON.stringify(conversation) +
          ", close: () => {} }));",
        "</script></body></html>",
      ].join("\n"),
    }),
  );
  await page.goto("/publication-test");
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Select all messages" }).click();
  await dialog
    .getByLabel("Redact exact text (one phrase per line)")
    .fill("private note");
  await dialog
    .getByRole("button", { name: "Review sanitized preview" })
    .click();
  await expect(dialog.locator(".finding")).toHaveCount(3);
  return { dialog, requests };
}

for (const width of [1440, 390]) {
  test(`select and redact multiple findings without reopening at ${width}px`, async ({
    page,
  }, testInfo) => {
    await page.setViewportSize({ width, height: 1000 });
    const { dialog, requests } = await mount(page);
    const confirmation = dialog.getByLabel(
      "I reviewed the included content and explicitly confirm publication.",
    );
    const publish = dialog.getByRole("button", { name: "Create public link" });
    await confirmation.check();
    await expect(publish).toBeEnabled();
    await dialog
      .getByRole("checkbox", { name: findings[0], exact: true })
      .check();
    await dialog
      .getByRole("checkbox", { name: findings[2], exact: true })
      .check();
    await expect(
      dialog.getByRole("heading", { name: "Pre-publication review" }),
    ).toBeVisible();
    await expect(dialog.locator(".finding input:checked")).toHaveCount(2);
    await expect(confirmation).not.toBeChecked();
    await expect(confirmation).toBeDisabled();
    await expect(publish).toBeDisabled();
    expect(requests).toHaveLength(1);
    await page.screenshot({
      path: testInfo.outputPath("selected-findings.png"),
    });
    expect(
      await dialog.evaluate((el) =>
        [...el.querySelectorAll("*")]
          .filter(
            (child) =>
              child.getBoundingClientRect().right >
              el.getBoundingClientRect().right + 1,
          )
          .map((child) => ({
            tag: child.tagName,
            class: child.className,
            width: child.getBoundingClientRect().width,
          }))
          .slice(0, 20),
      ),
    ).toEqual([]);

    await dialog.getByRole("button", { name: "Redact selected (2)" }).click();
    await expect(dialog.locator(".finding")).toHaveCount(1);
    expect(requests).toHaveLength(2);
    expect(requests[1].redactions).toEqual([
      "private note",
      findings[0],
      findings[2],
    ]);
    expect(requests[1].confirm).toBe(false);
    await expect(
      dialog.getByLabel("Redact exact text (one phrase per line)"),
    ).toHaveValue(requests[1].redactions.join("\n"));
    await expect(dialog.locator(".publication-preview")).not.toContainText(
      findings[0],
    );
    await expect(dialog.locator(".publication-preview")).not.toContainText(
      findings[2],
    );
    await expect(dialog.locator(".publication-preview")).toContainText(
      findings[1],
    );
    await expect(confirmation).not.toBeChecked();
    await expect(publish).toBeDisabled();

    await dialog.getByRole("button", { name: "Select all findings" }).click();
    await expect(dialog.locator(".finding input:checked")).toHaveCount(1);
    await dialog
      .getByRole("button", { name: "Clear selection", exact: true })
      .last()
      .click();
    await expect(dialog.locator(".finding input:checked")).toHaveCount(0);
    await expect(
      dialog.getByRole("button", { name: "Redact selected (0)" }),
    ).toBeDisabled();
    await dialog.getByRole("button", { name: "Select all findings" }).click();
    await dialog.getByRole("button", { name: "Redact selected (1)" }).click();
    await expect(
      dialog.getByText(
        "No common credential, email, or path patterns detected.",
      ),
    ).toBeVisible();
    await expect(dialog.locator(".publication-preview")).not.toContainText(
      findings[1],
    );
    await confirmation.check();
    await expect(publish).toBeEnabled();
  });
}

test("a failed batch preview can be retried without losing the redactions", async ({
  page,
}) => {
  const { dialog } = await mount(page);
  await dialog.getByRole("button", { name: "Select all findings" }).click();
  await page.route(
    "**/publication-preview",
    (route) =>
      route.fulfill({ status: 500, json: { error: "Preview unavailable" } }),
    { times: 1 },
  );
  await dialog.getByRole("button", { name: "Redact selected (3)" }).click();
  const review = dialog.getByRole("button", {
    name: "Review sanitized preview",
  });
  await expect(review).toBeEnabled();
  await expect(
    dialog.getByRole("button", { name: "Create public link" }),
  ).toHaveCount(0);
  await expect(
    dialog.getByLabel("Redact exact text (one phrase per line)"),
  ).toHaveValue(["private note", ...findings].join("\n"));
  await review.click();
  await expect(
    dialog.getByText("No common credential, email, or path patterns detected."),
  ).toBeVisible();
  await expect(
    dialog.getByRole("button", { name: "Create public link" }),
  ).toBeDisabled();
});

test("an earlier preview cannot replace a newer redaction review", async ({
  page,
}) => {
  const { dialog } = await mount(page);
  let release!: () => void;
  const pending = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(
    "**/publication-preview",
    async (route) => {
      const body = route.request().postDataJSON();
      await pending;
      await route.fulfill({ json: preview(body) });
    },
    { times: 1 },
  );
  const earlierRequest = page.waitForRequest("**/publication-preview");
  await dialog
    .getByRole("button", { name: "Review sanitized preview" })
    .click();
  await earlierRequest;
  await dialog
    .getByLabel("Redact exact text (one phrase per line)")
    .fill(["private note", ...findings].join("\n"));
  await dialog
    .getByRole("button", { name: "Review sanitized preview" })
    .click();
  await expect(
    dialog.getByText("No common credential, email, or path patterns detected."),
  ).toBeVisible();
  const response = page.waitForResponse("**/publication-preview");
  release();
  await response;
  await dialog
    .getByLabel(
      "I reviewed the included content and explicitly confirm publication.",
    )
    .check();
  await expect(dialog.locator(".finding")).toHaveCount(0);
  await expect(dialog.locator(".publication-preview")).not.toContainText(
    findings[0],
  );
  await expect(
    dialog.getByRole("button", { name: "Create public link" }),
  ).toBeEnabled();
});

test("a custom shared title is reviewed before publishing and changing it resets confirmation", async ({
  page,
}) => {
  const { dialog, requests } = await mount(page);
  const title = dialog.getByLabel("Shared conversation title (optional)");
  await expect(title).toHaveAttribute("placeholder", conversation.title);
  await title.fill("Scaling strategy review");
  await expect(dialog.locator(".publication-preview")).toHaveCount(0);
  await dialog
    .getByRole("button", { name: "Review sanitized preview" })
    .click();
  await expect(dialog.locator(".publication-preview h1")).toHaveText(
    "Scaling strategy review",
  );
  expect(requests.at(-1)?.title).toBe("Scaling strategy review");
  await dialog
    .getByLabel(
      "I reviewed the included content and explicitly confirm publication.",
    )
    .check();
  await title.fill("Final scaling review");
  await expect(
    dialog.getByRole("button", { name: "Create public link" }),
  ).toHaveCount(0);
  await dialog
    .getByRole("button", { name: "Review sanitized preview" })
    .click();
  await expect(dialog.locator(".publication-preview h1")).toHaveText(
    "Final scaling review",
  );
  await expect(
    dialog.getByRole("button", { name: "Create public link" }),
  ).toBeDisabled();
  await dialog
    .getByLabel(
      "I reviewed the included content and explicitly confirm publication.",
    )
    .check();
  let published: PreviewRequest | undefined;
  await page.route("**/api/conversations/redaction-demo/publish", (route) => {
    published = route.request().postDataJSON();
    return route.fulfill({ json: { slug: "friendly-review" } });
  });
  await dialog.getByRole("button", { name: "Create public link" }).click();
  await expect(
    dialog.getByRole("heading", { name: "Your snapshot is ready" }),
  ).toBeVisible();
  expect(published?.title).toBe("Final scaling review");
  expect(published?.confirm).toBe(true);
});
