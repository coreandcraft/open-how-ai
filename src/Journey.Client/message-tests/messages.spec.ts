import { test, expect, type Page } from "@playwright/test";
import {
  embeddedImages,
  embeddedMarkdown,
  imageDataUrl,
} from "../src/features/conversations/embeddedImages.ts";
import {
  blockHasContent,
  describeActivity,
  describeTool,
  groupSourceBlocks,
  messageHasContent,
  pointerView,
  sourceText,
  type SourceBlock,
} from "../src/features/conversations/messagePresentation.ts";

const payload = JSON.stringify({
  type: "tool_use",
  name: "Read",
  input: { path: "C:\\_Dev\\JourneyAI\\docs\\scaling-strategy.md" },
});
const block = (
  id: string,
  kind: string,
  text: string,
  order = 0,
): SourceBlock => ({ id, kind, text, order });
const call = block("read-call", "tool_use", payload, 1);
const pixel =
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jQ4kAAAAASUVORK5CYII=";
const imageUrl = "data:image/png;base64," + pixel;
const largeImageUrl =
  "data:image/png;base64," +
  Buffer.concat([Buffer.from(pixel, "base64"), Buffer.alloc(60_000)]).toString(
    "base64",
  );
const messages = [
  {
    id: "user",
    role: "user",
    order: 0,
    timestamp: "2026-10-09T17:02:00Z",
    blocks: [
      block(
        "prompt",
        "text",
        "In docs there is a scaling strategy - review and validate want a second opinion.",
      ),
    ],
  },
  {
    id: "assistant",
    role: "assistant",
    order: 1,
    parentId: "user",
    blocks: [
      block(
        "response",
        "text",
        "I'll read the scaling strategy and the supporting evidence (baseline data, bench tool, infra) to check its claims against the code.\n\nI'll check the scaling strategy doc and related files for more context.",
      ),
      call,
      block("unknown-call", "tool_call", "{unparsed original call", 2),
      block("unnamed-call", "tool_use", '{"type":"tool_use","input":{}}', 3),
      block("continuation", "text", "The doc's numbers match the raw JSON.", 4),
      block(
        "later-call",
        "function_call",
        JSON.stringify({
          name: "exec_command",
          arguments: JSON.stringify({ cmd: "git status" }),
        }),
        5,
      ),
    ],
  },
  ...["system", "developer", "tool", "function", "custom-role"].map(
    (role, index) => ({
      id: role,
      role,
      order: index + 2,
      parentId: "assistant",
      blocks: [
        block(role + "-content", "text", "Preserved " + role + " message"),
      ],
    }),
  ),
];

test("tool labels handle supported source shapes and malformed payloads", () => {
  expect(describeTool(call)).toMatchObject({
    isCall: true,
    name: "Read",
    target: "scaling-strategy.md",
    icon: "read",
  });
  const apiCall = block(
    "api",
    "tool_call",
    JSON.stringify({
      type: "function",
      function: {
        name: "grep",
        arguments: JSON.stringify({ pattern: "throughput" }),
      },
    }),
  );
  expect(describeTool(apiCall)).toMatchObject({
    isCall: true,
    name: "grep",
    target: "throughput",
    icon: "search",
  });
  expect(describeTool({ ...call, text: "", metadata: payload })).toMatchObject({
    name: "Read",
  });
  for (const text of [
    "{invalid",
    "null",
    "[]",
    "42",
    '"string"',
    "x".repeat(100_000),
  ]) {
    expect(describeTool(block("unknown", "tool_use", text))).toMatchObject({
      isCall: true,
      name: "",
      target: "",
      icon: "tool",
    });
  }
  expect(
    describeTool(
      block("result", "tool_result", '{"type":"tool_result","name":"Read"}'),
    ),
  ).toMatchObject({ isCall: false, isResult: true });
  expect(call.text).toBe(payload);
});

