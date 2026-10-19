import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { AnnotationContent, AnnotationTopics, AnnotationTools, TopicFilter, } from "../review/AnnotationContent";
import { threadTags } from "../review/annotationParsing";
import { AnnotationArtifacts } from "../review/AnnotationArtifacts";
import { t, useLocale, count, formatDate } from "../../shared/i18n/i18n";
import { useState, useEffect } from "react";
import { ReactFlow, Background, MiniMap, Controls, Handle, Position, addEdge, applyNodeChanges, applyEdgeChanges, } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { useEditor, EditorContent } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import { ArrowLeft, Plus, Save, Undo2, Redo2, Share2, Play, LayoutGrid, Search, X, ExternalLink, Flag, Lightbulb, FlaskConical, FileText, Folder, Paperclip, TriangleAlert, MessageSquare, Wrench, } from "lucide-react";
import { api, act, go, graph, useApp, providerClass, useLibrary, } from "../../shared/state";
import { Badge } from "../../shared/ui/ui";
const icons = {
    conversation: MessageSquare,
    milestone: Flag,
    decision: Lightbulb,
    problem: TriangleAlert,
    experiment: FlaskConical,
    note: FileText,
    artifact: Paperclip,
    group: Folder,
};
const relations = [
    "Parent session",
    "Recorded tool execution",
    "Continues from",
    "Inspired by",
    "Alternative approach",
    "Replaced by",
    "Failed because",
    "Validated by",
    "Depends on",
    "Related to",
];
function TimelineDetails({ data }) {
    return (_jsxs("div", { className: "timeline-meta", children: [_jsx("div", { className: "lane-label", children: t("Lane: {name}", { name: data.laneLabel }) }), _jsx("div", { children: data.timelineStart
                    ? formatDate(data.timelineStart, {
                        dateStyle: "short",
                        timeStyle: "short",
                    })
                    : t("Time unknown") }), data.timelineEnd && data.timelineEnd !== data.timelineStart && (_jsx("div", { children: t("Until {time}", {
                    time: formatDate(data.timelineEnd, {
                        dateStyle: "short",
                        timeStyle: "short",
                    }),
                }) }))] }));
}
function JourneyNode({ data, selected }) {
    useLocale();
    const Icon = data.recordedExecution ? Wrench : icons[data.kind] || FileText;
    return (_jsxs("div", { className: "journey-node " +
            (data.timeline || data.timelineLaneHeader ? "timeline-node " : "") +
            (selected ? "selected " : "") +
            data.kind, children: [_jsx(Handle, { id: "in", type: "target", position: Position.Left, isConnectable: true, "aria-label": t("Connection point") }), _jsxs("div", { className: "node-kind", children: [_jsx(Icon, { size: 13 }), _jsx("span", { children: data.recordedExecution
                            ? t("Recorded tool execution")
                            : data.kind === "conversation"
                                ? data.provider
                                : t(data.kind) }), data.kind === "conversation" && (_jsx("span", { className: "node-provider " + providerClass(data.provider), children: "\u25CF" }))] }), _jsx("strong", { children: data.title }), _jsx("div", { className: "node-meta", children: data.kind === "conversation"
                    ? `${count("messages", data.messageCount ?? 0)} · ${data.model || t("Imported source")}`
                    : t(data.status || "Curator commentary") }), data.timeline && _jsx(TimelineDetails, { data: data }), _jsx(Handle, { id: "out", type: "source", position: Position.Right, isConnectable: true, "aria-label": t("Connection point") })] }));
}
const nodeTypes = { journey: JourneyNode };
export function RichEditor({ value, onChange, disabled = false, }) {
    useLocale();
    const editor = useEditor({
        extensions: [StarterKit],
        content: value,
        editable: !disabled,
        onUpdate: ({ editor }) => onChange(editor.getHTML()),
    });
    useEffect(() => {
        if (editor && editor.getHTML() !== value)
            editor.commands.setContent(value, { emitUpdate: false });
    }, [value]);
    useEffect(() => {
        editor?.setEditable(!disabled);
    }, [disabled]);
    return (_jsxs("div", { className: "rich-editor", children: [_jsxs("div", { className: "row", children: [_jsx("button", { disabled: disabled, onClick: () => editor?.chain().focus().toggleBold().run(), children: _jsx("b", { "aria-label": t("Bold"), children: "B" }) }), _jsx("button", { disabled: disabled, onClick: () => editor?.chain().focus().toggleItalic().run(), children: _jsx("i", { "aria-label": t("Italic"), children: "I" }) }), _jsx("button", { disabled: disabled, onClick: () => editor?.chain().focus().toggleBulletList().run(), children: t("• List") }), _jsx("small", { children: t("Curator-written content") })] }), _jsx(EditorContent, { editor: editor })] }));
}
export function JourneyCanvas({ journey, canEdit, canPublish, onPublish, refresh, user, members, online, }) {
    useLocale();
    const initial = graph(journey);
    const [nodes, setNodes] = useState(initial.nodes);
    const [edges, setEdges] = useState(initial.edges);
    const [steps, setSteps] = useState(initial.steps || []);
    const [selected, setSelected] = useState(null);
    const [edgeId, setEdgeId] = useState(null);
    const [panel, setPanel] = useState(false);
    const [query, setQuery] = useState("");
    const [past, setPast] = useState([]);
    const [future, setFuture] = useState([]);
    const [version, setVersion] = useState(journey.version);
    const [dirty, setDirty] = useState(false);
    const [conversation, setConversation] = useState(null);
    const [title, setTitle] = useState(journey.title);
    const [visibility, setVisibility] = useState(journey.visibility);
    const [guided, setGuided] = useState(false);
    const [step, setStep] = useState(0);
    const [collapsed, setCollapsed] = useState(initial.collapsed || []);
    const [flow, setFlow] = useState(null);
    const [comments, setComments] = useState([]);
    const [body, setBody] = useState("");
    const [topic, setTopic] = useState("");
    const { theme, workspace, notify } = useApp();
    const [find, setFind] = useState("");
    const library = useLibrary(panel ? workspace : "", { q: find, limit: 30 });
    const node = nodes.find((n) => n.id === selected);
    const edge = edges.find((e) => e.id === edgeId);
    useEffect(() => {
        if (journey.version === version)
            return;
        if (dirty) {
            notify(t("A collaborator saved a newer version. Save will detect the conflict; reload to use their version."));
            return;
        }
        const incoming = graph(journey);
        setNodes(incoming.nodes);
        setEdges(incoming.edges);
        setSteps(incoming.steps || []);
        setVersion(journey.version);
        setTitle(journey.title);
        setVisibility(journey.visibility);
        setCollapsed(incoming.collapsed || []);
    }, [journey.version]);
    const checkpoint = () => {
        setPast((p) => [...p.slice(-39), { nodes, edges, steps }]);
        setFuture([]);
        setDirty(true);
    };
    useEffect(() => {
        const fn = (e) => {
            if (dirty)
                e.preventDefault();
        };
        window.addEventListener("beforeunload", fn);
        return () => window.removeEventListener("beforeunload", fn);
    }, [dirty]);
    useEffect(() => {
        setConversation(null);
        if (node?.data.conversationId)
            act(async () => setConversation(await api("/conversations/" + node.data.conversationId + "?limit=3")));
        if (selected)
            act(async () => setComments(await api("/w/" +
                workspace +
                "/annotations?journeyId=" +
                journey.id +
                "&nodeId=" +
                encodeURIComponent(selected))));
        else
            setComments([]);
    }, [selected]);
    const update = (patch) => {
        checkpoint();
        setNodes((ns) => ns.map((n) => n.id === selected
            ? {
                ...n,
                data: {
                    ...n.data,
                    ...patch,
                    ...(patch.groupId !== undefined && n.data.timeline
                        ? {
                            laneLabel: ns.find((g) => g.id === patch.groupId)?.data.title ||
                                t("No group"),
                        }
                        : {}),
                },
            }
            : node?.data.timelineLaneHeader &&
                patch.title !== undefined &&
                n.data.groupId === selected
                ? { ...n, data: { ...n.data, laneLabel: patch.title } }
                : n));
    };
    const add = (kind, c) => {
        checkpoint();
        const n = {
            id: crypto.randomUUID(),
            type: "journey",
            position: {
                x: 150 + (nodes.length % 4) * 300,
                y: 150 + Math.floor(nodes.length / 4) * 180,
            },
            data: {
                kind,
                title: c?.title || "Untitled " + kind,
                conversationId: c?.id,
                provider: c?.provider,
                model: c?.model,
                messageCount: c?.messageCount,
                body: "",
            },
        };
        setNodes((ns) => [...ns, n]);
        setSteps((s) => [...s, n.id]);
        setSelected(n.id);
        setPanel(false);
    };
    const addConversations = (items) => {
        const present = new Set(nodes
            .map((n) => n.data.conversationId)
            .filter((id) => typeof id === "string" && !!id));
        const fresh = items.filter((c) => c?.id && !present.has(c.id));
        if (!fresh.length)
            return;
        checkpoint();
        const created = fresh.map((c, index) => ({
            id: crypto.randomUUID(),
            type: "journey",
            position: {
                x: 150 + ((nodes.length + index) % 4) * 300,
                y: 150 + Math.floor((nodes.length + index) / 4) * 180,
            },
            data: {
                kind: "conversation",
                title: c.title || "Untitled conversation",
                conversationId: c.id,
                provider: c.provider,
                model: c.model,
                messageCount: c.messageCount,
                body: "",
            },
        }));
        setNodes((ns) => [...ns, ...created]);
        setSteps((s) => [...s, ...created.map((n) => n.id)]);
    };
    const save = () => act(async () => {
        const result = await api("/journeys/" + journey.id, {
            title,
            description: journey.description,
            visibility,
            graph: JSON.stringify({
                nodes,
                edges,
                viewport: flow?.getViewport(),
                collapsed,
                timeline: initial.timeline,
                steps: steps.filter((id) => nodes.some((n) => n.id === id)),
            }),
            version,
        }, "PUT");
        setVersion(result.version);
        setDirty(false);
        refresh();
        notify(t("Journey saved. Original conversations are unchanged."));
    });
    const undo = (redo = false) => {
        const list = redo ? future : past;
        if (!list.length)
            return;
        const snapshot = list[list.length - 1];
        if (redo) {
            setFuture(list.slice(0, -1));
            setPast([...past, { nodes, edges, steps }]);
        }
        else {
            setPast(list.slice(0, -1));
            setFuture([...future, { nodes, edges, steps }]);
        }
        setNodes(snapshot.nodes);
        setEdges(snapshot.edges);
        setSteps(snapshot.steps);
        setDirty(true);
    };
    const layout = () => {
        checkpoint();
        if (initial.timeline) {
            const headers = nodes.filter((n) => n.data.timelineLaneHeader);
            const laneIds = headers.map((n) => n.id);
            const times = [
                ...new Set(nodes
                    .filter((n) => n.data.timelineStart)
                    .map((n) => String(n.data.timelineStart))),
            ].sort();
            let extra = 0;
            setNodes((ns) => ns.map((n) => {
                const header = laneIds.indexOf(n.id);
                if (header >= 0)
                    return { ...n, position: { x: 0, y: header * 210 } };
                const lane = laneIds.indexOf(String(n.data.groupId));
                if (n.data.timeline && lane >= 0)
                    return {
                        ...n,
                        position: {
                            x: 340 +
                                (n.data.timelineStart
                                    ? times.indexOf(String(n.data.timelineStart))
                                    : times.length) *
                                    320,
                            y: lane * 210,
                        },
                    };
                return {
                    ...n,
                    position: { x: 340 + extra++ * 320, y: headers.length * 210 },
                };
            }));
            return;
        }
        const ranks = new Map(nodes.map((n) => [n.id, 0]));
        for (let i = 0; i < nodes.length; i++) {
            let changed = false;
            for (const e of edges) {
                const r = Math.min(nodes.length - 1, (ranks.get(e.source) || 0) + 1);
                if (r > (ranks.get(e.target) || 0)) {
                    ranks.set(e.target, r);
                    changed = true;
                }
            }
            if (!changed)
                break;
        }
        const counts = new Map();
        setNodes((ns) => ns.map((n) => {
            const r = ranks.get(n.id) || 0;
            const y = counts.get(r) || 0;
            counts.set(r, y + 1);
            return { ...n, position: { x: r * 315, y: y * 190 } };
        }));
    };
    const visible = nodes.map((n) => ({
        ...n,
        hidden: !!(n.data.groupId && collapsed.includes(String(n.data.groupId))) ||
            (!!query &&
                !String(n.data.title).toLowerCase().includes(query.toLowerCase())),
        style: { opacity: guided && steps[step] !== n.id?.toString() ? 0.35 : 1 },
    }));
    return (_jsxs("div", { className: "canvas-page", children: [_jsxs("div", { className: "canvas-title", children: [_jsx("button", { title: t("Back to journeys"), onClick: () => go("journeys"), children: _jsx(ArrowLeft, { size: 19 }) }), _jsxs("div", { children: [_jsx("input", { className: "title-input", "aria-label": t("Journey title"), value: title, disabled: !canEdit, onChange: (e) => {
                                    setTitle(e.target.value);
                                    setDirty(true);
                                } }), _jsxs("small", { children: [_jsx("span", { className: "dot" }), " ", dirty ? t("Unsaved changes") : t("Saved to workspace"), " ", _jsx("span", { children: "\u00B7" }), " ", count("nodes", nodes.length), " ", _jsx("span", { children: "\u00B7" }), " ", count("connections", edges.length)] })] }), _jsx("span", { className: "spacer" }), _jsx("div", { className: "avatar-stack", children: members
                            .filter((m) => online.includes(m.id))
                            .slice(0, 3)
                            .map((m) => (_jsx("span", { className: "avatar small", title: m.name + " · viewing now", children: m.name[0] }, m.id))) }), _jsxs("button", { onClick: () => {
                            setGuided(!guided);
                            setStep(0);
                        }, children: [_jsx(Play, { size: 15 }), " ", t("Guided view")] }), canPublish && (_jsxs("button", { onClick: () => {
                            if (dirty)
                                notify(t("Save your changes before publishing."));
                            else
                                onPublish();
                        }, children: [_jsx(Share2, { size: 15 }), " ", t("Publish")] })), _jsxs("button", { className: "primary", disabled: !canEdit || !dirty, onClick: save, children: [_jsx(Save, { size: 15 }), t("Save journey")] })] }), _jsxs("div", { className: "canvas-toolbar", children: [_jsxs("button", { className: "primary subtle", disabled: !canEdit, onClick: () => setPanel(!panel), children: [_jsx(Plus, { size: 16 }), " ", t("Add to journey")] }), _jsx("div", { className: "divider" }), _jsx("button", { title: t("Undo"), disabled: !past.length || !canEdit, onClick: () => undo(), children: _jsx(Undo2, { size: 16 }) }), _jsx("button", { title: t("Redo"), disabled: !future.length || !canEdit, onClick: () => undo(true), children: _jsx(Redo2, { size: 16 }) }), _jsxs("button", { disabled: !canEdit, onClick: layout, children: [_jsx(LayoutGrid, { size: 16 }), t("Auto layout")] }), _jsx("span", { className: "spacer" }), _jsxs("label", { className: "search compact", children: [_jsx(Search, { size: 15 }), _jsx("input", { placeholder: t("Find a node…"), value: query, onChange: (e) => setQuery(e.target.value) })] }), _jsxs("select", { "aria-label": t("Journey visibility"), disabled: !canEdit, value: visibility, onChange: (e) => {
                            setVisibility(e.target.value);
                            setDirty(true);
                        }, children: [_jsx("option", { value: "Private", children: t("Private") }), _jsx("option", { value: "Team", children: t("Team") })] })] }), initial.timeline && (_jsxs("div", { className: "timeline-notice", children: [_jsx("strong", { children: t("Generated timeline draft") }), " · ", t("Left to right: source time. Spacing does not represent duration. Lanes separate threads and tool executions."), nodes.some((n) => n.data.timeline && !n.data.timelineStart) && (_jsx("div", { children: t("Some activity has no recorded start time and is placed after dated activity.") })), initial.timeline.omittedRelationshipCount > 0 && (_jsx("div", { children: t("{count} relationships could not be included because their targets were unselected or unresolved.", { count: initial.timeline.omittedRelationshipCount }) }))] })), _jsxs("div", { className: "canvas-body", children: [_jsxs(ReactFlow, { ariaLabelConfig: {
                            "controls.zoomIn.ariaLabel": t("Zoom in"),
                            "controls.zoomOut.ariaLabel": t("Zoom out"),
                            "controls.fitView.ariaLabel": t("Fit view"),
                            "controls.interactive.ariaLabel": t("Toggle interactivity"),
                        }, onInit: setFlow, defaultViewport: initial.viewport, onMoveEnd: (event) => {
                            if (event && canEdit)
                                setDirty(true);
                        }, nodes: visible, edges: edges, nodeTypes: nodeTypes, colorMode: theme, onNodesChange: (changes) => {
                            if (canEdit) {
                                if (changes.some((c) => c.type === "remove"))
                                    checkpoint();
                                setNodes((ns) => applyNodeChanges(changes, ns));
                                if (changes.some((c) => c.type !== "select" && c.type !== "dimensions"))
                                    setDirty(true);
                            }
                        }, onEdgesChange: (changes) => {
                            if (canEdit) {
                                setEdges((es) => applyEdgeChanges(changes, es));
                                if (changes.some((c) => c.type !== "select"))
                                    setDirty(true);
                            }
                        }, onNodeDragStart: checkpoint, onConnect: (c) => {
                            if (!canEdit || !c.source || !c.target || c.source === c.target)
                                return;
                            checkpoint();
                            setEdges((es) => addEdge({
                                ...c,
                                label: "Continues from",
                                type: "smoothstep",
                                markerEnd: { type: "arrowclosed" },
                            }, es));
                            setDirty(true);
                        }, connectionRadius: 40, connectionLineStyle: { stroke: "#4369bc", strokeWidth: 2 }, onNodeClick: (_, n) => {
                            setSelected(n.id);
                            setEdgeId(null);
                        }, onNodeDoubleClick: (_, n) => {
                            if (n.data.conversationId)
                                go("explorer/" + n.data.conversationId);
                            else if (n.data.kind === "group")
                                setCollapsed((s) => s.includes(n.id) ? s.filter((i) => i !== n.id) : [...s, n.id]);
                        }, onEdgeClick: (_, e) => {
                            setEdgeId(e.id);
                            setSelected(null);
                        }, nodesDraggable: canEdit, nodesConnectable: canEdit, deleteKeyCode: canEdit ? ["Backspace", "Delete"] : null, fitView: !initial.viewport, minZoom: 0.15, maxZoom: 2, defaultEdgeOptions: {
                            type: "smoothstep",
                            style: { stroke: "#96a5b8", strokeWidth: 1.4 },
                            labelStyle: { fontSize: 10, fill: "#6c7789" },
                            labelBgPadding: [6, 4],
                            labelBgBorderRadius: 4,
                        }, children: [_jsx(Background, { gap: 22, size: 1, color: theme === "light" ? "#d6dde5" : "#334155" }), _jsx(MiniMap, { pannable: true, zoomable: true, nodeColor: (n) => n.data.kind === "decision"
                                    ? "#d7a64b"
                                    : n.data.kind === "milestone"
                                        ? "#60a892"
                                        : "#c0cbdc" }), _jsx(Controls, {})] }), _jsxs("div", { className: "canvas-legend", children: [_jsxs("span", { children: [_jsx("i", { className: "legend-source" }), t("Imported source")] }), _jsxs("span", { children: [_jsx("i", { className: "legend-curation" }), t("Curator perspective")] }), _jsx("small", { children: t("Click to inspect · Double-click to explore") })] }), panel && (_jsxs("aside", { className: "node-library", children: [_jsxs("div", { className: "section-title", children: [_jsx("h3", { children: t("Add to your journey") }), _jsx("button", { title: t("Close node library"), onClick: () => setPanel(false), children: _jsx(X, { size: 16 }) })] }), _jsx("span", { className: "eyebrow", children: t("CURATOR CONTENT") }), _jsx("div", { className: "node-types", children: Object.entries(icons)
                                    .filter(([k]) => k !== "conversation")
                                    .map(([k, Icon]) => (_jsxs("button", { disabled: !canEdit, onClick: () => add(k), children: [_jsx(Icon, { size: 16 }), k] }, t(k)))) }), _jsx(LibraryPicker, { presentIds: nodes.flatMap((n) => typeof n.data.conversationId === "string"
                                    ? [n.data.conversationId]
                                    : []), addMany: addConversations, show: (conversationId) => {
                                    const existing = nodes.find((n) => n.data.conversationId === conversationId);
                                    if (!existing)
                                        return;
                                    setSelected(existing.id);
                                    setEdgeId(null);
                                    flow?.fitView({
                                        nodes: [{ id: existing.id }],
                                        duration: 200,
                                        padding: 0.4,
                                    });
                                } })] })), (node || edge) && (_jsxs("aside", { className: "inspector", children: [_jsxs("div", { className: "section-title", children: [_jsx("span", { className: "eyebrow", children: node ? t("NODE INSPECTOR") : t("CONNECTION") }), _jsx("button", { title: t("Close inspector"), onClick: () => {
                                            setSelected(null);
                                            setEdgeId(null);
                                        }, children: _jsx(X, { size: 17 }) })] }), edge ? (_jsxs(_Fragment, { children: [_jsx("h3", { children: t(edge.data?.recorded
                                            ? "Recorded relationship"
                                            : "Curator-defined relationship") }), _jsx("select", { disabled: !canEdit, value: String(edge.label), onChange: (e) => {
                                            checkpoint();
                                            setEdges((es) => es.map((x) => x.id === edge.id
                                                ? {
                                                    ...x,
                                                    label: e.target.value,
                                                    data: { ...x.data, recorded: false },
                                                }
                                                : x));
                                        }, children: [...new Set([...relations, String(edge.label)])].map((r) => (_jsx("option", { value: r, children: t(r) }, r))) }), _jsx("p", { children: t(edge.data?.recorded
                                            ? "This connection comes from an imported source relationship or tool execution. You can edit or remove it in this draft."
                                            : "This connection explains your interpretation. It does not change the original message tree.") }), _jsx("button", { disabled: !canEdit, onClick: () => {
                                            checkpoint();
                                            setEdges((es) => es.filter((e) => e.id !== edge.id));
                                            setEdgeId(null);
                                        }, children: t("Disconnect nodes") })] })) : (node && (_jsxs(_Fragment, { children: [_jsx("span", { className: "type-tag " + node.data.kind, children: t(String(node.data.kind)) }), _jsxs("label", { children: [t("Title"), _jsx("input", { disabled: !canEdit, value: String(node.data.title), onChange: (e) => update({ title: e.target.value }) })] }), node.data.timeline && _jsx(TimelineDetails, { data: node.data }), node.data.recordedExecution && (_jsxs("div", { className: "provenance", children: [_jsxs("p", { children: [t("Recorded tool execution"), " \u00B7", " ", String(node.data.provider), " \u00B7", " ", String(node.data.model || t("Model not provided"))] }), _jsx("p", { children: t(String(node.data.status)) }), _jsx("button", { onClick: () => go("explorer/" + node.data.sourceConversationId), children: t("Open source conversation") })] })), node.data.kind === "conversation" ? (_jsxs(_Fragment, { children: [_jsxs("div", { className: "provenance", children: [_jsx(Badge, { provider: String(node.data.provider) }), _jsx("span", { children: t("Immutable source") })] }), _jsxs("p", { children: [String(node.data.model || "Model not available"), " \u00B7", " ", count("messages", Number(node.data.messageCount || 0))] }), _jsxs("button", { className: "primary full", onClick: () => go("explorer/" + node.data.conversationId), children: [t("Open conversation"), " ", _jsx(ExternalLink, { size: 15 })] }), _jsx("div", { className: "source-excerpt", children: conversation?.messages.slice(0, 3).map((m) => (_jsxs("div", { children: [_jsxs("small", { children: [t("ORIGINAL"), " ", m.role.toUpperCase()] }), _jsx("p", { children: m.blocks
                                                                .map((b) => b.text)
                                                                .join("\n")
                                                                .slice(0, 350) })] }, m.id))) })] })) : (_jsxs(_Fragment, { children: [_jsx("label", { children: t("Curator explanation") }), _jsx(RichEditor, { value: String(node.data.body || ""), onChange: (body) => update({ body }), disabled: !canEdit }, node.id), node.data.kind === "experiment" && (_jsxs("select", { value: String(node.data.status || t("Inconclusive")), onChange: (e) => update({ status: e.target.value }), children: [_jsx("option", { value: "Inconclusive", children: t("Inconclusive") }), _jsx("option", { value: "Success", children: t("Success") }), _jsx("option", { value: "Failure", children: t("Failure") })] })), node.data.kind === "artifact" && (_jsxs("label", { children: [t("File, commit, or document reference"), _jsx("input", { value: String(node.data.reference || ""), disabled: !canEdit, onChange: (e) => update({ reference: e.target.value }) })] }))] })), _jsxs("label", { children: [t("Group"), _jsxs("select", { disabled: !canEdit, value: String(node.data.groupId || ""), onChange: (e) => update({ groupId: e.target.value }), children: [_jsx("option", { value: "", children: t("No group") }), nodes
                                                        .filter((n) => n.data.kind === "group" && n.id !== node.id)
                                                        .map((n) => (_jsx("option", { value: n.id, children: String(n.data.title) }, n.id)))] })] }), node.data.kind === "group" && (_jsx("button", { onClick: () => setCollapsed((s) => s.includes(node.id)
                                            ? s.filter((i) => i !== node.id)
                                            : [...s, node.id]), children: t("Collapse / expand group") })), _jsxs("label", { children: [t("Reading order"), _jsx("input", { type: "number", min: 1, max: nodes.length, value: steps.indexOf(node.id) + 1, disabled: !canEdit, onChange: (e) => {
                                                    checkpoint();
                                                    const next = steps.filter((i) => i !== node.id);
                                                    next.splice(Math.max(0, Number(e.target.value) - 1), 0, node.id);
                                                    setSteps(next);
                                                } })] }), _jsx("h3", { children: t("Node discussion") }), _jsx(TopicFilter, { annotations: comments, value: topic, onChange: setTopic }), comments
                                        .filter((a) => !topic || threadTags(a).includes(topic))
                                        .map((a) => (_jsxs("div", { className: "thread", children: [_jsx(AnnotationTopics, { annotation: a }), canEdit && threadTags(a).includes("action") && (_jsx("button", { type: "button", onClick: () => act(async () => {
                                                    await api("/annotations/" + a.id, { resolved: !a.resolved }, "PATCH");
                                                    setComments((current) => current.map((thread) => thread.id === a.id
                                                        ? { ...thread, resolved: !a.resolved }
                                                        : thread));
                                                }), children: t(a.resolved ? "Reopen" : "Complete action") })), _jsx(AnnotationArtifacts, { annotation: a, canUpload: canEdit, onUploaded: (file) => setComments((current) => current.map((thread) => thread.id === a.id
                                                    ? {
                                                        ...thread,
                                                        artifacts: [
                                                            ...(thread.artifacts || []),
                                                            file,
                                                        ],
                                                    }
                                                    : thread)) }), a.comments.map((c) => (_jsxs("div", { children: [members.find((m) => m.id === c.authorId)?.name, ":", " ", _jsx(AnnotationContent, { body: c.body })] }, c.id)))] }, a.id))), _jsxs("form", { onSubmit: (e) => {
                                            e.preventDefault();
                                            act(async () => {
                                                const a = await api("/w/" + workspace + "/annotations", { journeyId: journey.id, nodeId: node.id, body });
                                                setComments([...comments, a]);
                                                setBody("");
                                            });
                                        }, children: [_jsx("textarea", { placeholder: t("Add curator discussion…"), value: body, onChange: (e) => setBody(e.target.value) }), _jsx(AnnotationTools, { body: body, setBody: setBody }), _jsx("button", { disabled: !body.trim(), children: t("Add comment") })] }), _jsx("button", { className: "danger", disabled: !canEdit, onClick: () => {
                                            checkpoint();
                                            setNodes((ns) => ns.filter((n) => n.id !== node.id));
                                            setEdges((es) => es.filter((e) => e.source !== node.id && e.target !== node.id));
                                            setSelected(null);
                                        }, children: t("Remove from journey") })] })))] })), guided && (_jsxs("div", { className: "guided-bar", children: [_jsx("button", { disabled: step === 0, onClick: () => {
                                    setStep((s) => s - 1);
                                    setSelected(steps[step - 1]);
                                }, children: t("← Previous") }), _jsxs("div", { children: [_jsx("small", { children: t("Guided step", { current: step + 1, total: steps.length }) }), _jsx("strong", { children: String(nodes.find((n) => n.id === steps[step])?.data.title ||
                                            "Add a node to begin") })] }), _jsx("button", { disabled: step >= steps.length - 1, onClick: () => {
                                    setStep((s) => s + 1);
                                    setSelected(steps[step + 1]);
                                }, children: t("Next →") }), _jsx("button", { title: t("Exit guided view"), onClick: () => setGuided(false), children: _jsx(X, { size: 16 }) })] }))] })] }));
}
function LibraryPicker({ presentIds, addMany, show, }) {
    const [q, setQ] = useState("");
    const [picked, setPicked] = useState({});
    const { workspace } = useApp();
    const results = useLibrary(workspace, { q, limit: 20 });
    const present = new Set(presentIds);
    const chosen = Object.values(picked).filter((c) => !present.has(c.id));
    return (_jsxs(_Fragment, { children: [_jsx("span", { className: "eyebrow", children: t("FROM YOUR LIBRARY") }), _jsxs("label", { className: "search", children: [_jsx(Search, { size: 15 }), _jsx("input", { placeholder: t("Search conversations, messages, code, comments…"), value: q, onChange: (e) => setQ(e.target.value) })] }), results.items.map((c) => {
                const added = present.has(c.id);
                const selected = added || !!picked[c.id];
                return (_jsxs("button", { type: "button", className: "library-option" +
                        (added ? " added" : "") +
                        (selected && !added ? " picked" : ""), "aria-pressed": selected, onClick: () => {
                        if (added) {
                            show(c.id);
                            return;
                        }
                        setPicked((current) => {
                            const next = { ...current };
                            if (next[c.id])
                                delete next[c.id];
                            else
                                next[c.id] = c;
                            return next;
                        });
                    }, children: [_jsx("span", { className: "library-check", "aria-hidden": "true", children: selected ? "✓" : "" }), _jsxs("span", { children: [_jsx(Badge, { provider: c.provider }), _jsx("strong", { children: c.title }), _jsx("small", { children: added
                                        ? t("On this journey")
                                        : count("messages", c.messageCount) })] })] }, c.id));
            }), _jsx("button", { type: "button", className: "primary", disabled: !chosen.length, onClick: () => {
                    addMany(chosen);
                    setPicked({});
                }, children: t("Add selected conversations") }), results.more && (_jsx("button", { disabled: results.loading, onClick: results.more, children: t("Load more") }))] }));
}
