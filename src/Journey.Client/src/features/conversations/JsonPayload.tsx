import { ChevronRight } from "lucide-react";
import { useEffect, useRef, type ReactNode } from "react";

// Large payloads stay verbatim so the reader does not build a tree from them.
const maxStructuredPayload = 48_000;

export function structuredPayload(
  text: string,
): object | unknown[] | undefined {
  if (!text || text.length > maxStructuredPayload) return undefined;
  const trimmed = text.trim();
  if (!trimmed.startsWith("{") && !trimmed.startsWith("[")) return undefined;
  try {
    const value = JSON.parse(text);
    if (value !== null && typeof value === "object") return value;
  } catch {
    return undefined;
  }
  return undefined;
}

function primitiveKind(value: unknown) {
  if (typeof value === "string") return "string";
  if (typeof value === "number" && Number.isFinite(value)) return "number";
  if (typeof value === "boolean") return "boolean";
  return "null";
}

function formatPrimitive(value: unknown) {
  if (typeof value === "string") return JSON.stringify(value);
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  if (typeof value === "boolean") return value ? "true" : "false";
  return "null";
}

function JsonBranch({
  name,
  countLabel,
  startOpen,
  children,
}: {
  name?: string;
  countLabel: string;
  startOpen: boolean;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    const node = ref.current;
    if (!node || node.dataset.ready === "1") return;
    node.open = startOpen;
    node.dataset.ready = "1";
  }, [startOpen]);
  return (
    <details className="json-node" ref={ref} data-json-key={name}>
      <summary>
        <ChevronRight className="json-chevron" size={12} aria-hidden="true" />
        {name !== undefined && (
          <span className="json-key">
            {name}
            <span className="json-colon">:</span>
          </span>
        )}
        <span className="json-meta">{countLabel}</span>
      </summary>
      <div className="json-children">{children}</div>
    </details>
  );
}

function JsonNode({
  name,
  value,
  depth,
}: {
  name?: string;
  value: unknown;
  depth: number;
}) {
  const encoded =
    typeof value === "string" ? structuredPayload(value) : undefined;
  const structured =
    encoded ??
    (value !== null && typeof value === "object" ? value : undefined);
  if (structured) {
    const entries = Array.isArray(structured)
      ? structured.map((item, index) => [String(index), item] as const)
      : Object.entries(structured);
    return (
      <JsonBranch
        name={name}
        countLabel={
          Array.isArray(structured)
            ? `[${entries.length}]`
            : `{${entries.length}}`
        }
        startOpen={depth < 2}
      >
        {entries.map(([key, child]) => (
          <JsonNode key={key} name={key} value={child} depth={depth + 1} />
        ))}
      </JsonBranch>
    );
  }
  return (
    <div className="json-leaf" data-json-key={name}>
      {name !== undefined && (
        <span className="json-key">
          {name}
          <span className="json-colon">:</span>
        </span>
      )}
      <span className={"json-value json-" + primitiveKind(value)}>
        {formatPrimitive(value)}
      </span>
    </div>
  );
}

export function JsonPayload({
  value,
  sourceRef,
}: {
  value: object | unknown[];
  sourceRef: (element: HTMLElement | null) => void;
}) {
  return (
    <div className="json-tree" ref={sourceRef}>
      <JsonNode value={value} depth={0} />
    </div>
  );
}

export function SourcePayload({
  text,
  sourceRef,
  plain = false,
}: {
  text: string;
  sourceRef: (element: HTMLElement | null) => void;
  plain?: boolean;
}) {
  const structured = plain ? undefined : structuredPayload(text);
  return structured ? (
    <JsonPayload value={structured} sourceRef={sourceRef} />
  ) : (
    <pre ref={sourceRef}>{text}</pre>
  );
}