test("activity labels recover thoughts, recaps, and structured source text", () => {
  const thoughts = {
    content_type: "thoughts",
    thoughts: [{ summary: "Considering the request", finished: true }],
  };
  const recap = {
    content: "worked for em 22's",
    content_type: "reasoning_recaps",
  };
  expect(
    describeActivity({
      id: "thoughts",
      kind: "unknown",
      text: "",
      metadata: JSON.stringify(thoughts),
      order: 0,
    }),
  ).toMatchObject({
    tone: "progress",
    label: "Thinking",
    preview: "Considering the request",
  });
  expect(
    describeActivity({
      id: "recap",
      kind: "unknown",
      text: recap.content,
      metadata: JSON.stringify(recap),
      order: 1,
    }),
  ).toMatchObject({
    tone: "result",
    label: "Reasoning recap",
    preview: "worked for em 22's",
  });
  expect(
    sourceText({
      id: "recap",
      kind: "unknown",
      text: recap.content,
      metadata: JSON.stringify(recap),
      order: 1,
    }),
  ).toBe(JSON.stringify(recap));
  expect(describeActivity(block("plain", "unknown", "not json")).label).toBe(
    "Preserved source",
  );
});

test("blank message shells stay hidden while image pointers remain content", () => {
  expect(blockHasContent(block("empty", "text", "  "))).toBe(false);
  expect(messageHasContent({ blocks: [block("empty", "text", "")] })).toBe(
    false,
  );
  const image = {
    id: "img",
    kind: "image_asset_pointer",
    text: "",
    metadata: JSON.stringify({
      content_type: "image_asset_pointer",
      asset_pointer: "sediment://file_000000009000820d9bc1f10113679080",
    }),
    order: 0,
  };
  expect(pointerView(image)).toBe("image");
  expect(blockHasContent(image)).toBe(true);
  expect(
    messageHasContent({ blocks: [block("empty", "text", ""), image] }),
  ).toBe(true);
});

test("grouping preserves narration and results without mutating source order", () => {
  const blocks = [
    block("last", "text", "After", 5),
    block("first", "text", "Before", 0),
    call,
    block("next", "tool_call", "{}", 2),
    block("result", "tool_result", "{}", 3),
    block("later", "tool_use", "{}", 4),
  ];
  const originalOrder = blocks.map((b) => b.id);
  expect(
    groupSourceBlocks(blocks).map((g) => ({
      tools: g.tools,
      ids: g.blocks.map((b) => b.id),
    })),
  ).toEqual([
    { tools: false, ids: ["first"] },
    { tools: true, ids: ["read-call", "next"] },
    { tools: false, ids: ["result"] },
    { tools: true, ids: ["later"] },
    { tools: false, ids: ["last"] },
  ]);
  expect(blocks.map((b) => b.id)).toEqual(originalOrder);
});

async function mount(
  page: Page,
  isolatedMessage: boolean | { message: object; plain?: boolean } = false,
) {
  await page.route("**/api/**", (route) => {
    const pathname = new URL(route.request().url()).pathname;
    const data =
      pathname === "/api/conversations/role-demo"
        ? {
            id: "role-demo",
            title: "Scaling strategy review",
            provider: "Cursor",
            workspaceId: "workspace",
            sourceId: "source",
            sourceFormat: "Imported source",
            messageCount: messages.length,
            messages,
          }
        : pathname.endsWith("/recovered-source")
          ? { attachments: [], executions: [] }
          : pathname.endsWith("/library")
            ? { items: [], nextCursor: null }
            : [];
    return route.fulfill({ json: data });
  });
  const props =
    isolatedMessage === true
      ? { message: messages[1], plain: true }
      : isolatedMessage
        ? { plain: false, ...isolatedMessage }
        : {
            id: "role-demo",
            user: { id: "reviewer" },
            members: [],
            canComment: true,
            revision: 0,
          };
  await page.route("**/message-test", (route) =>
    route.fulfill({
      contentType: "text/html",
      body: [
        '<html><head><meta name="viewport" content="width=device-width, initial-scale=1" /></head><body><div id="test" style="display:flex;min-height:100vh"></div><script type="module">',
        "import RefreshRuntime from '/@react-refresh';",
        "RefreshRuntime.injectIntoGlobalHook(window);",
        "window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => (type) => type;",
        "window.__vite_plugin_react_preamble_installed__ = true;",
        "await import('/src/app/styles.css'); await import('/src/app/blueprint.css');",
        "const { default: React } = await import('/node_modules/.vite/deps/react.js');",
        "const { default: { createRoot } } = await import('/node_modules/.vite/deps/react-dom_client.js');",
        "const { Explorer, Message } = await import('/src/features/conversations/Explorer.tsx');",
        "const { setLocale } = await import('/src/shared/i18n/i18n.ts'); window.setTestLocale = setLocale;",
        "const props = " + JSON.stringify(props) + ";",
        "props.onSelect = (selection) => { window.selectedSource = selection; };",
        "createRoot(document.getElementById('test')).render(React.createElement(" +
          (isolatedMessage ? "Message" : "Explorer") +
          ", props));",
        "</script></body></html>",
      ].join("\n"),
    }),
  );
  await page.goto("/message-test");
}

