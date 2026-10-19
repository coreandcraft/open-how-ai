import { test, expect, type Page } from "@playwright/test";
import {
  githubSourceLink,
  repositoryPath,
  type GitHubSource,
} from "../src/features/conversations/githubSource";
import type { SourceBlock } from "../src/features/conversations/messagePresentation";

const commit = "0123456789abcdef0123456789abcdef01234567";
const source: GitHubSource = {
  repositoryUrl: "https://github.com/acme/app",
  branch: "main",
  localRoot: "C:/work/app",
  sessionCommit: commit,
  provider: "Claude Code",
};
const read = (
  input: object,
  name = "Read",
  kind = "tool_use",
): SourceBlock => ({
  id: "read-call",
  kind,
  order: 0,
  text: JSON.stringify({ name, input }),
});

test("paths only map inside the configured root and encode filenames", () => {
  expect(repositoryPath("C:\\work\\app\\src\\auth.ts", source.localRoot)).toBe(
    "src/auth.ts",
  );
  expect(repositoryPath("c:\\WORK\\APP\\src\\Auth.ts", source.localRoot)).toBe(
    "src/Auth.ts",
  );
  expect(repositoryPath("./src/auth.ts", null)).toBe("src/auth.ts");
  expect(repositoryPath("/home/app/src/auth.ts", "/home/app")).toBe(
    "src/auth.ts",
  );
  for (const path of [
    "C:/work/app-other/secret",
    "C:/other/secret",
    "../secret",
    "src/../secret",
    ".git/config",
    "src//auth.ts",
    "https://evil.test/file",
    "//host/share/file",
    "src/\0file",
  ]) {
    expect(repositoryPath(path, source.localRoot), path).toBeNull();
  }
  expect(repositoryPath("/home/App/src/auth.ts", "/home/app")).toBeNull();
  expect(repositoryPath("C:/work/app/src/auth.ts", null)).toBeNull();
  expect(githubSourceLink(read({ path: "src/a b#?.ts" }), source)?.url).toBe(
    "https://github.com/acme/app/blob/" + commit + "/src/a%20b%23%3F.ts",
  );
});

test("line anchors respect tool conventions and revision evidence", () => {
  expect(
    githubSourceLink(
      read({ file_path: "src/auth.ts", offset: 42, limit: 8 }),
      source,
    ),
  ).toMatchObject({ pinned: true, startLine: 42, endLine: 49 });
  expect(
    githubSourceLink(
      read(
        {
          target_file: "src/auth.ts",
          start_line_one_indexed: 10,
          end_line_one_indexed_inclusive: 20,
        },
        "read_file",
      ),
      source,
    )?.url,
  ).toContain("#L10-L20");
  expect(
    githubSourceLink(
      read({ path: "src/auth.ts", offset: 0, limit: 3 }, "read_file"),
      { ...source, provider: "Gemini CLI" },
    ),
  ).toMatchObject({ startLine: 1, endLine: 3 });
  expect(
    githubSourceLink(
      read({ path: "src/auth.ts", offset: 42, limit: 8 }, "read_file"),
      source,
    )?.startLine,
  ).toBeUndefined();
  expect(
    githubSourceLink(read({ path: "src/auth.ts" }), {
      ...source,
      sessionCommit: null,
      branch: "release/next",
    }),
  ).toMatchObject({
    pinned: false,
    reference: "release/next",
    url: "https://github.com/acme/app/blob/release%2Fnext/src/auth.ts",
  });
  expect(
    githubSourceLink(
      read({ path: "src/auth.ts", startLine: 9, endLine: 3 }),
      source,
    )?.url,
  ).toContain("#L9");
  for (const tool of ["Write", "Edit", "exec_command", "grep"])
    expect(
      githubSourceLink(read({ path: "src/auth.ts" }, tool), source),
    ).toBeNull();
  expect(
    githubSourceLink(
      read({ path: "src/auth.ts" }, "Read", "tool_result"),
      source,
    ),
  ).toBeNull();
  expect(githubSourceLink(read({ path: "src/auth.ts" }), null)).toBeNull();
});

