import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { FileText, Search, SquarePen, Terminal, Wrench, ExternalLink, FileCode2, } from "lucide-react";
import { useEffect, useState } from "react";
import { api } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
import { describeTool } from "./messagePresentation";
import { githubSourceLink } from "./githubSource";
import { SourceDisclosure } from "./SourceActivity";
export function ToolCall({ block, sourceRef, github, conversationId, plain = false, }) {
    const tool = describeTool(block);
    const link = githubSourceLink(block, github);
    const [expanded, setExpanded] = useState(false);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState("");
    const [preview, setPreview] = useState();
    useEffect(() => {
        setExpanded(false);
        setPreview(undefined);
        setError("");
    }, [link?.url, conversationId]);
    useEffect(() => {
        if (!expanded || !link || !conversationId || preview)
            return;
        let live = true;
        setLoading(true);
        setError("");
        const query = new URLSearchParams({
            path: link.path,
            startLine: String(link.startLine || 1),
        });
        api(`/conversations/${conversationId}/github/file?${query}`)
            .then((data) => {
            if (live)
                setPreview(data);
        })
            .catch((e) => {
            if (live)
                setError(e.message);
        })
            .finally(() => {
            if (live)
                setLoading(false);
        });
        return () => {
            live = false;
        };
    }, [expanded, link?.url, conversationId, preview]);
    const Icon = tool.icon === "read"
        ? FileText
        : tool.icon === "edit"
            ? SquarePen
            : tool.icon === "search"
                ? Search
                : tool.icon === "run"
                    ? Terminal
                    : Wrench;
    const raw = block.text || block.metadata || "";
    return (_jsx(SourceDisclosure, { icon: Icon, title: tool.isResult ? t("Tool result") : tool.name || t("Tool call"), detail: tool.target, detailTitle: tool.path || tool.target, detailFallback: t("Preserved source block"), path: tool.path, sourceLabel: t("Original tool payload"), payload: raw, sourceRef: sourceRef, plain: plain, summaryExtra: link && (_jsxs("a", { className: "tool-github-link", href: link.url, target: "_blank", rel: "noopener noreferrer", onClick: (e) => e.stopPropagation(), children: [t(link.pinned ? "View on GitHub" : "Latest on GitHub"), _jsx(ExternalLink, { size: 13, "aria-hidden": "true" })] })), footer: link && (_jsxs("div", { className: "tool-github-details", children: [_jsxs("span", { children: [link.path, link.startLine
                            ? ` · ${link.startLine}${link.endLine ? `–${link.endLine}` : ""}`
                            : ""] }), _jsx("span", { children: link.pinned
                        ? `${link.reference.slice(0, 7)} · ${t("Session commit")}`
                        : `${link.reference} · ${t("Original revision unknown")}` }), conversationId && (_jsxs("button", { type: "button", className: "tool-source-toggle", "aria-expanded": expanded, "aria-controls": `github-preview-${block.id}`, onClick: () => setExpanded((value) => !value), children: [_jsx(FileCode2, { size: 13 }), t(expanded ? "Close source preview" : "Preview source")] })), expanded && (_jsxs("section", { id: `github-preview-${block.id}`, className: "github-source-preview", "aria-label": t("GitHub source preview"), children: [_jsxs("header", { children: [_jsx("strong", { children: t("GitHub revision") }), _jsx("span", { children: link.reference })] }), _jsx("p", { children: t(link.pinned
                                ? "Local, uncommitted changes may differ from this commit."
                                : "This file may have changed since the conversation.") }), loading && _jsx("p", { role: "status", children: t("Loading…") }), error && (_jsx("p", { role: "alert", className: "error", children: error })), preview && (_jsxs(_Fragment, { children: [_jsx("div", { className: "github-source-code", children: preview.lines.map((line, index) => {
                                        const number = preview.startLine + index;
                                        const highlighted = !!link.startLine &&
                                            number >= link.startLine &&
                                            number <= (link.endLine || link.startLine);
                                        return (_jsxs("div", { className: highlighted ? "highlighted" : "", children: [_jsx("span", { "aria-hidden": "true", children: number }), _jsx("code", { children: line || " " })] }, number));
                                    }) }), (preview.startLine > 1 ||
                                    preview.lines.length < preview.totalLines) && (_jsx("p", { children: t("Preview shows part of the file. Open GitHub for the full source.") }))] }))] }))] })) }));
}
