import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { ChevronRight } from "lucide-react";
import { useEffect, useRef } from "react";
// Large payloads stay verbatim so the reader does not build a tree from them.
const maxStructuredPayload = 48_000;
export function structuredPayload(text) {
    if (!text || text.length > maxStructuredPayload)
        return undefined;
    const trimmed = text.trim();
    if (!trimmed.startsWith("{") && !trimmed.startsWith("["))
        return undefined;
    try {
        const value = JSON.parse(text);
        if (value !== null && typeof value === "object")
            return value;
    }
    catch {
        return undefined;
    }
    return undefined;
}
function primitiveKind(value) {
    if (typeof value === "string")
        return "string";
    if (typeof value === "number" && Number.isFinite(value))
        return "number";
    if (typeof value === "boolean")
        return "boolean";
    return "null";
}
function formatPrimitive(value) {
    if (typeof value === "string")
        return JSON.stringify(value);
    if (typeof value === "number" && Number.isFinite(value))
        return String(value);
    if (typeof value === "boolean")
        return value ? "true" : "false";
    return "null";
}
function JsonBranch({ name, countLabel, startOpen, children, }) {
    const ref = useRef(null);
    useEffect(() => {
        const node = ref.current;
        if (!node || node.dataset.ready === "1")
            return;
        node.open = startOpen;
        node.dataset.ready = "1";
    }, [startOpen]);
    return (_jsxs("details", { className: "json-node", ref: ref, "data-json-key": name, children: [_jsxs("summary", { children: [_jsx(ChevronRight, { className: "json-chevron", size: 12, "aria-hidden": "true" }), name !== undefined && (_jsxs("span", { className: "json-key", children: [name, _jsx("span", { className: "json-colon", children: ":" })] })), _jsx("span", { className: "json-meta", children: countLabel })] }), _jsx("div", { className: "json-children", children: children })] }));
}
function JsonNode({ name, value, depth, }) {
    const encoded = typeof value === "string" ? structuredPayload(value) : undefined;
    const structured = encoded ??
        (value !== null && typeof value === "object" ? value : undefined);
    if (structured) {
        const entries = Array.isArray(structured)
            ? structured.map((item, index) => [String(index), item])
            : Object.entries(structured);
        return (_jsx(JsonBranch, { name: name, countLabel: Array.isArray(structured)
                ? `[${entries.length}]`
                : `{${entries.length}}`, startOpen: depth < 2, children: entries.map(([key, child]) => (_jsx(JsonNode, { name: key, value: child, depth: depth + 1 }, key))) }));
    }
    return (_jsxs("div", { className: "json-leaf", "data-json-key": name, children: [name !== undefined && (_jsxs("span", { className: "json-key", children: [name, _jsx("span", { className: "json-colon", children: ":" })] })), _jsx("span", { className: "json-value json-" + primitiveKind(value), children: formatPrimitive(value) })] }));
}
export function JsonPayload({ value, sourceRef, }) {
    return (_jsx("div", { className: "json-tree", ref: sourceRef, children: _jsx(JsonNode, { value: value, depth: 0 }) }));
}
export function SourcePayload({ text, sourceRef, plain = false, }) {
    const structured = plain ? undefined : structuredPayload(text);
    return structured ? (_jsx(JsonPayload, { value: structured, sourceRef: sourceRef })) : (_jsx("pre", { ref: sourceRef, children: text }));
}
