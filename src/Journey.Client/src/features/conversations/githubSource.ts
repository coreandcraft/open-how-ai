import { describeTool, type SourceBlock } from "./messagePresentation";

export type GitHubSource = {
  repositoryUrl: string;
  branch: string;
  localRoot: string | null;
  sessionCommit: string | null;
  provider: string;
};
export type SourceLink = {
  url: string;
  path: string;
  reference: string;
  pinned: boolean;
  startLine?: number;
  endLine?: number;
};

// A path must be repo-relative or under the explicitly configured repository root.
export function repositoryPath(
  value: string,
  localRoot: string | null,
): string | null {
  let path = value.replace(/\\/g, "/");
  if (
    path.length > 2048 ||
    /[\x00-\x1f\x7f]/.test(path) ||
    path.includes("://") ||
    path.startsWith("//")
  )
    return null;
  if (path.split("/").some((p) => p === ".." || p === ".git")) return null;
  const absolute = path.startsWith("/") || /^[a-z]:/i.test(path);
  if (absolute) {
    if (!localRoot) return null;
    const root = localRoot.replace(/\\/g, "/").replace(/\/+$/, "");
    const windows = /^[a-z]:\//i.test(path) && /^[a-z]:\//i.test(root);
    const candidate = windows ? path.toLowerCase() : path;
    const prefix = (windows ? root.toLowerCase() : root) + "/";
    if (!candidate.startsWith(prefix)) return null;
    path = path.slice(root.length + 1);
  }
  path = path.replace(/^(\.\/)+/, "");
  if (
    !path ||
    path.includes(":") ||
    path.split("/").some((p) => !p || p === ".")
  )
    return null;
  return path;
}

function integer(value: unknown) {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0
    ? value
    : undefined;
}

export function githubSourceLink(
  block: SourceBlock,
  source?: GitHubSource | null,
): SourceLink | null {
  if (
    !source ||
    !/^https:\/\/github\.com\/[a-z\d-]+\/[a-z\d_.-]+$/i.test(
      source.repositoryUrl,
    )
  )
    return null;
  const tool = describeTool(block);
  if (!tool.isCall || tool.icon !== "read" || !tool.path) return null;
  const path = repositoryPath(tool.path, source.localRoot);
  if (!path) return null;
  const input = tool.input;
  let startLine = integer(
    input.start_line ?? input.startLine ?? input.start_line_one_indexed,
  );
  let endLine = integer(
    input.end_line ?? input.endLine ?? input.end_line_one_indexed_inclusive,
  );
  const name = tool.name.toLowerCase().split(/[./]/).pop();
  if (!startLine && name === "read") {
    startLine = integer(input.offset) ?? (integer(input.limit) ? 1 : undefined);
    const limit = integer(input.limit);
    if (startLine && limit) endLine = startLine + limit - 1;
  } else if (
    !startLine &&
    source.provider === "Gemini CLI" &&
    name === "read_file"
  ) {
    const offset = input.offset ?? 0;
    if (
      typeof offset === "number" &&
      Number.isSafeInteger(offset) &&
      offset >= 0 &&
      integer(input.limit)
    ) {
      startLine = offset + 1;
      endLine = startLine + (input.limit as number) - 1;
    }
  }
  if (startLine && (!Number.isSafeInteger(startLine) || startLine > 10_000_000))
    startLine = undefined;
  if (
    !startLine ||
    !endLine ||
    endLine < startLine ||
    !Number.isSafeInteger(endLine) ||
    endLine > 10_000_000
  )
    endLine = undefined;
  const pinned = /^[a-f\d]{40}$/i.test(source.sessionCommit || "");
  const reference = pinned ? source.sessionCommit! : source.branch;
  if (!reference || /[\x00-\x20\x7f]/.test(reference)) return null;
  const anchor = startLine
    ? `#L${startLine}${endLine && endLine !== startLine ? `-L${endLine}` : ""}`
    : "";
  const url = `${source.repositoryUrl}/blob/${encodeURIComponent(reference)}/${path.split("/").map(encodeURIComponent).join("/")}${anchor}`;
  return { url, path, reference, pinned, startLine, endLine };
}