test("embedded image detection supports large provider payloads and preserves source", () => {
  for (const data of [
    { type: "input_image", image_url: largeImageUrl },
    { type: "image_url", image_url: { url: largeImageUrl } },
    {
      type: "image",
      source: {
        type: "base64",
        media_type: "image/png",
        data: largeImageUrl.split(",")[1],
      },
    },
    {
      inlineData: { mimeType: "image/png", data: largeImageUrl.split(",")[1] },
    },
    [{ type: "input_image", image_url: largeImageUrl }],
  ]) {
    const original = JSON.stringify(data);
    const image = { ...block("image", "unknown", ""), metadata: original };
    expect(embeddedImages(image)).toEqual([largeImageUrl]);
    expect(blockHasContent(image)).toBe(true);
    expect(sourceText(image)).toBe(original);
    expect(embeddedImages({ ...image, text: original })).toEqual([
      largeImageUrl,
    ]);
  }
  expect(embeddedImages(block("image", "image", imageUrl))).toEqual([imageUrl]);
  for (const value of [
    "{bad json",
    "data:image/png;base64,??",
    "data:image/png;base64,A",
    "data:image/svg+xml;base64,PHN2Zz4=",
    "https://example.com/image.png",
    "javascript:alert(1)",
  ]) {
    expect(imageDataUrl(value)).toBeNull();
    expect(embeddedImages(block("image", "unknown", value))).toEqual([]);
  }
  const markdown = `Before\n\n![Example](${largeImageUrl})\n\nAfter`;
  const prepared = embeddedMarkdown(markdown);
  expect(prepared.markdown.length).toBeLessThan(100);
  expect([...prepared.images.values()]).toEqual([largeImageUrl]);
  for (const example of [
    `\`![Example](${imageUrl})\``,
    `\`\`\`md\n![Example](${imageUrl})\n\`\`\``,
  ]) {
    expect(embeddedMarkdown(example).markdown).toBe(example);
    expect(embeddedMarkdown(example).images.size).toBe(0);
  }
});

for (const width of [1440, 390]) {
  test(`base64 images render with collapsed original source at ${width}px`, async ({
    page,
  }, testInfo) => {
    const errors: string[] = [];
    page.on("pageerror", (error) => errors.push(error.message));
    await page.setViewportSize({ width, height: 1000 });
    const original = JSON.stringify({
      type: "input_image",
      image_url: largeImageUrl,
    });
    const markdown = `Before image\n\n![Example](${largeImageUrl})\n\nAfter image\n\n![Remote](https://example.com/private.png)`;
    await mount(page, {
      message: {
        id: "images",
        role: "user",
        blocks: [
          { ...block("image", "input_image", ""), metadata: original },
          block("markdown", "markdown", markdown, 1),
        ],
      },
    });
    const previews = page.locator("img.embedded-image-preview");
    await expect(previews).toHaveCount(2);
    for (const preview of await previews.all()) {
      await expect(preview).toBeVisible();
      await expect
        .poll(() =>
          preview.evaluate((img: HTMLImageElement) => img.naturalWidth),
        )
        .toBe(1);
    }
    await expect(page.getByText("Before image", { exact: true })).toBeVisible();
    await expect(page.getByText("After image", { exact: true })).toBeVisible();
    await expect(page.locator('img[src^="https:"]')).toHaveCount(0);
    const disclosures = page.locator(".tool-call");
    await expect(disclosures).toHaveCount(2);
    await expect(disclosures.nth(0)).toHaveJSProperty("open", false);
    await expect(disclosures.nth(1)).toHaveJSProperty("open", false);
    await page.screenshot({ path: testInfo.outputPath("embedded-images.png") });
    await disclosures.first().locator("> summary").focus();
    await page.keyboard.press("Enter");
    await expect(disclosures.first().locator("pre")).toHaveText(original);
    await disclosures.nth(1).locator("> summary").click();
    await expect(disclosures.nth(1).locator("pre")).toHaveText(markdown);
    expect(
      await page
        .locator("article.message")
        .evaluate((el) => el.scrollWidth <= el.clientWidth + 1),
    ).toBeTruthy();
    expect(errors).toEqual([]);
  });
}

