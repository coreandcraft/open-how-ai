import { expect, type APIRequestContext } from "@playwright/test";
import { readdir, readFile, stat } from "node:fs/promises";
import path from "node:path";

export const accountBase =
  process.env.JOURNEY_TEST_URL || "http://127.0.0.1:5080";
const mailDirectory =
  process.env.JOURNEY_TEST_MAIL || path.resolve("../Journey.Server/data/mail");

export async function emailContent(email: string, action = "Confirm email") {
  let result: { html: string; link: string } | undefined;
  await expect
    .poll(
      async () => {
        const files = await readdir(mailDirectory).catch(() => [] as string[]);
        const ordered = await Promise.all(
          files
            .filter((f) => f.endsWith(".eml"))
            .map(async (f) => ({
              f,
              time: (await stat(path.join(mailDirectory, f))).mtimeMs,
            })),
        );
        for (const { f } of ordered.sort((a, b) => b.time - a.time)) {
          const raw = await readFile(path.join(mailDirectory, f), "utf8");
          if (!raw.includes("To: " + email + "\r\n")) continue;
          const match = raw.match(
            /Content-Type: text\/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n([\s\S]*?)(?=\r\n--)/,
          );
          if (!match) continue;
          const html = Buffer.from(
            match[1].replace(/\s/g, ""),
            "base64",
          ).toString("utf8");
          if (!html.includes(">" + action + "</a>")) continue;
          const link = html.match(/href="([^"]+)"/)?.[1].replace(/&amp;/g, "&");
          if (link) {
            result = { html, link };
            return true;
          }
        }
        return false;
      },
      {
        message:
          "Account email should arrive in the local SMTP pickup directory",
      },
    )
    .toBe(true);
  return result!;
}

export async function confirmAndLogin(
  request: APIRequestContext,
  email: string,
  password: string,
  base = accountBase,
) {
  const { link } = await emailContent(email);
  const params = new URLSearchParams(new URL(link).hash.split("?")[1]);
  expect(
    (
      await request.post(base + "/api/auth/confirm-email", {
        headers: { "X-Journey": "1" },
        data: { userId: params.get("userId"), code: params.get("code") },
      })
    ).ok(),
  ).toBeTruthy();
  expect(
    (
      await request.post(base + "/api/auth/login", {
        headers: { "X-Journey": "1" },
        data: { email, password },
      })
    ).ok(),
  ).toBeTruthy();
}
