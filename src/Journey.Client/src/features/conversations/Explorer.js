import { jsx as _jsx, Fragment as _Fragment, jsxs as _jsxs } from "react/jsx-runtime";
import { AnnotationContent, AnnotationTopics, AnnotationTools, TopicFilter, } from "../review/AnnotationContent";
import { threadTags } from "../review/annotationParsing";
import { SourceHistory } from "../imports/SourceHistory";
import { SuppliedFile } from "../imports/SuppliedFile";
import { AnnotationArtifacts } from "../review/AnnotationArtifacts";
import { t, useLocale, formatNumber, formatDate, count, } from "../../shared/i18n/i18n";
import { PublishDialog } from "../publications/Publish";
import { useState, useEffect, useRef } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import rehypeHighlight from "rehype-highlight";
import { Search, MessageSquare, Bookmark, GitBranch, ArrowLeft, Columns2, Check, Link2, X, Download, Eye, } from "lucide-react";
import { api, act, go, useApp, initials, loadConversation, } from "../../shared/state";
import { Badge } from "../../shared/ui/ui";
import { MessageRole } from "./MessageRole";
import { ToolCall } from "./ToolCall";
import { SourceActivity } from "./SourceActivity";
import { describeTool, groupSourceBlocks, } from "./messagePresentation";
import { Wrench } from "lucide-react";
import "./messages.css";
const markdownLimit = 48_000;
const readerPage = 40;
export function Markdown({ text }) {
    useLocale();
    if (text.length > markdownLimit)
        return (_jsxs(_Fragment, { children: [_jsx("p", { className: "source-notice", children: t("Large source kept as text so the page stays responsive.") }), _jsx("pre", { className: "source-text", children: text })] }));
    return (_jsx(ReactMarkdown, { remarkPlugins: [remarkGfm], rehypePlugins: [rehypeHighlight], components: {
            img: ({ alt }) => (_jsxs("span", { className: "attachment", children: [t("Image reference:"), " ", alt || "attachment", " ", t("· remote loading disabled")] })),
            a: ({ href, children }) => (_jsx("a", { href: href, target: "_blank", rel: "noreferrer", children: children })),
        }, children: text }));
}
async function fingerprint(text) {
    return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text))))
        .map((v) => v.toString(16).padStart(2, "0"))
        .join("")
        .toUpperCase();
}
function textRange(el, start, end) {
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    let pos = 0;
    const range = document.createRange();
    let begun = false;
    while (walker.nextNode()) {
        const n = walker.currentNode;
        const len = n.textContent?.length || 0;
        if (!begun && start <= pos + len) {
            range.setStart(n, Math.max(0, start - pos));
            begun = true;
        }
        if (begun && end <= pos + len) {
            range.setEnd(n, Math.max(0, end - pos));
            return range;
        }
        pos += len;
    }
    return null;
}
export function Message({ message, annotations = [], plain = false, original = false, onSelect, onThread, onPin, pinned = false, onLink, github, conversationId, attachments = [], }) {
    useLocale();
    const refs = useRef({});
    const ranges = useRef([]);
    useEffect(() => {
        ranges.current = [];
        if (original)
            return;
        for (const block of message.blocks) {
            const el = refs.current[block.id];
            if (!el)
                continue;
            const text = el.textContent || "";
            for (const a of annotations.filter((a) => !a.orphaned)) {
                const anchor = JSON.parse(a.anchor);
                if (anchor.blockId !== block.id || !anchor.quote)
                    continue;
                const exact = plain && text.slice(anchor.start, anchor.end) === anchor.quote;
                const start = exact ? anchor.start : text.indexOf(anchor.quote);
                if (start < 0 || (!exact && text.indexOf(anchor.quote, start + 1) >= 0))
                    continue;
                const range = textRange(el, start, start + anchor.quote.length);
                if (range)
                    ranges.current.push({ range, id: a.id });
            }
        }
        const registry = CSS.highlights;
        if (registry && window.Highlight) {
            registry.set("annotation-" + message.id, new window.Highlight(...ranges.current.map((x) => x.range)));
            const style = document.createElement("style");
            style.textContent = `::highlight(annotation-${message.id}) { background: #f6d77777; color: inherit; }`;
            document.head.append(style);
            return () => {
                registry.delete("annotation-" + message.id);
                style.remove();
            };
        }
    }, [annotations, plain, original, message]);
    const select = async (block, e) => {
        if (!onSelect)
            return;
        const selection = window.getSelection();
        if (!selection || selection.isCollapsed)
            return;
        const el = refs.current[block.id];
        if (!el ||
            !el.contains(selection.anchorNode) ||
            !el.contains(selection.focusNode))
            return;
        const quote = selection.toString();
        let start = block.text.indexOf(quote);
        if (plain) {
            const range = selection.getRangeAt(0);
            const pre = range.cloneRange();
            pre.selectNodeContents(el);
            pre.setEnd(range.startContainer, range.startOffset);
            start = pre.toString().length;
        }
        else if (start < 0 || block.text.indexOf(quote, start + 1) >= 0) {
            useApp
                .getState()
                .notify(t("Switch to Source text to anchor this selection precisely."));
            return;
        }
        if (start < 0)
            return;
        const end = start + quote.length;
        onSelect({
            messageId: message.id,
            blockId: block.id,
            start,
            end,
            quote,
            prefix: block.text.slice(Math.max(0, start - 40), start),
            suffix: block.text.slice(end, end + 40),
            fingerprint: await fingerprint(block.text),
            x: Math.min(e.clientX, window.innerWidth - 220),
            y: e.clientY + 10,
        });
    };
    const renderBlock = (b) => {
        const isText = b.kind === "text" || b.kind === "markdown";
        const tool = !isText && describeTool(b);
        const files = attachments.filter((file) => file.blockId === b.id);
        return (_jsxs("div", { children: [_jsx("div", { ref: isText
                        ? (el) => {
                            refs.current[b.id] = el;
                        }
                        : undefined, className: "message-content" + (isText ? "" : " tool-call-block"), onMouseUp: (e) => select(b, e), onClick: (e) => {
                        if (window.getSelection()?.isCollapsed)
                            for (const r of ranges.current)
                                for (const rect of r.range.getClientRects())
                                    if (e.clientX >= rect.left &&
                                        e.clientX <= rect.right &&
                                        e.clientY >= rect.top &&
                                        e.clientY <= rect.bottom)
                                        onThread?.(r.id);
                    }, children: isText ? (plain ? (_jsx("pre", { className: "source-text", children: b.text })) : (_jsx(Markdown, { text: b.text }))) : tool && (tool.isCall || tool.isResult) ? (_jsx(ToolCall, { block: b, github: github, conversationId: conversationId, plain: plain, sourceRef: (el) => {
                            refs.current[b.id] = el;
                        } })) : (_jsx(SourceActivity, { block: b, plain: plain, sourceRef: (el) => {
                            refs.current[b.id] = el;
                        } })) }), files.map((file) => (_jsx(SuppliedFile, { file: file }, file.id)))] }, b.id));
    };
    return (_jsxs("article", { className: "message message-card " + message.role + (pinned ? " pinned" : ""), "data-message-role": message.role, id: "message-" + message.id, children: [_jsxs("header", { children: [_jsx(MessageRole, { role: message.role }), (message.role === "user" || message.role === "assistant") && (_jsx("span", { className: "message-source-label", children: message.role === "user"
                            ? t("Original prompt")
                            : message.role === "assistant"
                                ? t("Original AI response")
                                : t(message.role) })), message.model && _jsx("span", { className: "model-badge", children: message.model }), _jsx("span", { className: "spacer" }), message.timestamp && (_jsx("time", { children: formatDate(message.timestamp, {
                            month: "short",
                            day: "numeric",
                            year: "numeric",
                            hour: "2-digit",
                            minute: "2-digit",
                        }) })), onPin && (_jsx("button", { title: t("Bookmark message"), className: pinned ? "active" : "", onClick: onPin, children: _jsx(Bookmark, { size: 14 }) })), onLink && (_jsxs("button", { title: t("Use this message as evidence"), onClick: onLink, children: [_jsx(Link2, { size: 14 }), " ", t("Use as evidence")] })), onSelect && (_jsx("button", { title: t("Comment on whole message"), onClick: () => onSelect({ messageId: message.id, whole: true }), children: _jsx(MessageSquare, { size: 14 }) }))] }), _jsxs("details", { open: true, children: [_jsx("summary", { children: t("Message content") }), groupSourceBlocks(message.blocks).map((group) => group.tools ? (_jsxs("section", { className: "tool-call-group", "aria-label": t("Tool calls"), children: [_jsxs("header", { children: [_jsx(Wrench, { size: 14, "aria-hidden": "true" }), _jsx("span", { children: t("Tool calls") }), _jsx("span", { className: "tool-call-count", children: formatNumber(group.blocks.length) })] }), group.blocks.map(renderBlock)] }, group.blocks[0].id)) : (renderBlock(group.blocks[0])))] }), !original && annotations.length > 0 && (_jsxs("button", { className: "annotation-count", onClick: () => onThread?.(annotations[0].id), children: [_jsx(MessageSquare, { size: 12 }), count("comments", annotations.length)] }))] }));
}
export function Explorer({ id, user, members, canComment, revision, }) {
    useLocale();
    const [c, setC] = useState(null);
    const [suppliedFiles, setSuppliedFiles] = useState([]);
    const [github, setGithub] = useState();
    useEffect(() => {
        let live = true;
        api(`/conversations/${id}/recovered-source`)
            .then((data) => {
            if (live)
                setSuppliedFiles(data.attachments || []);
        })
            .catch(() => {
            if (live)
                setSuppliedFiles([]);
        });
        return () => {
            live = false;
        };
    }, [id, revision]);
    useEffect(() => {
        if (!c || c.id !== id)
            return;
        let live = true;
        setGithub(undefined);
        api(`/conversations/${id}/github`)
            .then((data) => {
            if (live)
                setGithub({ id, source: data.source });
        })
            .catch(() => {
            if (live)
                setGithub(undefined);
        });
        return () => {
            live = false;
        };
    }, [id, c?.id, c?.applicationProjectId, revision]);
    const [sharing, setSharing] = useState(false);
    const [annotations, setAnnotations] = useState([]);
    const [q, setQ] = useState("");
    const [role, setRole] = useState("");
    const [plain, setPlain] = useState(false);
    const [original, setOriginal] = useState(false);
    const [selected, setSelected] = useState(null);
    const [active, setActive] = useState("");
    const [composing, setComposing] = useState(false);
    const [body, setBody] = useState("");
    const [filter, setFilter] = useState("open");
    const [topic, setTopic] = useState("");
    const [person, setPerson] = useState("");
    const [reply, setReply] = useState({});
    const [pins, setPins] = useState(JSON.parse(localStorage.getItem("pins") || "[]"));
    const [branch, setBranch] = useState("");
    const [page, setPage] = useState(0);
    const [relevant, setRelevant] = useState([]);
    const { setWorkspace, notify } = useApp();
    const refresh = () => {
        if (!c || c.id !== id)
            return;
        return act(async () => setAnnotations(await api("/w/" + c.workspaceId + "/annotations?conversationId=" + id)));
    };
    useEffect(() => {
        let cancelled = false;
        setRole("");
        setPage(0);
        setC(null);
        setAnnotations([]);
        act(async () => {
            const conversation = await loadConversation(id);
            if (!cancelled) {
                setC(conversation);
                setWorkspace(conversation.workspaceId);
            }
            const journeys = await api("/conversations/" + id + "/journeys");
            if (!cancelled)
                setRelevant(journeys);
        });
        return () => {
            cancelled = true;
        };
    }, [id]);
    useEffect(() => {
        refresh();
    }, [id, c?.workspaceId, revision]);
    useEffect(() => {
        const params = new URLSearchParams(location.hash.split("?")[1]);
        const message = params.get("message");
        const thread = params.get("thread");
        if (thread)
            setActive(thread);
        if (message && c) {
            const index = [...c.messages]
                .sort((a, b) => a.order - b.order)
                .findIndex((m) => m.id === message);
            if (index >= 0)
                setPage(Math.floor(index / readerPage));
            setTimeout(() => document
                .getElementById("message-" + message)
                ?.scrollIntoView({ block: "center" }), 100);
        }
    }, [c]);
    if (!c)
        return _jsx("div", { className: "loading", children: t("Opening original conversation…") });
    const ordered = [...c.messages].sort((a, b) => a.order - b.order);
    const roles = Array.from(new Set(ordered.map((m) => m.role)));
    const path = new Set();
    let cursor = branch;
    while (cursor && !path.has(cursor)) {
        path.add(cursor);
        cursor = c.messages.find((m) => m.id === cursor)?.parentId;
    }
    const messages = ordered.filter((m) => (!role || m.role === role) &&
        (!branch || path.has(m.id)) &&
        (!q ||
            m.blocks.some((b) => b.text.toLowerCase().includes(q.toLowerCase()))));
    const leaves = ordered.filter((m) => !ordered.some((x) => x.parentId === m.id));
    const pageCount = Math.max(1, Math.ceil(messages.length / readerPage));
    const safePage = Math.min(page, pageCount - 1);
    const start = safePage * readerPage;
    const visible = messages.slice(start, start + readerPage);
    const openMessage = (m) => {
        const index = messages.indexOf(m);
        setPage(Math.floor(Math.max(index, 0) / readerPage));
        setTimeout(() => document
            .getElementById("message-" + m.id)
            ?.scrollIntoView({ behavior: "smooth", block: "start" }), 50);
    };
    const submit = () => act(async () => {
        await api("/w/" + c.workspaceId + "/annotations", {
            conversationId: id,
            messageId: selected.messageId,
            anchor: selected.whole
                ? null
                : {
                    blockId: selected.blockId,
                    start: selected.start,
                    end: selected.end,
                    quote: selected.quote,
                    prefix: selected.prefix,
                    suffix: selected.suffix,
                    fingerprint: selected.fingerprint,
                },
            body,
        });
        setBody("");
        setComposing(false);
        setSelected(null);
        refresh();
        notify(t("Comment anchored to the original source"));
    });
    const showThread = (id) => {
        setActive(id);
        setOriginal(false);
        setFilter("all");
        setTopic("");
        setTimeout(() => document
            .getElementById("thread-" + id)
            ?.scrollIntoView({ block: "center" }), 50);
    };
    return (_jsxs("div", { className: "explorer", children: [sharing && (_jsx(PublishDialog, { conversation: c, close: () => setSharing(false) })), _jsxs("div", { className: "explorer-heading", children: [_jsx("button", { title: t("Back to library"), onClick: () => go("library"), children: _jsx(ArrowLeft, { size: 17 }) }), _jsxs("div", { children: [_jsx("h2", { children: c.title }), _jsxs("div", { className: "row", children: [_jsx(Badge, { provider: c.provider }), _jsxs("small", { children: [count("messages", c.messages.length), " \u00B7", " ", c.demo ? t("Demonstration data") : t("Imported source"), " ", t("· Read-only original")] })] })] }), _jsx("span", { className: "spacer" }), ["Owner", "Admin"].includes(members.find((m) => m.id === user.id)?.role) && (_jsx("button", { className: "primary", onClick: () => setSharing(true), children: t("Share conversation") })), _jsxs("button", { onClick: () => go("compare?left=" + id), children: [_jsx(Columns2, { size: 16 }), t("Compare")] }), _jsxs("a", { className: "button", href: "/api/sources/" + c.sourceId, children: [_jsx(Download, { size: 15 }), t("Original file")] })] }), _jsxs("div", { className: "explorer-panes", children: [_jsxs("aside", { className: "outline", children: [_jsx("span", { className: "eyebrow", children: t("CONVERSATION OUTLINE") }), _jsx(SourceHistory, { conversation: c, onUpdated: () => {
                                    act(async () => setC(await loadConversation(id)));
                                    refresh();
                                }, canEdit: ["Owner", "Admin", "Editor"].includes(members.find((m) => m.id === user.id)?.role) }), _jsxs("label", { className: "search compact", children: [_jsx(Search, { size: 15 }), _jsx("input", { placeholder: t("Find in conversation…"), value: q, onChange: (e) => {
                                            setQ(e.target.value);
                                            setPage(0);
                                        } })] }), _jsxs("label", { children: [t("Source branches"), _jsxs("select", { value: branch, onChange: (e) => {
                                            setBranch(e.target.value);
                                            setPage(0);
                                        }, children: [_jsx("option", { value: "", children: t("All paths", { count: leaves.length }) }), leaves.map((m, i) => (_jsxs("option", { value: m.id, children: [t("Path"), " ", i + 1, " ", t("· ends at #"), m.order + 1] }, m.id)))] })] }), _jsx("div", { className: "message-tree", children: messages.map((m) => (_jsxs("button", { className: "outline-message" + (pins.includes(m.id) ? " bookmarked" : ""), "data-message-role": m.role, onClick: () => openMessage(m), children: [_jsx("span", { children: String(ordered.indexOf(m) + 1).padStart(2, "0") }), _jsxs("div", { children: [_jsxs("div", { className: "outline-role", children: [_jsx(MessageRole, { role: m.role }), ordered.filter((x) => x.parentId === m.parentId).length >
                                                            1 ? (_jsx("small", { children: t(" · branch") })) : null] }), _jsx("span", { className: "outline-preview", children: m.blocks[0]?.text.slice(0, 65) || t("Content block") })] }), pins.includes(m.id) && _jsx(Bookmark, { size: 12 })] }, m.id))) }), _jsx("span", { className: "eyebrow", children: t("APPEARS IN") }), relevant.map((j) => (_jsxs("a", { className: "journey-link", href: "#canvas/" + j.id, children: [_jsx(GitBranch, { size: 14 }), j.title] }, j.id)))] }), _jsxs("main", { className: "reader", children: [_jsxs("div", { className: "reader-toolbar", children: [_jsxs("span", { children: [_jsx(Eye, { size: 14 }), " ", original ? t("Original only") : t("Review mode")] }), _jsxs("div", { className: "row", children: [_jsx("button", { className: plain ? "active" : "", onClick: () => setPlain(!plain), children: plain ? t("Reading view") : t("Source text") }), _jsx("button", { onClick: () => setOriginal(!original), children: original ? t("Show annotations") : t("Hide annotations") })] })] }), _jsxs("div", { className: "message-role-filter", children: [_jsxs("label", { children: [t("Message role"), _jsxs("select", { value: role, onChange: (e) => {
                                                    setRole(e.target.value);
                                                    setPage(0);
                                                }, children: [_jsx("option", { value: "", children: t("All roles") }), roles.map((value) => (_jsx("option", { value: value, children: value.charAt(0).toUpperCase() + value.slice(1) }, value)))] })] }), _jsx("small", { role: "status", children: t("Messages shown", {
                                            shown: messages.length,
                                            total: ordered.length,
                                        }) })] }), _jsxs("div", { className: "source-notice", children: [c.demo ? t("SIMULATED DEMONSTRATION") : t("PRESERVED SOURCE"), " ", _jsx("span", { children: t("· Curation never overwrites this conversation.") })] }), messages.length > readerPage && (_jsxs("div", { className: "public-pagination", children: [_jsx("button", { disabled: safePage === 0, onClick: () => setPage(safePage - 1), children: t("Earlier messages") }), _jsx("span", { children: t("Showing {from}–{to} of {total}", {
                                            from: start + 1,
                                            to: Math.min(start + readerPage, messages.length),
                                            total: messages.length,
                                        }) }), _jsx("button", { disabled: start + readerPage >= messages.length, onClick: () => setPage(safePage + 1), children: t("Later messages") })] })), visible.map((m) => (_jsx(Message, { message: m, github: github?.id === id ? github.source : null, conversationId: id, attachments: suppliedFiles, plain: plain, original: original, annotations: annotations.filter((a) => a.messageId === m.id), onThread: showThread, onSelect: canComment && !original
                                    ? (a) => {
                                        setSelected(a);
                                        if (a.whole)
                                            setComposing(true);
                                    }
                                    : undefined, pinned: pins.includes(m.id), onPin: () => {
                                    const next = pins.includes(m.id)
                                        ? pins.filter((x) => x !== m.id)
                                        : [...pins, m.id];
                                    setPins(next);
                                    localStorage.setItem("pins", JSON.stringify(next));
                                } }, m.id))), !messages.length && (_jsx("div", { className: "empty", children: t("No matching messages.") }))] }), !original && (_jsxs("aside", { className: "review-pane", children: [_jsxs("div", { className: "section-title", children: [_jsxs("h3", { children: [t("Discussion"), " ", _jsx("span", { className: "count", children: formatNumber(annotations.length) })] }), _jsx(MessageSquare, { size: 17 })] }), _jsxs("div", { className: "review-filters", children: [_jsx(TopicFilter, { annotations: annotations, value: topic, onChange: setTopic }), _jsxs("select", { "aria-label": t("Comment status"), value: filter, onChange: (e) => setFilter(e.target.value), children: [_jsx("option", { value: "open", children: t("Open threads") }), _jsx("option", { value: "resolved", children: t("Resolved") }), _jsx("option", { value: "all", children: t("All threads") })] }), _jsxs("select", { "aria-label": t("Comment author"), value: person, onChange: (e) => setPerson(e.target.value), children: [_jsx("option", { value: "", children: t("Everyone") }), members.map((m) => (_jsx("option", { value: m.id, children: m.name }, m.id)))] })] }), _jsx("p", { className: "muted small-text", children: t("Select a passage to add a contextual comment. Source text mode supports exact code and Markdown ranges.") }), composing && selected && (_jsxs("div", { className: "thread composing", children: [_jsx("strong", { children: t("New contextual comment") }), selected.quote && _jsx("blockquote", { children: selected.quote }), _jsx(MentionInput, { body: body, setBody: setBody, members: members }), _jsx(AnnotationTools, { body: body, setBody: setBody }), _jsxs("div", { className: "row", children: [_jsx("button", { onClick: () => {
                                                    setComposing(false);
                                                    setSelected(null);
                                                }, children: t("Cancel") }), _jsx("button", { className: "primary", disabled: !body.trim(), onClick: submit, children: t("Post comment") })] })] })), annotations
                                .filter((a) => (filter === "all" ||
                                a.resolved === (filter === "resolved")) &&
                                (!person || a.authorId === person) &&
                                (!topic || threadTags(a).includes(topic)))
                                .map((a) => (_jsxs("div", { className: "thread " + (active === a.id ? "focused" : ""), id: "thread-" + a.id, onClick: () => setActive(a.id), children: [_jsxs("div", { className: "thread-top", children: [_jsx("span", { className: "avatar tiny", children: initials(members.find((m) => m.id === a.authorId)?.name ||
                                                    "Curator") }), _jsx("strong", { children: members.find((m) => m.id === a.authorId)?.name ||
                                                    "Curator" }), _jsx("button", { title: a.resolved
                                                    ? t("Reopen thread")
                                                    : t(threadTags(a).includes("action")
                                                        ? "Complete action"
                                                        : "Resolve thread"), disabled: !canComment, onClick: () => act(async () => {
                                                    await api("/annotations/" + a.id, { resolved: !a.resolved }, "PATCH");
                                                    refresh();
                                                }), children: a.resolved ? (t("Reopen")) : threadTags(a).includes("action") ? (t("Complete action")) : (_jsx(Check, { size: 15 })) })] }), _jsx(AnnotationTopics, { annotation: a }), a.orphaned && (_jsx("p", { className: "error", children: t("Orphaned anchor — review required") })), JSON.parse(a.anchor).quote && (_jsx("blockquote", { onClick: () => document
                                            .getElementById("message-" + a.messageId)
                                            ?.scrollIntoView({
                                            block: "center",
                                            behavior: "smooth",
                                        }), children: JSON.parse(a.anchor).quote })), a.comments.map((co, i) => (_jsxs("div", { className: "comment", children: [i > 0 && (_jsx("strong", { children: members.find((m) => m.id === co.authorId)?.name ||
                                                    "Teammate" })), _jsx(AnnotationContent, { body: co.body }), _jsxs("div", { className: "row", children: [_jsx("small", { children: formatDate(co.createdAt, { dateStyle: "medium" }) }), co.authorId === user.id && (_jsxs(_Fragment, { children: [_jsx("button", { onClick: () => {
                                                                    const b = prompt(t("Edit comment"), co.body);
                                                                    if (b !== null)
                                                                        act(async () => {
                                                                            await api("/comments/" + co.id, { body: b }, "PUT");
                                                                            refresh();
                                                                        });
                                                                }, children: t("Edit") }), _jsx("button", { onClick: () => act(async () => {
                                                                    await api("/comments/" + co.id, undefined, "DELETE");
                                                                    refresh();
                                                                }), children: t("Delete") })] }))] })] }, co.id))), _jsx(AnnotationArtifacts, { annotation: a, canUpload: canComment, onUploaded: () => refresh() }), canComment && (_jsxs("form", { onSubmit: (e) => {
                                            e.preventDefault();
                                            act(async () => {
                                                await api("/annotations/" + a.id + "/replies", {
                                                    body: reply[a.id],
                                                });
                                                setReply({ ...reply, [a.id]: "" });
                                                refresh();
                                            });
                                        }, children: [_jsx(MentionInput, { body: reply[a.id] || "", setBody: (b) => setReply({ ...reply, [a.id]: b }), members: members, placeholder: t("Reply or @mention…") }), _jsx("button", { disabled: !reply[a.id]?.trim(), children: t("Reply") })] }))] }, a.id)))] }))] }), selected && !composing && !selected.whole && (_jsxs("div", { className: "selection-toolbar", style: { left: selected.x, top: selected.y }, children: [_jsxs("button", { className: "primary", onClick: () => setComposing(true), children: [_jsx(MessageSquare, { size: 14 }), t("Add comment")] }), _jsx("button", { title: t("Dismiss selection"), onClick: () => setSelected(null), children: _jsx(X, { size: 14 }) })] }))] }));
}
function MentionInput({ body, setBody, members, placeholder = t("Add your perspective. Use @ to mention…"), }) {
    useLocale();
    const match = body.match(/@([^@\n]*)$/);
    return (_jsxs("div", { className: "mention-input", children: [_jsx("textarea", { value: body, onChange: (e) => setBody(e.target.value), placeholder: placeholder }), match && (_jsx("div", { className: "mention-menu", children: members
                    .filter((m) => m.name.toLowerCase().includes(match[1].toLowerCase()))
                    .map((m) => (_jsxs("button", { type: "button", onClick: () => setBody(body.slice(0, body.lastIndexOf("@")) + "@" + m.name + " "), children: [_jsx("span", { className: "avatar tiny", children: initials(m.name) }), m.name] }, m.id))) }))] }));
}
