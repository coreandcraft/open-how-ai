import { getLocale, t } from "./i18n/i18n";
import { useEffect, useState } from "react";
import { create } from "zustand";
export type Page<T = any> = { items: T[]; nextCursor: string | null };
/** Loads a conversation and every message page; the server bounds each response, not the total. */
export async function loadConversation(id: string) {
  const conversation = await api("/conversations/" + id);
  let cursor: string | null = conversation.nextCursor;
  while (cursor) {
    const page: Page = await api(
      "/conversations/" + id + "/messages?cursor=" + encodeURIComponent(cursor),
    );
    conversation.messages.push(...page.items);
    cursor = page.nextCursor;
  }
  return conversation;
}
/** Every visible journey's ID, title and version, for pickers. */
export function useJourneyOptions(workspace: string, revision?: unknown) {
  const [options, setOptions] = useState<
    { id: string; title: string; version: number }[]
  >([]);
  useEffect(() => {
    if (!workspace) return;
    let live = true;
    api("/w/" + workspace + "/journey-options")
      .then((o) => live && setOptions(o))
      .catch((e) => live && useApp.getState().notify(e.message));
    return () => {
      live = false;
    };
  }, [workspace, revision]);
  return options;
}
/** Server-searched, newest-first conversation pages for a workspace. */
export function useLibrary(
  workspace: string,
  filters: {
    q?: string;
    provider?: string;
    project?: string;
    sourceGroup?: string;
    limit?: number;
    sort?: string;
  },
  revision?: unknown,
) {
  const [items, setItems] = useState<any[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const query = (after: string | null) =>
    "/w/" +
    workspace +
    "/library?" +
    new URLSearchParams({
      q: filters.q || "",
      provider: filters.provider || "",
      project: filters.project || "",
      ...(filters.sourceGroup ? { sourceGroup: filters.sourceGroup } : {}),
      limit: String(filters.limit || 50),
      ...(filters.sort ? { sort: filters.sort } : {}),
      ...(after ? { cursor: after } : {}),
    });
  useEffect(() => {
    if (!workspace) return;
    let live = true;
    const timer = setTimeout(() => {
      setLoading(true);
      api<Page>(query(null))
        .then((page) => {
          if (!live) return;
          setItems(page.items);
          setCursor(page.nextCursor);
        })
        .catch((e) => live && useApp.getState().notify(e.message))
        .finally(() => live && setLoading(false));
    }, 180);
    return () => {
      live = false;
      clearTimeout(timer);
    };
  }, [
    workspace,
    filters.q,
    filters.provider,
    filters.project,
    filters.sourceGroup,
    filters.sort,
    filters.limit,
    revision,
  ]);
  const more = () =>
    cursor &&
    act(async () => {
      setLoading(true);
      try {
        const page = await api<Page>(query(cursor));
        setItems((xs) => [...xs, ...page.items]);
        setCursor(page.nextCursor);
      } finally {
        setLoading(false);
      }
    });
  return { items, more: cursor ? more : null, loading };
}
export async function api<T = any>(
  path: string,
  body?: unknown,
  method?: string,
): Promise<T> {
  const r = await fetch("/api" + path, {
    method: method || (body === undefined ? "GET" : "POST"),
    headers: {
      "X-Journey": "1",
      "Accept-Language": getLocale(),
      ...(body instanceof FormData
        ? {}
        : { "Content-Type": "application/json" }),
    },
    body:
      body === undefined
        ? undefined
        : body instanceof FormData
          ? body
          : JSON.stringify(body),
  });
  if (!r.ok) {
    const e = await r.json().catch(() => ({ error: r.statusText }));
    throw Object.assign(new Error(t(e.error || "Request failed")), {
      code: e.code,
      status: r.status,
    });
  }
  return r.status === 204
    ? (undefined as T)
    : await r.json().catch(() => undefined);
}
export const useApp = create<{
  workspace: string;
  setWorkspace: (w: string) => void;
  theme: string;
  toggleTheme: () => void;
  toast: string;
  notify: (s: string) => void;
}>((set) => ({
  workspace: localStorage.getItem("workspace") || "cloudplay",
  setWorkspace: (workspace) => {
    localStorage.setItem("workspace", workspace);
    set({ workspace });
  },
  theme: localStorage.getItem("theme") || "light",
  toggleTheme: () =>
    set((s) => {
      const theme = s.theme === "light" ? "dark" : "light";
      localStorage.setItem("theme", theme);
      return { theme };
    }),
  toast: "",
  notify: (toast) => {
    set({ toast });
    setTimeout(() => set({ toast: "" }), 5000);
  },
}));
export function go(path: string) {
  window.location.hash = path;
}
export function graph(j: any) {
  try {
    return JSON.parse(j.graph);
  } catch {
    return { nodes: [], edges: [], steps: [] };
  }
}
/** The list-card excerpt from a journey summary, or the full graph when that is what was loaded. */
export function preview(j: any) {
  if (!j.preview) return graph(j);
  try {
    return JSON.parse(j.preview);
  } catch {
    return { nodes: [], edges: [] };
  }
}
export function initials(name: string) {
  return name
    .split(" ")
    .map((x) => x[0])
    .slice(0, 2)
    .join("");
}
export function providerClass(p: string) {
  return p?.toLowerCase().includes("claude")
    ? "claude"
    : p?.toLowerCase().includes("cursor")
      ? "cursor"
      : "chatgpt";
}
export async function act(fn: () => Promise<unknown>) {
  try {
    await fn();
  } catch (e) {
    useApp.getState().notify((e as Error).message);
  }
}