async function mount(
  page: Page,
  component: "ToolCall" | "GitHubIntegration",
  props: object,
) {
  await page.route("**/github-test", (route) =>
    route.fulfill({
      contentType: "text/html",
      body: [
        '<html><head><meta name="viewport" content="width=device-width, initial-scale=1" /></head><body><div id="test" style="max-width:1200px;margin:40px auto;padding:20px"></div><script type="module">',
        "import RefreshRuntime from '/@react-refresh';",
        "RefreshRuntime.injectIntoGlobalHook(window);",
        "window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => (type) => type;",
        "window.__vite_plugin_react_preamble_installed__ = true;",
        "await import('/src/app/styles.css'); await import('/src/app/blueprint.css'); await import('/src/features/conversations/messages.css');",
        "const { default: React } = await import('/node_modules/.vite/deps/react.js');",
        "const { default: { createRoot } } = await import('/node_modules/.vite/deps/react-dom_client.js');",
        "const { " +
          component +
          " } = await import('/src/features/" +
          (component === "ToolCall"
            ? "conversations/ToolCall"
            : "integrations/GitHubIntegration") +
          ".tsx');",
        "const { useApp } = await import('/src/shared/state.ts'); useApp.setState({ workspace: 'cloudplay' });",
        "const props = " +
          JSON.stringify(props) +
          "; props.sourceRef = () => {};",
        "createRoot(document.getElementById('test')).render(React.createElement(" +
          component +
          ", props));",
        "</script></body></html>",
      ].join("\n"),
    }),
  );
  await page.goto("/github-test");
}

for (const width of [1440, 390]) {
  test(
    "source links and lazy preview preserve payload at " + width + "px",
    async ({ page }, testInfo) => {
      const errors: string[] = [];
      page.on("pageerror", (error) => errors.push(error.message));
      await page.setViewportSize({ width, height: 1000 });
      let requests = 0;
      await page.route(
        "**/api/conversations/conversation/github/file?**",
        (route) => {
          requests++;
          expect(new URL(route.request().url()).searchParams.get("path")).toBe(
            "src/auth.ts",
          );
          return route.fulfill({
            json: {
              startLine: 42,
              totalLines: 100,
              lines: ["function session() {", "  return 'ok';", "}"],
            },
          });
        },
      );
      const block = read({
        file_path: "C:\\work\\app\\src\\auth.ts",
        offset: 42,
        limit: 3,
      });
      await mount(page, "ToolCall", {
        block,
        github: source,
        conversationId: "conversation",
      });
      const link = page.getByRole("link", { name: "View on GitHub" });
      await expect(link).toHaveAttribute(
        "href",
        source.repositoryUrl + "/blob/" + commit + "/src/auth.ts#L42-L44",
      );
      expect(requests).toBe(0);
      await page.locator(".tool-call > summary").click();
      await expect(page.locator(".json-tree")).toBeVisible();
      await expect(page.locator('[data-json-key="file_path"]')).toContainText(
        "auth.ts",
      );
      await expect(page.locator('[data-json-key="offset"]')).toContainText(
        "42",
      );
      await expect(page.locator('[data-json-key="limit"]')).toContainText("3");
      await page.getByRole("button", { name: "Preview source" }).click();
      await expect(page.locator(".github-source-code code").first()).toHaveText(
        "function session() {",
      );
      await expect(
        page.locator(".github-source-code .highlighted"),
      ).toHaveCount(3);
      await expect(
        page.getByText(
          "Local, uncommitted changes may differ from this commit.",
        ),
      ).toBeVisible();
      expect(requests).toBe(1);
      await page.getByRole("button", { name: "Close source preview" }).click();
      await page.getByRole("button", { name: "Preview source" }).click();
      expect(requests).toBe(1);
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
      ).toBeTruthy();
      await page.screenshot({ path: testInfo.outputPath("github-source.png") });
      expect(errors).toEqual([]);
    },
  );
}

test("missing revision and failed previews are explicit without hiding the link", async ({
  page,
}) => {
  await page.route(
    "**/api/conversations/conversation/github/file?**",
    (route) =>
      route.fulfill({
        status: 400,
        json: {
          error:
            "GitHub could not find this public repository, branch, revision, or file.",
        },
      }),
  );
  await mount(page, "ToolCall", {
    block: read({ path: "src/auth.ts" }),
    github: { ...source, sessionCommit: null },
    conversationId: "conversation",
  });
  await expect(
    page.getByRole("link", { name: "Latest on GitHub" }),
  ).toHaveAttribute("href", source.repositoryUrl + "/blob/main/src/auth.ts");
  await expect(
    page.getByText("main · Original revision unknown"),
  ).toBeVisible();
  await page.getByRole("button", { name: "Preview source" }).click();
  await expect(page.getByRole("alert")).toContainText("could not find");
  await expect(
    page.getByRole("link", { name: "Latest on GitHub" }),
  ).toBeVisible();
});