test("undecodable embedded images keep the original source accessible", async ({
  page,
}) => {
  const original = JSON.stringify({
    type: "input_image",
    image_url: "data:image/png;base64,AAAA",
  });
  await mount(page, {
    message: {
      id: "broken-image",
      role: "user",
      blocks: [block("image", "input_image", original)],
    },
  });
  await expect(page.getByText("Image preview unavailable.")).toBeVisible();
  await page.getByText("View source block", { exact: true }).click();
  await expect(page.locator(".tool-call pre")).toHaveText(original);
});

for (const width of [1440, 390]) {
  test(
    "roles, source disclosures and theme remain usable at " + width + "px",
    async ({ page }, testInfo) => {
      const errors: string[] = [];
      page.on("pageerror", (error) => errors.push(error.message));
      await page.setViewportSize({ width, height: 1000 });
      await mount(page);
      await expect(page.locator("article.message")).toHaveCount(
        messages.length,
      );
      for (const role of [
        "user",
        "assistant",
        "system",
        "developer",
        "tool",
        "function",
        "custom-role",
      ]) {
        await expect(
          page.locator(
            'article[data-message-role="' + role + '"] > header .message-role',
          ),
        ).toHaveText(role);
        await expect(
          page.locator(
            '.outline-message[data-message-role="' + role + '"] .message-role',
          ),
        ).toHaveText(role);
      }
      const assistant = page.locator("#message-assistant");
      await expect(
        assistant.getByRole("region", { name: "Tool calls", exact: true }),
      ).toHaveCount(2);
      const callDetails = assistant.locator(".tool-call").first();
      await callDetails.locator("> summary").focus();
      await page.keyboard.press("Enter");
      const input = callDetails.locator('.json-node[data-json-key="input"]');
      await expect(input).toHaveJSProperty("open", true);
      await expect(input.locator(".json-value")).toContainText(
        "scaling-strategy.md",
      );
      await input.locator("> summary").click();
      await expect(input).toHaveJSProperty("open", false);
      await expect(input.locator(".json-value")).toBeHidden();
      await input.locator("> summary").click();
      await expect(input.locator(".json-value")).toContainText(
        "scaling-strategy.md",
      );
      await expect(callDetails.locator(".tool-call-target")).toHaveText(
        "scaling-strategy.md",
      );
      await assistant.locator(".tool-call").nth(1).locator("> summary").click();
      await expect(
        assistant.locator(".tool-call").nth(1).locator("pre"),
      ).toHaveText("{unparsed original call");
      const command = assistant.locator(".tool-call").nth(3);
      await command.locator("> summary").click();
      const argumentsField = command.locator(
        '.json-node[data-json-key="arguments"]',
      );
      await expect(
        argumentsField.locator('[data-json-key="cmd"]'),
      ).toContainText("git status");
      await argumentsField.locator("> summary").click();
      await expect(argumentsField).toHaveJSProperty("open", false);
      await expect(
        argumentsField.locator('[data-json-key="cmd"]'),
      ).toBeHidden();
      await expect(
        assistant.locator(".tool-call").nth(1).locator("strong"),
      ).toHaveText("Tool call");
      const color = (selector: string) =>
        page
          .locator(selector)
          .first()
          .evaluate((el) => getComputedStyle(el).color);
      expect(await color("article.user .message-role")).not.toBe(
        await color("article.assistant .message-role"),
      );
      expect(await color("article.user .message-role")).toBe(
        await color('.outline-message[data-message-role="user"] .message-role'),
      );
      await page
        .getByRole("button", { name: "Source text", exact: true })
        .click();
      await expect(callDetails.locator("pre")).toHaveText(payload);
      expect(
        await page
          .locator(".reader")
          .evaluate((el) => el.scrollWidth <= el.clientWidth + 1),
      ).toBeTruthy();
      await page.screenshot({ path: testInfo.outputPath("roles-light.png") });
      await page.evaluate(() => {
        document.documentElement.dataset.theme = "dark";
      });
      await page.screenshot({ path: testInfo.outputPath("roles-dark.png") });
      await page.evaluate(() => (window as any).setTestLocale("es"));
      await expect(page.locator("article.user .message-role")).toHaveText(
        "usuario",
      );
      await expect(
        assistant.getByRole("region", { name: "Llamadas a herramientas" }),
      ).toHaveCount(2);
      expect(errors).toEqual([]);
    },
  );
}

