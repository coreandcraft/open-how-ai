import { test, expect } from "@playwright/test";
import {
  topicTags,
  threadTags,
  videoEmbed,
} from "../src/features/review/annotationParsing";

test("topics normalize, deduplicate and exclude headings and URL fragments", () => {
  expect(
    topicTags(
      "#Architecture #operations #ARCHITECTURE #重要\n# Heading https://example.com/#ignored",
    ),
  ).toEqual(["architecture", "operations", "重要"]);
  expect(
    threadTags({
      comments: [{ body: "#action" }, { text: "#important #action" }],
    }),
  ).toEqual(["action", "important"]);
});

test("video links use validated providers and preserve timestamps and unlisted hashes", () => {
  for (const url of [
    "https://youtu.be/dQw4w9WgXcQ",
    "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
    "https://youtube.com/shorts/dQw4w9WgXcQ",
  ])
    expect(videoEmbed(url)).toBe(
      "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ",
    );
  expect(videoEmbed("https://youtu.be/dQw4w9WgXcQ?t=1m30s")).toContain(
    "start=90",
  );
  expect(videoEmbed("https://vimeo.com/12345678/abc123")).toBe(
    "https://player.vimeo.com/video/12345678?h=abc123",
  );
  for (const url of [
    "javascript:alert(1)",
    "https://youtube.com.evil.test/watch?v=dQw4w9WgXcQ",
    "https://evil.test/?v=dQw4w9WgXcQ",
    "https://user@youtube.com/watch?v=dQw4w9WgXcQ",
    "https://youtube.com/watch?v=invalid",
    "http://youtu.be/dQw4w9WgXcQ",
  ])
    expect(videoEmbed(url)).toBeNull();
});

test("topic controls, callouts and click-to-load video work on a narrow screen", async ({
  page,
}) => {
  page.on("pageerror", error => { throw error; });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route("**/annotation-test", (route) =>
    route.fulfill({
      contentType: "text/html",
      body: `<html><head></head><body><div id="test"></div><script type="module">
    import RefreshRuntime from '/@react-refresh';
    RefreshRuntime.injectIntoGlobalHook(window);
    window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => (type) => type;
    window.__vite_plugin_react_preamble_installed__ = true;
    const { default: React } = await import('/node_modules/.vite/deps/react.js');
    const { default: { createRoot } } = await import('/node_modules/.vite/deps/react-dom_client.js');
    const { AnnotationContent, AnnotationTopics, AnnotationTools, TopicFilter } = await import('/src/features/review/AnnotationContent.tsx');
    function App() {
      const [body, setBody] = React.useState('Review deployment https://youtu.be/dQw4w9WgXcQ');
      const [filter, setFilter] = React.useState('');
      const annotation = { comments: [{body}], resolved: false };
      return React.createElement('main', {},
        React.createElement(AnnotationTools, {body, setBody}),
        React.createElement(TopicFilter, {annotations: [annotation], value: filter, onChange: setFilter}),
        React.createElement(AnnotationTopics, {annotation}),
        React.createElement(AnnotationContent, {body}));
    }
    createRoot(document.getElementById('test')).render(React.createElement(App));
  </script></body></html>`,
    }),
  );
  await page.route("https://www.youtube-nocookie.com/**", (route) =>
    route.fulfill({ body: "Video provider placeholder" }),
  );
  await page.goto("http://127.0.0.1:5188/annotation-test");
  await page
    .getByRole("button", { name: "#architecture", exact: true })
    .click();
  await page.getByRole("button", { name: "#action", exact: true }).click();
  await expect(
    page.getByText("Action required", { exact: true }),
  ).toBeVisible();
  await page.getByLabel("Filter by topic").selectOption("architecture");
  await expect(page.getByLabel("Filter by topic")).toHaveValue("architecture");
  await expect(page.locator("iframe")).toHaveCount(0);
  await page.getByRole("button", { name: "Load video", exact: true }).click();
  await expect(page.locator("iframe")).toHaveAttribute(
    "src",
    "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ",
  );
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
});
