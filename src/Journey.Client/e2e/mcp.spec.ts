import { test, expect } from "@playwright/test";
import { accountBase } from "./account-helpers";
test("MCP connection, proposal, human approval, revocation and mobile layout", async ({
  page,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  const headers = { "X-Journey": "1" };
  expect(
    (
      await page.request.post(accountBase + "/api/auth/login", {
        headers,
        data: { email: "alex@journey.local", password: "JourneyDemo!2026" },
      })
    ).ok(),
  ).toBeTruthy();
  const created = await page.request.post(
    accountBase + "/api/w/cloudplay/journeys",
    { headers, data: { name: "MCP browser test " + Date.now() } },
  );
  expect(created.ok()).toBeTruthy();
  const journey = await created.json();
  await page.goto(accountBase + "/#integrations");
  await expect(
    page.getByRole("heading", { name: "Your journey, in context." }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Add connection", exact: true })
    .click();
  let dialog = page.getByRole("dialog", {
    name: "Add connection",
    exact: true,
  });
  await dialog.getByLabel("Connection name").fill("MCP browser test");
  await dialog.getByLabel(journey.title, { exact: true }).check();
  await dialog.getByLabel("Allow proposals for review").check();
  const response = page.waitForResponse(
    (r) => r.url().endsWith("/connections") && r.request().method() === "POST",
  );
  await dialog
    .getByRole("button", { name: "Create connection", exact: true })
    .click();
  const grant = await (await response).json();
  dialog = page.getByRole("dialog", {
    name: "Connection created",
    exact: true,
  });
  await expect(
    dialog.getByText(
      "Save this credential now. It is shown only once. Keep it private.",
    ),
  ).toBeVisible();
  await dialog.getByRole("button", { name: "Done", exact: true }).click();
  expect(await page.locator("body").innerText()).not.toContain(grant.token);
  const rpc = async (method: string, params: unknown, id = 1) => {
    const r = await page.request.post(accountBase + "/mcp", {
      headers: {
        Authorization: "Bearer " + grant.token,
        Accept: "application/json, text/event-stream",
        "MCP-Protocol-Version": "2025-11-25",
      },
      data: { jsonrpc: "2.0", id, method, params },
    });
    expect(r.ok()).toBeTruthy();
    const text = await r.text();
    return JSON.parse(
      text.startsWith("{")
        ? text
        : text
            .split("\n")
            .find((l) => l.startsWith("data: "))!
            .slice(6),
    );
  };
  await rpc("initialize", {
    protocolVersion: "2025-11-25",
    capabilities: {},
    clientInfo: { name: "openhow-browser-test", version: "1.0" },
  });
  const proposed = await rpc(
    "tools/call",
    {
      name: "propose_milestone",
      arguments: {
        journeyId: journey.id,
        data: {
          title: "Browser approval milestone",
          body: "This must remain pending until a user approves.",
        },
      },
    },
    2,
  );
  expect(proposed.result.isError).not.toBe(true);
  await page.getByRole("button", { name: "Refresh", exact: true }).click();
  const row = page
    .locator(".mcp-connection")
    .filter({ hasText: "Browser approval milestone" });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Review", exact: true }).click();
  dialog = page.getByRole("dialog", { name: "Review proposal", exact: true });
  await expect(
    dialog.getByRole("button", { name: "Approve and apply" }),
  ).toBeDisabled();
  await dialog
    .getByLabel("I reviewed this proposal and confirm my decision.")
    .check();
  await dialog.getByRole("button", { name: "Approve and apply" }).click();
  await expect(dialog).toHaveCount(0);
  await expect(row).toHaveCount(0);
  const updated = await (
    await page.request.get(accountBase + "/api/journeys/" + journey.id)
  ).json();
  expect(updated.visibility).toBe("Private");
  expect(JSON.parse(updated.graph).nodes[0].data.title).toBe(
    "Browser approval milestone",
  );
  await page.screenshot({
    path: "../../artifacts/mcp-integrations-desktop.png",
    fullPage: true,
  });
  const connection = page
    .locator(".mcp-connection")
    .filter({ hasText: journey.title });
  await connection.getByRole("button", { name: "Revoke", exact: true }).click();
  await expect(connection.getByText("Revoked", { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({
    path: "../../artifacts/mcp-integrations-mobile.png",
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  expect(errors).toEqual([]);
  await page.request.delete(accountBase + "/api/journeys/" + journey.id, {
    headers,
  });
});