test("source selection offsets exclude the new tool labels", async ({
  page,
}) => {
  await mount(page, true);
  const callDetails = page.locator(".tool-call").first();
  await callDetails.locator("> summary").click();
  await callDetails.locator("pre").evaluate((el) => {
    const source = el.firstChild!;
    const start = source.textContent!.indexOf("scaling-strategy.md");
    const range = document.createRange();
    range.setStart(source, start);
    range.setEnd(source, start + "scaling-strategy.md".length);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
    el.dispatchEvent(new MouseEvent("mouseup", { bubbles: true }));
  });
  await expect
    .poll(() => page.evaluate(() => (window as any).selectedSource))
    .toMatchObject({
      blockId: "read-call",
      quote: "scaling-strategy.md",
      start: payload.indexOf("scaling-strategy.md"),
      end:
        payload.indexOf("scaling-strategy.md") + "scaling-strategy.md".length,
    });
});

test("unknown structured blocks use the json viewer as progress and result indicators", async ({
  page,
}) => {
  const thoughts = {
    content_type: "thoughts",
    thoughts: [{ summary: "Considering the request", finished: true }],
  };
  const recap = {
    content: "worked for em 22's",
    content_type: "reasoning_recaps",
  };
  await mount(page, {
    message: {
      id: "assistant",
      role: "assistant",
      order: 1,
      blocks: [
        {
          id: "thoughts",
          kind: "unknown",
          text: "",
          metadata: JSON.stringify(thoughts),
          order: 0,
        },
        {
          id: "recap",
          kind: "unknown",
          text: recap.content,
          metadata: JSON.stringify(recap),
          order: 1,
        },
      ],
    },
  });
  await expect(page.getByText("unknown · preserved source block")).toHaveCount(
    0,
  );
  const thinking = page.locator('[data-activity="progress"]');
  const result = page.locator('[data-activity="result"]');
  await expect(thinking.locator("strong")).toHaveText("Thinking");
  await expect(thinking.locator(".activity-mark")).toHaveText("Progress");
  await expect(thinking.locator(".tool-call-target")).toHaveText(
    "Considering the request",
  );
  await expect(result.locator("strong")).toHaveText("Reasoning recap");
  await expect(result.locator(".activity-mark")).toHaveText("Result");
  await expect(result.locator(".tool-call-target")).toHaveText(
    "worked for em 22's",
  );
  await result.locator("> summary").click();
  await expect(
    result.locator('.json-leaf[data-json-key="content"] .json-value'),
  ).toContainText("worked for em 22's");
  await expect(
    result.locator('.json-leaf[data-json-key="content_type"] .json-value'),
  ).toContainText("reasoning_recaps");
  await thinking.locator("> summary").click();
  await expect(
    thinking.locator('.json-leaf[data-json-key="content_type"] .json-value'),
  ).toContainText("thoughts");
});
