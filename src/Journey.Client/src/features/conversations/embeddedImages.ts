import type { SourceBlock } from "./messagePresentation";

// Keep previews local and limited to browser image formats.
const imageDataPattern =
  /^data:image\/(?:png|jpe?g|gif|webp|avif|bmp|x-icon|vnd\.microsoft\.icon);base64,([a-z0-9+/]+={0,2})$/i;

export function imageDataUrl(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const url = value.trim();
  const match = imageDataPattern.exec(url);
  return match && match[1].length % 4 !== 1 ? url : null;
}

export function embeddedImages(block: SourceBlock): string[] {
  const images = new Set<string>();
  function visit(value: unknown, depth = 0) {
    if (depth > 32) return;
    if (typeof value === "string") {
      const url = imageDataUrl(value);
      if (url) {
        images.add(url);
      } else if (
        value.includes("base64") ||
        value.includes("inlineData") ||
        value.includes("inline_data")
      ) {
        try {
          visit(JSON.parse(value), depth + 1);
        } catch {
          // Malformed payloads remain available in the source disclosure.
        }
      }
      return;
    }
    if (!value || typeof value !== "object") return;
    if (Array.isArray(value)) {
      value.forEach((item) => visit(item, depth + 1));
      return;
    }
    const raw = value as Record<string, unknown>;
    const mime = raw.media_type ?? raw.mimeType ?? raw.mime_type;
    if (typeof mime === "string" && typeof raw.data === "string") {
      const url = imageDataUrl(`data:${mime};base64,${raw.data}`);
      if (url) images.add(url);
    }
    Object.values(raw).forEach((item) => visit(item, depth + 1));
  }
  visit(block.metadata);
  visit(block.text);
  return [...images];
}

// Shorten Markdown image URLs before its size guard and syntax highlighting.
// Leave code examples verbatim, and only resolve these tokens in image nodes.
export function embeddedMarkdown(text: string) {
  const images = new Map<string, string>();
  const markdown = text.replace(
    /(`+)[\s\S]*?\1|(~{3,})[\s\S]*?\2|(!\[[^\]\n]*\]\(\s*<?)(data:image\/[a-z0-9.+-]+;base64,[a-z0-9+/]+={0,2})(?=>?\s*(?:\)|["']))/gi,
    (match, code, fence, prefix, url) => {
      if (code || fence || !imageDataUrl(url)) return match;
      const token = `journey-image:${images.size}`;
      images.set(token, url);
      return prefix + token;
    },
  );
  return { markdown, images };
}
