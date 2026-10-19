import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { ConversationFilters } from "./ConversationFilters";
import { ReplacementRules, } from "../imports/SanitizationSettings";
import { AnnotationContent, AnnotationTopics, } from "../review/AnnotationContent";
import { BrandMark } from "../../shared/ui/BrandMark";
import { Discussion, DiscussionToggle } from "./Discussion";
import { LanguagePicker } from "../../shared/i18n/LanguagePicker";
import { t, useLocale, count } from "../../shared/i18n/i18n";
import "@xyflow/react/dist/style.css";
import { ReactFlow, Background, Controls, MiniMap } from "@xyflow/react";
import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, ArrowRight, Check, ShieldCheck, ExternalLink, Copy, Trash2, X, } from "lucide-react";
import { api, act, useApp, graph, loadConversation } from "../../shared/state";
import { Modal, Badge } from "../../shared/ui/ui";
import { Markdown } from "../conversations/Explorer";
export function PublishDialog({ journey, conversation, close, }) {
    useLocale();
    const base = conversation
        ? "/conversations/" + conversation.id
        : "/journeys/" + journey.id;
    const [cs, setCs] = useState([]);
    const [ids, setIds] = useState([]);
    const [curatorNodeIds, setCurators] = useState([]);
    const [annotations, setAnnotations] = useState([]);
    const [annotationIds, setAnnotationIds] = useState([]);
    const [includeAnnotations, setIncludeAnnotations] = useState(false);
    useEffect(() => {
        const workspaceId = conversation?.workspaceId || journey?.workspaceId;
        if (!workspaceId)
            return;
        act(async () => {
            const rows = await api("/w/" +
                workspaceId +
                "/annotations" +
                (conversation ? "?conversationId=" + conversation.id : ""));
            setAnnotations(rows.filter((a) => !a.orphaned &&
                (conversation
                    ? !a.journeyId
                    : !a.journeyId || a.journeyId === journey.id)));
        });
    }, [conversation?.id, journey?.id]);
    const eligibleAnnotations = annotations.filter((a) => a.messageId
        ? ids.includes(a.messageId)
        : !conversation &&
            graph(journey).nodes.some((n) => n.id === a.nodeId));
    const [redactions, setRedactions] = useState("");
    const [replacementRules, setReplacementRules] = useState([]);
    const [rulesLoaded, setRulesLoaded] = useState(false);
    useEffect(() => {
        act(async () => {
            setReplacementRules(await api("/me/sanitization"));
            setRulesLoaded(true);
        });
    }, []);
    const [title, setTitle] = useState("");
    const [discussionEnabled, setDiscussionEnabled] = useState(false);
    const [visibility, setVisibility] = useState("Public");
    const [preview, setPreview] = useState(null);
    const [selectedFindings, setSelectedFindings] = useState([]);
    const [previewLoading, setPreviewLoading] = useState(false);
    const previewVersion = useRef(0);
    const [confirmed, setConfirmed] = useState(false);
    const [slug, setSlug] = useState("");
    const [publications, setPublications] = useState([]);
    const [historyError, setHistoryError] = useState("");
    const [historyLoading, setHistoryLoading] = useState(true);
    const [deleting, setDeleting] = useState("");
    const [confirmDelete, setConfirmDelete] = useState("");
    const linkFor = (value) => location.origin + location.pathname + "#public/" + value;
    useEffect(() => {
        let active = true;
        setHistoryLoading(true);
        setHistoryError("");
        api(base + "/publications")
            .then((rows) => {
            if (active)
                setPublications(rows);
        })
            .catch((e) => {
            if (active)
                setHistoryError(e.message);
        })
            .finally(() => {
            if (active)
                setHistoryLoading(false);
        });
        return () => {
            active = false;
        };
    }, [base, slug]);
    useEffect(() => {
        if (conversation) {
            setCs([conversation]);
            return;
        }
        act(async () => {
            const cids = [
                ...new Set(graph(journey)
                    .nodes.map((n) => n.data.conversationId)
                    .filter(Boolean)),
            ];
            setCs(await Promise.all(cids.map((id) => loadConversation(id))));
        });
    }, [journey?.id, conversation?.id]);
    const request = {
        title: conversation ? title.trim() || undefined : undefined,
        messageIds: ids,
        curatorNodeIds,
        annotationIds: includeAnnotations
            ? annotationIds.filter((id) => eligibleAnnotations.some((a) => a.id === id))
            : [],
        redactions: redactions.split("\n").filter(Boolean),
        replacementRules,
        visibility,
        confirm: confirmed,
        discussionEnabled,
    };
    const invalidate = () => {
        previewVersion.current += 1;
        setPreview(null);
        setSelectedFindings([]);
        setPreviewLoading(false);
        setConfirmed(false);
    };
    const reviewPreview = async (text = redactions) => {
        invalidate();
        const version = previewVersion.current;
        setPreviewLoading(true);
        try {
            const result = await api(base + "/publication-preview", {
                ...request,
                redactions: text.split("\n").filter(Boolean),
                confirm: false,
            });
            if (version === previewVersion.current)
                setPreview(result);
        }
        finally {
            if (version === previewVersion.current)
                setPreviewLoading(false);
        }
    };
    return (_jsxs(Modal, { title: conversation
            ? t("Share this conversation")
            : t("Publish a curated journey"), onClose: close, wide: true, children: [!slug && _jsx("h3", { children: t("Create a new shared link") }), slug ? (_jsxs("div", { className: "import-result", children: [_jsx(Check, { size: 36 }), _jsx("h2", { children: t("Your snapshot is ready") }), _jsx("p", { children: t("Future edits and imports do not change this publication.") }), _jsxs("a", { className: "button primary", href: "#public/" + slug, target: "_blank", children: [t("Open visitor experience"), " ", _jsx(ExternalLink, { size: 16 })] }), _jsx("button", { onClick: () => act(async () => {
                            await navigator.clipboard.writeText(location.origin + location.pathname + "#public/" + slug);
                            useApp.getState().notify(t("Link copied"));
                        }), children: t("Copy link") }), _jsx("input", { "aria-label": t("Publication link"), readOnly: true, value: location.origin + location.pathname + "#public/" + slug })] })) : (_jsxs(_Fragment, { children: [_jsxs("div", { className: "publish-notice", children: [_jsx(ShieldCheck, {}), _jsxs("p", { children: [conversation && (_jsxs(_Fragment, { children: [t("No journey needed. Create a link to the messages you choose."), " "] })), t("A separate, read-only snapshot. Raw files, metadata and attachments are excluded. Annotations stay private unless you select them below.")] })] }), conversation && (_jsxs("label", { children: [t("Shared conversation title (optional)"), _jsx("input", { value: title, maxLength: 200, placeholder: conversation.title, onChange: (e) => {
                                    setTitle(e.target.value);
                                    invalidate();
                                } }), _jsx("small", { children: t("Leave blank to use the current conversation title.") })] })), _jsx(DiscussionToggle, { enabled: discussionEnabled, save: async (enabled) => {
                            setDiscussionEnabled(enabled);
                            invalidate();
                        } }), _jsxs("div", { className: "publish-grid", children: [_jsxs("div", { children: [!conversation && _jsx("h3", { children: t("Curator explanations") }), (conversation ? { nodes: [] } : graph(journey)).nodes
                                        .filter((n) => n.data.body)
                                        .map((n) => (_jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: curatorNodeIds.includes(n.id), onChange: (e) => {
                                                    setCurators((s) => e.target.checked
                                                        ? [...s, n.id]
                                                        : s.filter((x) => x !== n.id));
                                                    invalidate();
                                                } }), n.data.title] }, n.id))), _jsx("h3", { children: t("Choose source messages") }), _jsxs("label", { children: [t("Select messages by role"), _jsxs("select", { value: "", onChange: (e) => {
                                                    const role = e.target.value;
                                                    if (!role)
                                                        return;
                                                    setIds(cs.flatMap((c) => c.messages
                                                        .filter((m) => m.role === role)
                                                        .map((m) => m.id)));
                                                    invalidate();
                                                }, children: [_jsx("option", { value: "", children: t("Choose a role") }), [
                                                        ...new Set(cs.flatMap((c) => c.messages.map((m) => m.role))),
                                                    ].map((role) => (_jsx("option", { value: role, children: t(role) }, role)))] }), _jsx("small", { children: t("Replaces the selection with messages from this role.") })] }), _jsx("button", { onClick: () => {
                                            setIds(cs.flatMap((c) => c.messages.map((m) => m.id)));
                                            invalidate();
                                        }, children: t("Select all messages") }), _jsx("button", { onClick: () => {
                                            setIds([]);
                                            invalidate();
                                        }, children: t("Clear selection") }), _jsx("div", { className: "publish-messages", children: cs.map((c) => (_jsxs("details", { children: [_jsx("summary", { children: c.title }), c.messages.map((m) => (_jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: ids.includes(m.id), onChange: (e) => {
                                                                setIds((s) => e.target.checked
                                                                    ? [...s, m.id]
                                                                    : s.filter((id) => id !== m.id));
                                                                invalidate();
                                                            } }), _jsxs("span", { children: [_jsx("small", { children: t(m.role) }), m.blocks
                                                                    .map((b) => b.text)
                                                                    .join(" ")
                                                                    .slice(0, 130)] })] }, m.id)))] }, c.id))) })] }), _jsxs("div", { children: [_jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: includeAnnotations, onChange: (e) => {
                                                    setIncludeAnnotations(e.target.checked);
                                                    invalidate();
                                                } }), t("Include selected annotations")] }), includeAnnotations && (_jsxs("div", { className: "publication-annotation-options", children: [_jsx("p", { children: t("Choose threads to share, including their current replies. Author identities and mention notifications are excluded. Only annotations on included content are available.") }), _jsxs("div", { className: "row", children: [_jsx("button", { type: "button", disabled: !eligibleAnnotations.length, onClick: () => {
                                                            setAnnotationIds(eligibleAnnotations.map((a) => a.id));
                                                            invalidate();
                                                        }, children: t("Select all annotations") }), _jsx("button", { type: "button", disabled: !annotationIds.length, onClick: () => {
                                                            setAnnotationIds([]);
                                                            invalidate();
                                                        }, children: t("Clear annotations") })] }), !eligibleAnnotations.length && (_jsx("p", { children: t("Select source messages with annotations, or add a node annotation to this journey.") })), eligibleAnnotations.map((a) => (_jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: annotationIds.includes(a.id), onChange: (e) => {
                                                            setAnnotationIds((old) => e.target.checked
                                                                ? [...old, a.id]
                                                                : old.filter((id) => id !== a.id));
                                                            invalidate();
                                                        } }), _jsx("span", { children: a.comments.map((c) => c.body).join(" · ") })] }, a.id)))] })), _jsxs("label", { children: [t("Redact exact text (one phrase per line)"), _jsx("textarea", { rows: 5, value: redactions, onChange: (e) => {
                                                    setRedactions(e.target.value);
                                                    invalidate();
                                                }, placeholder: t("Sensitive phrases are replaced on the server.") })] }), _jsx(ReplacementRules, { rules: replacementRules, disabled: !rulesLoaded, onChange: (rules) => {
                                            setReplacementRules(rules);
                                            invalidate();
                                        } }), _jsxs("label", { children: [t("Publication visibility"), _jsxs("select", { value: visibility, onChange: (e) => {
                                                    setVisibility(e.target.value);
                                                    invalidate();
                                                }, children: [_jsx("option", { value: "Unlisted", children: t("Unlisted") }), _jsx("option", { value: "Public", children: t("Public") })] })] }), _jsx("button", { className: "primary", disabled: !rulesLoaded || previewLoading || selectedFindings.length > 0, onClick: () => act(() => reviewPreview()), children: t("Review sanitized preview") }), previewLoading && _jsx("p", { role: "status", children: t("Updating preview…") }), preview && (_jsxs(_Fragment, { children: [_jsx("h3", { children: t("Pre-publication review") }), preview.findings.length ? (_jsxs(_Fragment, { children: [_jsx("p", { className: "error", children: t("Possible sensitive text remains:") }), _jsx("p", { children: t("Select the text you want to redact, then apply your selection.") }), _jsxs("div", { className: "row", children: [_jsx("button", { type: "button", onClick: () => {
                                                                    setSelectedFindings(preview.findings);
                                                                    setConfirmed(false);
                                                                }, children: t("Select all findings") }), _jsx("button", { type: "button", disabled: !selectedFindings.length, onClick: () => setSelectedFindings([]), children: t("Clear selection") })] }), preview.findings.map((s) => (_jsxs("label", { className: "checkbox-row finding", children: [_jsx("input", { type: "checkbox", checked: selectedFindings.includes(s), onChange: (e) => {
                                                                    setSelectedFindings((selected) => e.target.checked
                                                                        ? [...selected, s]
                                                                        : selected.filter((item) => item !== s));
                                                                    setConfirmed(false);
                                                                } }), _jsx("span", { children: s })] }, s))), _jsx("button", { type: "button", className: "redact-selected", disabled: !selectedFindings.length, onClick: () => act(async () => {
                                                            const next = [
                                                                ...new Set([
                                                                    ...redactions.split("\n").filter(Boolean),
                                                                    ...selectedFindings,
                                                                ]),
                                                            ].join("\n");
                                                            setRedactions(next);
                                                            await reviewPreview(next);
                                                        }), children: t("Redact selected ({count})", {
                                                            count: selectedFindings.length,
                                                        }) })] })) : (_jsx("p", { className: "success", children: t("No common credential, email, or path patterns detected.") })), _jsx("small", { children: t("Pattern scanning is not exhaustive. Review the exact snapshot below.") }), _jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: confirmed, disabled: selectedFindings.length > 0, onChange: (e) => setConfirmed(e.target.checked) }), t("I reviewed the included content and explicitly confirm publication.")] }), _jsx("button", { className: "primary", disabled: !confirmed || selectedFindings.length > 0, onClick: () => act(async () => setSlug((await api(base + "/publish", request)).slug)), children: conversation
                                                    ? t(visibility === "Public"
                                                        ? "Create public link"
                                                        : "Create unlisted link")
                                                    : t(visibility === "Public"
                                                        ? "Publish public snapshot"
                                                        : "Publish unlisted snapshot") })] }))] })] }), preview && (_jsx("div", { className: "publication-preview", children: _jsx(Snapshot, { data: preview.snapshot }) }))] })), _jsxs("section", { className: "share-history", "aria-label": t("Previously shared links"), children: [_jsx("h3", { children: t("Previously shared links") }), _jsx("p", { children: t("Copy an existing link, or delete it to revoke visitor access. Each link is a separate snapshot.") }), historyLoading && _jsx("p", { role: "status", children: t("Loading shared links…") }), historyError && (_jsx("p", { role: "alert", className: "error", children: historyError })), !historyLoading && !historyError && !publications.length && (_jsx("p", { children: t("No shared links yet.") })), publications.map((p) => (_jsxs("div", { className: "share-history-row", children: [_jsxs("div", { className: "share-history-info", children: [_jsx("strong", { children: t(p.visibility) }), " \u00B7", " ", _jsx("time", { dateTime: p.createdAt, children: new Date(p.createdAt).toLocaleString() }), _jsx("a", { href: "#public/" + p.slug, target: "_blank", rel: "noreferrer", children: linkFor(p.slug) }), p.isLegacy && (_jsx("small", { children: t("To enable discussion on an older link, create a new shared link.") })), p.canManageDiscussion && (_jsx(DiscussionToggle, { enabled: p.discussionEnabled, save: async (enabled) => {
                                            await api("/public/" + p.slug + "/discussion", { enabled }, "PATCH");
                                            setPublications((rows) => rows.map((row) => row.id === p.id
                                                ? { ...row, discussionEnabled: enabled }
                                                : row));
                                        } }))] }), _jsxs("div", { className: "share-history-actions", children: [confirmDelete !== p.id && (_jsxs("button", { type: "button", onClick: () => act(async () => {
                                            await navigator.clipboard.writeText(linkFor(p.slug));
                                            useApp.getState().notify(t("Link copied"));
                                        }), children: [_jsx(Copy, { size: 14 }), t("Copy link")] })), confirmDelete !== p.id ? (_jsx("button", { type: "button", className: "share-delete", "aria-label": t("Delete link"), title: t("Delete link"), onClick: () => setConfirmDelete(p.id), children: _jsx(Trash2, { size: 15 }) })) : (_jsxs("div", { className: "share-delete-confirm", role: "group", "aria-label": t("Revoke this link?"), children: [_jsx("span", { children: t("Revoke this link?") }), _jsx("button", { type: "button", className: "share-delete", "aria-label": t("Confirm delete"), title: t("Confirm delete"), disabled: !!deleting, onClick: async () => {
                                                    setDeleting(p.id);
                                                    setHistoryError("");
                                                    try {
                                                        await api(base + "/publications/" + p.id, undefined, "DELETE");
                                                        setPublications((rows) => rows.filter((row) => row.id !== p.id));
                                                        setConfirmDelete("");
                                                        if (slug === p.slug)
                                                            setSlug("");
                                                        useApp.getState().notify(t("Shared link deleted"));
                                                    }
                                                    catch (e) {
                                                        setHistoryError(e.message);
                                                    }
                                                    finally {
                                                        setDeleting("");
                                                    }
                                                }, children: _jsx(Check, { size: 15 }) }), _jsx("button", { type: "button", disabled: !!deleting, "aria-label": t("Cancel"), title: t("Cancel"), onClick: () => setConfirmDelete(""), children: _jsx(X, { size: 15 }) })] }))] })] }, p.id)))] })] }));
}
function SharedAnnotations({ annotations, messageId, nodeId, }) {
    const threads = (annotations || []).filter((a) => messageId ? a.messageId === messageId : !a.messageId && a.nodeId === nodeId);
    return (_jsx(_Fragment, { children: threads.map((a, i) => (_jsxs("details", { className: "shared-annotation", children: [_jsxs("summary", { "aria-label": t("Shared annotation") + " " + (i + 1), title: t("Show or hide annotation"), children: [_jsx("span", { className: "annotation-marker", "aria-hidden": "true", children: "[!]" }), _jsx(AnnotationTopics, { annotation: a })] }), _jsxs("aside", { className: "published-curation", children: [_jsxs("small", { children: [t("SHARED ANNOTATION"), " \u00B7", " ", a.resolved ? t("Resolved") : t("Open")] }), a.quote && _jsx("blockquote", { children: a.quote }), a.comments.map((c, index) => (_jsxs("div", { children: [index > 0 && _jsxs("small", { children: [t("Reply"), " \u00B7 "] }), _jsx(AnnotationContent, { body: c.text })] }, index)))] })] }, i))) }));
}
const publishedMessagePage = 20;
function excerptBlocks(message) {
    return (message.blocks || []).filter((block) => typeof block.text === "string" && block.text.trim());
}
function shownPublishedMessages(messages, annotations) {
    const annotated = new Set((annotations || [])
        .map((annotation) => annotation.messageId)
        .filter((id) => typeof id === "string" && !!id));
    return messages.filter((message) => excerptBlocks(message).length > 0 || annotated.has(message.id));
}
function PublishedMessage({ message, annotations, shared = false, }) {
    const blocks = excerptBlocks(message);
    const threads = (annotations || []).filter((annotation) => annotation.messageId === message.id);
    if (!blocks.length && !threads.length)
        return null;
    return (_jsxs("article", { className: "public-message", "data-message-role": message.role, children: [blocks.length > 0 && (_jsxs(_Fragment, { children: [_jsxs("small", { className: "public-message-label", children: [_jsxs("strong", { children: [t("ORIGINAL"), " ", t(message.role)] }), " ", t(shared ? "· SHARED EXCERPT" : "· PUBLISHED EXCERPT")] }), blocks.map((block, index) => (_jsx(Markdown, { text: block.text }, index)))] })), threads.length > 0 && (_jsx(SharedAnnotations, { annotations: annotations, messageId: message.id }))] }));
}
function publishedMessages(step, data) {
    if (step?.messages?.length)
        return step.messages;
    const id = step?.conversationId;
    if (!id)
        return [];
    return ((data.sources || []).find((source) => source.id === id)?.messages || []);
}
function Snapshot({ data }) {
    useLocale();
    const [index, setIndex] = useState(0);
    const [messagePage, setMessagePage] = useState(0);
    const [all, setAll] = useState(false);
    const [canvas, setCanvas] = useState(false);
    const steps = data.steps || [];
    const totalMessages = useMemo(() => steps.reduce((sum, step) => sum +
        shownPublishedMessages(publishedMessages(step, data), data.annotations).length, 0), [steps, data]);
    const heavy = totalMessages > publishedMessagePage * 2;
    const flowNodes = useMemo(() => {
        const raw = steps.map((s, i) => ({
            x: Number.isFinite(s.position?.x) ? s.position.x : (i % 4) * 280,
            y: Number.isFinite(s.position?.y)
                ? s.position.y
                : Math.floor(i / 4) * 170,
        }));
        const gap = (values) => {
            const sorted = [
                ...new Set(values.map((value) => Math.round(value))),
            ].sort((a, b) => a - b);
            let min = Infinity;
            for (let i = 1; i < sorted.length; i++)
                min = Math.min(min, sorted[i] - sorted[i - 1]);
            return min;
        };
        const minGap = Math.min(gap(raw.map((point) => point.x)), gap(raw.map((point) => point.y)));
        const scale = !Number.isFinite(minGap) || minGap >= 240 ? 1 : 260 / minGap;
        const minX = raw.length ? Math.min(...raw.map((point) => point.x)) : 0;
        const minY = raw.length ? Math.min(...raw.map((point) => point.y)) : 0;
        return steps.map((s, i) => ({
            id: s.id,
            position: {
                x: (raw[i].x - minX) * scale,
                y: (raw[i].y - minY) * scale,
            },
            data: { label: s.title },
            style: { fontSize: 12, width: 220, padding: 16 },
        }));
    }, [steps]);
    const openChapter = (next) => {
        setIndex(next);
        setMessagePage(0);
        setAll(false);
        setCanvas(false);
    };
    if (data.kind === "conversation")
        return (_jsxs("div", { className: "public-content standalone-share", children: [_jsx("span", { className: "eyebrow", children: t("SHARED CONVERSATION") }), _jsx("h1", { children: data.title }), _jsx(Badge, { provider: data.provider }), _jsxs("p", { className: "subtitle", children: [t("Selected messages · Read-only snapshot ·"), " ", count("messages", shownPublishedMessages(data.messages || [], data.annotations)
                            .length)] }), _jsx(ConversationFilters, { messages: shownPublishedMessages(data.messages || [], data.annotations), annotations: data.annotations, renderMessage: (m) => (_jsx(PublishedMessage, { message: m, annotations: data.annotations, shared: true }, m.id)) })] }));
    return (_jsxs("div", { className: "public-content", children: [_jsx("span", { className: "eyebrow", children: t("A CURATED AI JOURNEY") }), _jsx("h1", { children: data.title }), _jsx("p", { className: "subtitle", children: data.description }), _jsxs("div", { className: "public-progress", children: [_jsx("progress", { max: Math.max(steps.length, 1), value: index + 1 }), _jsx("span", { children: t("Chapters shown", {
                            current: steps.length ? index + 1 : 0,
                            total: steps.length,
                        }) }), heavy ? (_jsx("span", { children: t("Open one chapter at a time. This publication has too many messages to show at once.") })) : (_jsx("button", { onClick: () => setAll(!all), children: all ? t("Guided reading") : t("Read all chapters") }))] }), _jsx("p", { className: "subtitle", children: "Chapters are a recommended reading order, not an execution timeline. Explore the canvas to see parallel branches and shared outcomes." }), _jsx("button", { onClick: () => setCanvas(!canvas), children: canvas ? t("Return to guided reading") : t("Explore journey canvas") }), canvas ? (_jsx("div", { className: "public-canvas", children: _jsxs(ReactFlow, { ariaLabelConfig: {
                        "controls.zoomIn.ariaLabel": t("Zoom in"),
                        "controls.zoomOut.ariaLabel": t("Zoom out"),
                        "controls.fitView.ariaLabel": t("Fit view"),
                        "controls.interactive.ariaLabel": t("Toggle interactivity"),
                    }, nodes: flowNodes, edges: data.connections || [], onlyRenderVisibleElements: true, minZoom: 0.05, nodesDraggable: false, nodesConnectable: false, elementsSelectable: true, onNodeClick: (_, node) => {
                        const next = steps.findIndex((s) => s.id === node.id);
                        if (next >= 0)
                            openChapter(next);
                    }, fitView: true, fitViewOptions: { padding: 0.15, minZoom: 0.35 }, children: [_jsx(Background, {}), _jsx(Controls, {}), _jsx(MiniMap, {})] }) })) : (_jsxs("div", { className: "public-layout", children: [_jsx("aside", { children: steps.map((s, i) => (_jsxs("button", { className: index === i ? "active" : "", onClick: () => openChapter(i), children: [_jsx("span", { children: String(i + 1).padStart(2, "0") }), s.title] }, s.id))) }), _jsxs("main", { children: [(all && !heavy ? steps : steps.slice(index, index + 1)).map((s) => {
                                const messages = shownPublishedMessages(publishedMessages(s, data), data.annotations);
                                const start = messagePage * publishedMessagePage;
                                const page = messages.slice(start, start + publishedMessagePage);
                                return (_jsxs("section", { children: [_jsx("span", { className: "eyebrow", children: s.kind === "conversation"
                                                ? t("PRESERVED CONVERSATION")
                                                : t("CURATOR MILESTONE") }), _jsx("h2", { children: s.title }), s.provider && _jsx(Badge, { provider: s.provider }), " ", s.commentary && (_jsxs("div", { className: "published-curation", children: [_jsx("small", { children: t("CURATOR EXPLANATION") }), _jsx(Markdown, { text: s.commentary })] })), _jsx(SharedAnnotations, { annotations: data.annotations, nodeId: s.id }), page.map((m) => (_jsx(PublishedMessage, { message: m, annotations: data.annotations }, m.id))), messages.length > publishedMessagePage && (_jsxs("div", { className: "public-pagination", children: [_jsx("button", { disabled: start === 0, onClick: () => setMessagePage((page) => page - 1), children: t("Earlier messages") }), _jsx("span", { children: t("Showing {from}–{to} of {total}", {
                                                        from: start + 1,
                                                        to: Math.min(start + publishedMessagePage, messages.length),
                                                        total: messages.length,
                                                    }) }), _jsx("button", { disabled: start + publishedMessagePage >= messages.length, onClick: () => setMessagePage((page) => page + 1), children: t("Later messages") })] }))] }, s.id));
                            }), !steps.length && (_jsx("p", { children: t("No source messages were selected for publication.") })), _jsxs("div", { className: "public-pagination", children: [_jsxs("button", { disabled: index === 0, onClick: () => {
                                            setIndex((i) => i - 1);
                                            setMessagePage(0);
                                        }, children: [_jsx(ArrowLeft, { size: 16 }), t("Previous")] }), _jsxs("button", { disabled: index >= steps.length - 1, className: "primary", onClick: () => {
                                            setIndex((i) => i + 1);
                                            setMessagePage(0);
                                        }, children: [t("Next chapter"), _jsx(ArrowRight, { size: 16 })] })] })] })] }))] }));
}
export function PublicJourney({ slug }) {
    useLocale();
    const [data, setData] = useState(null);
    const [error, setError] = useState("");
    useEffect(() => {
        api("/public/" + slug)
            .then(setData)
            .catch((e) => setError(e.message));
    }, [slug]);
    return (_jsxs("div", { className: "public-page", children: [_jsxs("header", { children: [_jsxs("a", { className: "brand", href: "#dashboard", children: [_jsx(BrandMark, {}), "openhow"] }), _jsxs("span", { children: [data?.kind === "conversation"
                                ? t("Shared conversation")
                                : t("Published journey"), " ", t("· Read-only snapshot")] }), _jsx(LanguagePicker, {}), _jsx("button", { className: "discussion-jump", onClick: () => {
                            const section = document.getElementById("discussion");
                            section?.scrollIntoView({ behavior: "smooth" });
                            section?.focus({ preventScroll: true });
                        }, children: t("Discussion") }), _jsx("a", { href: "#dashboard", children: t("Open your workspace ↗") })] }), data ? (_jsx(Snapshot, { data: data })) : (_jsx("div", { className: "empty", children: error || t("Opening published journey…") })), data && _jsx(Discussion, { slug: slug }, slug), _jsx("footer", { children: t("The original conversations are the source. The journey is a curated perspective.") })] }));
}
