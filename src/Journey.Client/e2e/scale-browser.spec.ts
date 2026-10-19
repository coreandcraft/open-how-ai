import { test, expect } from "@playwright/test";
import { writeFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { accountBase } from "./account-helpers";

type Probe = {
  route: string;
  usableMs: number;
  longTasks: number;
  longTaskMs: number;
  heapMiB: number | null;
  heading: string;
};

async function login(page: import("@playwright/test").Page) {
  await page.goto(accountBase + "/#login");
  await page.getByRole("button", { name: "alex", exact: true }).click();
  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(
    page.getByRole("heading", { name: "Good to see you, Alex." }),
  ).toBeVisible();
}

async function installObservers(page: import("@playwright/test").Page) {
  await page.addInitScript(() => {
    (window as any).__scale = { longTasks: 0, longTaskMs: 0 };
    try {
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
          (window as any).__scale.longTasks++;
          (window as any).__scale.longTaskMs += entry.duration;
        }
      }).observe({ type: "longtask", buffered: true });
    } catch {
      /* Chromium without longtask is still useful for usable timing. */
    }
  });
}

async function probeCanvas(page: import("@playwright/test").Page): Promise<Probe> {
  await page.evaluate(() => {
    (window as any).__scale = { longTasks: 0, longTaskMs: 0 };
  });
  const started = Date.now();
  await page.goto(accountBase + "/#canvas/cloud-gaming");
  await expect(page.locator(".react-flow__node").first()).toBeVisible({
    timeout: 20000,
  });
  const usableMs = Date.now() - started;
  const metrics = await page.evaluate(() => {
    const scale = (window as any).__scale || { longTasks: 0, longTaskMs: 0 };
    const memory = (performance as any).memory;
    return {
      longTasks: scale.longTasks,
      longTaskMs: Math.round(scale.longTaskMs),
      heapMiB: memory ? Math.round((memory.usedJSHeapSize / 1048576) * 10) / 10 : null,
    };
  });
  return { route: "canvas/cloud-gaming", usableMs, heading: "canvas", ...metrics };
}

async function probe(
  page: import("@playwright/test").Page,
  hash: string,
  heading: string | RegExp,
): Promise<Probe> {
  await page.evaluate(() => {
    (window as any).__scale = { longTasks: 0, longTaskMs: 0 };
  });
  const started = Date.now();
  await page.goto(accountBase + "/#" + hash);
  await expect(page.getByRole("heading", { name: heading })).toBeVisible({
    timeout: 20000,
  });
  const usableMs = Date.now() - started;
  const metrics = await page.evaluate(() => {
    const scale = (window as any).__scale || { longTasks: 0, longTaskMs: 0 };
    const memory = (performance as any).memory;
    return {
      longTasks: scale.longTasks,
      longTaskMs: Math.round(scale.longTaskMs),
      heapMiB: memory ? Math.round((memory.usedJSHeapSize / 1048576) * 10) / 10 : null,
    };
  });
  const title =
    typeof heading === "string" ? heading : (await page.getByRole("heading").first().textContent()) || hash;
  return { route: hash, usableMs, heading: title, ...metrics };
}

async function write(name: string, payload: unknown) {
  const dir = path.resolve("../../docs/performance");
  await mkdir(dir, { recursive: true });
  await writeFile(path.join(dir, name), JSON.stringify(payload, null, 2));
}

test("desktop time to usable content on dashboard, library, explorer and canvas", async ({
  page,
}) => {
  await installObservers(page);
  await login(page);
  const probes = [
    await probe(page, "dashboard", "Good to see you, Alex."),
    await probe(page, "library", "Conversation library"),
    await probe(page, "explorer/demo-c0", "The browser is the console"),
    await probeCanvas(page),
  ];
  await write("scale-phase4-browser-desktop-2026-10-10.json", {
    viewport: "1440x1000",
    generatedAtUtc: new Date().toISOString(),
    probes,
  });
  for (const p of probes) expect(p.usableMs).toBeLessThan(8000);
});

test("mobile time to usable content on dashboard and library", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await installObservers(page);
  await login(page);
  const probes = [
    await probe(page, "dashboard", "Good to see you, Alex."),
    await probe(page, "library", "Conversation library"),
    await probe(page, "explorer/demo-c0", "The browser is the console"),
  ];
  await write("scale-phase4-browser-mobile-2026-10-10.json", {
    viewport: "390x844",
    generatedAtUtc: new Date().toISOString(),
    probes,
  });
  for (const p of probes) expect(p.usableMs).toBeLessThan(10000);
});