test("project connection saves, reloads and unlinks through workspace routes", async ({
  page,
}, testInfo) => {
  let project = {
    id: "project",
    name: "My application",
    gitHubRepositoryUrl: null as string | null,
    gitHubBranch: null as string | null,
    gitHubLocalRoot: null as string | null,
  };
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/integrations/github/status")
      return route.fulfill({ json: { enabled: true } });
    if (path === "/api/w/cloudplay/projects")
      return route.fulfill({ json: [project] });
    if (path === "/api/w/cloudplay/projects/project/github") {
      if (route.request().method() === "DELETE") {
        project = {
          ...project,
          gitHubRepositoryUrl: null,
          gitHubBranch: null,
          gitHubLocalRoot: null,
        };
        return route.fulfill({ status: 204 });
      }
      const input = route.request().postDataJSON();
      expect(input).toEqual({
        repositoryUrl: "https://github.com/acme/app",
        branch: "",
        localRoot: "C:/work/app",
      });
      project = {
        ...project,
        gitHubRepositoryUrl: input.repositoryUrl,
        gitHubBranch: "main",
        gitHubLocalRoot: input.localRoot,
      };
      return route.fulfill({ json: project });
    }
    throw new Error("Unexpected route " + path);
  });
  await mount(page, "GitHubIntegration", { canEdit: true });
  await page.getByLabel("Repository URL").fill("https://github.com/acme/app");
  await page.getByLabel("Project folder in tool calls").fill("C:/work/app");
  await page.getByRole("button", { name: "Link repository" }).click();
  await expect(page.getByRole("status")).toContainText("Repository linked.");
  await expect(page.getByText("Connected", { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByLabel("Repository URL")).toHaveValue(
    "https://github.com/acme/app",
  );
  await expect(page.getByLabel("Branch", { exact: true })).toHaveValue("main");
  await page.screenshot({
    path: testInfo.outputPath("github-integration.png"),
  });
  await page.getByRole("button", { name: "Unlink", exact: true }).click();
  await expect(page.getByLabel("Repository URL")).toHaveValue("");
  await expect(page.getByText("Connected", { exact: true })).toHaveCount(0);
});

test("disabled feature explains its status and prevents connection", async ({
  page,
}) => {
  await page.route("**/api/**", (route) =>
    route.fulfill({
      json: route.request().url().endsWith("/status")
        ? { enabled: false }
        : [{ id: "project", name: "My application" }],
    }),
  );
  await mount(page, "GitHubIntegration", { canEdit: true });
  await expect(
    page.getByText("The experimental GitHub integration is disabled."),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Link repository" }),
  ).toBeDisabled();
});

test("GitHub App setup includes installation, server settings, and a credential check", async ({
  page,
}, testInfo) => {
  let checked = false;
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/status"))
      return route.fulfill({
        json: {
          enabled: true,
          mode: "App",
          configured: true,
          installationUrl:
            "https://github.com/apps/openhow-test/installations/new",
        },
      });
    if (path.endsWith("/check")) {
      checked = true;
      return route.fulfill({ json: { mode: "App", verified: true } });
    }
    return route.fulfill({ json: [{ id: "project", name: "My application" }] });
  });
  await mount(page, "GitHubIntegration", { canEdit: true });
  await page
    .getByText("GitHub App setup and operations", { exact: true })
    .click();
  await expect(
    page.getByRole("link", { name: "Register a GitHub App", exact: true }),
  ).toHaveAttribute("href", "https://github.com/settings/apps/new");
  await expect(page.locator(".github-setup pre")).toContainText(
    "GitHub__AuthenticationMode=App",
  );
  await expect(
    page.getByRole("link", { name: "Install or manage GitHub App" }),
  ).toHaveAttribute(
    "href",
    "https://github.com/apps/openhow-test/installations/new",
  );
  await page.getByRole("button", { name: "Check App credentials" }).click();
  await expect(page.getByRole("status")).toContainText("credentials verified");
  expect(checked).toBe(true);
  await expect(
    page.getByText(
      "Public repositories only. No GitHub sign-in or token required.",
    ),
  ).toHaveCount(0);
  await page.setViewportSize({ width: 390, height: 1100 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
  await page.screenshot({
    path: testInfo.outputPath("github-app-setup.png"),
    fullPage: true,
  });
});

test("incomplete App configuration blocks connection and explains what is missing", async ({
  page,
}) => {
  await page.route("**/api/**", (route) =>
    route.fulfill({
      json: route.request().url().endsWith("/status")
        ? {
            enabled: true,
            mode: "App",
            configured: false,
            configurationError:
              "GitHub App setup is incomplete. Configure its Client ID, slug, and private key on the server.",
          }
        : [{ id: "project", name: "My application" }],
    }),
  );
  await mount(page, "GitHubIntegration", { canEdit: true });
  await expect(page.getByRole("alert")).toContainText("setup is incomplete");
  await expect(
    page.getByRole("button", { name: "Link repository" }),
  ).toBeDisabled();
  await expect(
    page.getByRole("link", { name: "Install or manage GitHub App" }),
  ).toHaveCount(0);
});
