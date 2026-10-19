import {
  AnnotationContent,
  AnnotationTopics,
  AnnotationTools,
  TopicFilter,
} from "../review/AnnotationContent";
import { threadTags } from "../review/annotationParsing";
import { AnnotationArtifacts } from "../review/AnnotationArtifacts";
import { t, useLocale, count, formatDate } from "../../shared/i18n/i18n";
import { useState, useEffect, useCallback } from "react";
import {
  ReactFlow,
  Background,
  MiniMap,
  Controls,
  Handle,
  Position,
  addEdge,
  applyNodeChanges,
  applyEdgeChanges,
  type Node,
  type Edge,
  type Connection,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { useEditor, EditorContent } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import {
  ArrowLeft,
  Plus,
  Save,
  Undo2,
  Redo2,
  Share2,
  Play,
  LayoutGrid,
  Search,
  X,
  ExternalLink,
  GitBranch,
  Flag,
  Lightbulb,
  FlaskConical,
  FileText,
  Folder,
  Paperclip,
  TriangleAlert,
  MessageSquare,
  Wrench,
} from "lucide-react";
import {
  api,
  act,
  go,
  graph,
  useApp,
  providerClass,
  useLibrary,
} from "../../shared/state";
import { Badge } from "../../shared/ui/ui";
const icons: Record<string, any> = {
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
function TimelineDetails({ data }: { data: any }) {
  return (
    <div className="timeline-meta">
      <div className="lane-label">
        {t("Lane: {name}", { name: data.laneLabel })}
      </div>
      <div>
        {data.timelineStart
          ? formatDate(data.timelineStart, {
              dateStyle: "short",
              timeStyle: "short",
            })
          : t("Time unknown")}
      </div>
      {data.timelineEnd && data.timelineEnd !== data.timelineStart && (
        <div>
          {t("Until {time}", {
            time: formatDate(data.timelineEnd, {
              dateStyle: "short",
              timeStyle: "short",
            }),
          })}
        </div>
      )}
    </div>
  );
}
function JourneyNode({ data, selected }: { data: any; selected: boolean }) {
  useLocale();
  const Icon = data.recordedExecution ? Wrench : icons[data.kind] || FileText;
  return (
    <div
      className={
        "journey-node " +
        (data.timeline || data.timelineLaneHeader ? "timeline-node " : "") +
        (selected ? "selected " : "") +
        data.kind
      }
    >
      <Handle
        id="in"
        type="target"
        position={Position.Left}
        isConnectable
        aria-label={t("Connection point")}
      />
      <div className="node-kind">
        <Icon size={13} />
        <span>
          {data.recordedExecution
            ? t("Recorded tool execution")
            : data.kind === "conversation"
              ? data.provider
              : t(data.kind)}
        </span>
        {data.kind === "conversation" && (
          <span className={"node-provider " + providerClass(data.provider)}>
            ●
          </span>
        )}
      </div>
      <strong>{data.title}</strong>
      <div className="node-meta">
        {data.kind === "conversation"
          ? `${count("messages", data.messageCount ?? 0)} · ${data.model || t("Imported source")}`
          : t(data.status || "Curator commentary")}
      </div>
      {data.timeline && <TimelineDetails data={data} />}
      <Handle
        id="out"
        type="source"
        position={Position.Right}
        isConnectable
        aria-label={t("Connection point")}
      />
    </div>
  );
}
const nodeTypes = { journey: JourneyNode };
export function RichEditor({
  value,
  onChange,
  disabled = false,
}: {
  value: string;
  onChange: (v: string) => void;
  disabled?: boolean;
}) {
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
  return (
    <div className="rich-editor">
      <div className="row">
        <button
          disabled={disabled}
          onClick={() => editor?.chain().focus().toggleBold().run()}
        >
          <b aria-label={t("Bold")}>B</b>
        </button>
        <button
          disabled={disabled}
          onClick={() => editor?.chain().focus().toggleItalic().run()}
        >
          <i aria-label={t("Italic")}>I</i>
        </button>
        <button
          disabled={disabled}
          onClick={() => editor?.chain().focus().toggleBulletList().run()}
        >
          {t("• List")}
        </button>
        <small>{t("Curator-written content")}</small>
      </div>
      <EditorContent editor={editor} />
    </div>
  );
}
export function JourneyCanvas({
  journey,
  canEdit,
  canPublish,
  onPublish,
  refresh,
  user,
  members,
  online,
}: {
  journey: any;
  canEdit: boolean;
  canPublish: boolean;
  onPublish: () => void;
  refresh: () => void;
  user: any;
  members: any[];
  online: string[];
}) {
  useLocale();
  const initial = graph(journey);
  const [nodes, setNodes] = useState<Node[]>(initial.nodes);
  const [edges, setEdges] = useState<Edge[]>(initial.edges);
  const [steps, setSteps] = useState<string[]>(initial.steps || []);
  const [selected, setSelected] = useState<string | null>(null);
  const [edgeId, setEdgeId] = useState<string | null>(null);
  const [panel, setPanel] = useState(false);
  const [query, setQuery] = useState("");
  const [past, setPast] = useState<any[]>([]);
  const [future, setFuture] = useState<any[]>([]);
  const [version, setVersion] = useState(journey.version);
  const [dirty, setDirty] = useState(false);
  const [conversation, setConversation] = useState<any>(null);
  const [title, setTitle] = useState(journey.title);
  const [visibility, setVisibility] = useState(journey.visibility);
  const [guided, setGuided] = useState(false);
  const [step, setStep] = useState(0);
  const [collapsed, setCollapsed] = useState<string[]>(initial.collapsed || []);
  const [flow, setFlow] = useState<any>(null);
  const [comments, setComments] = useState<any[]>([]);
  const [body, setBody] = useState("");
  const [topic, setTopic] = useState("");
  const { theme, workspace, notify } = useApp();
  const [find, setFind] = useState("");
  const library = useLibrary(panel ? workspace : "", { q: find, limit: 30 });
  const node = nodes.find((n) => n.id === selected);
  const edge = edges.find((e) => e.id === edgeId);
  useEffect(() => {
    if (journey.version === version) return;
    if (dirty) {
      notify(
        t(
          "A collaborator saved a newer version. Save will detect the conflict; reload to use their version.",
        ),
      );
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
    const fn = (e: BeforeUnloadEvent) => {
      if (dirty) e.preventDefault();
    };
    window.addEventListener("beforeunload", fn);
    return () => window.removeEventListener("beforeunload", fn);
  }, [dirty]);
  useEffect(() => {
    setConversation(null);
    if (node?.data.conversationId)
      act(async () =>
        setConversation(
          await api("/conversations/" + node.data.conversationId + "?limit=3"),
        ),
      );
    if (selected)
      act(async () =>
        setComments(
          await api(
            "/w/" +
              workspace +
              "/annotations?journeyId=" +
              journey.id +
              "&nodeId=" +
              encodeURIComponent(selected),
          ),
        ),
      );
    else setComments([]);
  }, [selected]);
  const update = (patch: any) => {
    checkpoint();
    setNodes((ns) =>
      ns.map((n) =>
        n.id === selected
          ? {
              ...n,
              data: {
                ...n.data,
                ...patch,
                ...(patch.groupId !== undefined && n.data.timeline
                  ? {
                      laneLabel:
                        ns.find((g) => g.id === patch.groupId)?.data.title ||
                        t("No group"),
                    }
                  : {}),
              },
            }
          : node?.data.timelineLaneHeader &&
              patch.title !== undefined &&
              n.data.groupId === selected
            ? { ...n, data: { ...n.data, laneLabel: patch.title } }
            : n,
      ),
    );
  };
  const add = (kind: string, c?: any) => {
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
  const addConversations = (items: any[]) => {
    const present = new Set(
      nodes
        .map((n) => n.data.conversationId)
        .filter((id): id is string => typeof id === "string" && !!id),
    );
    const fresh = items.filter((c) => c?.id && !present.has(c.id));
    if (!fresh.length) return;
    checkpoint();
    const created = fresh.map((c, index) => ({
      id: crypto.randomUUID(),
      type: "journey" as const,
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
  const save = () =>
    act(async () => {
      const result = await api(
        "/journeys/" + journey.id,
        {
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
        },
        "PUT",
      );
      setVersion(result.version);
      setDirty(false);
      refresh();
      notify(t("Journey saved. Original conversations are unchanged."));
    });
  const undo = (redo = false) => {
    const list = redo ? future : past;
    if (!list.length) return;
    const snapshot = list[list.length - 1];
    if (redo) {
      setFuture(list.slice(0, -1));
      setPast([...past, { nodes, edges, steps }]);
    } else {
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
        ...new Set(
          nodes
            .filter((n) => n.data.timelineStart)
            .map((n) => String(n.data.timelineStart)),
        ),
      ].sort();
      let extra = 0;
      setNodes((ns) =>
        ns.map((n) => {
          const header = laneIds.indexOf(n.id);
          if (header >= 0) return { ...n, position: { x: 0, y: header * 210 } };
          const lane = laneIds.indexOf(String(n.data.groupId));
          if (n.data.timeline && lane >= 0)
            return {
              ...n,
              position: {
                x:
                  340 +
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
        }),
      );
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
      if (!changed) break;
    }
    const counts = new Map<number, number>();
    setNodes((ns) =>
      ns.map((n) => {
        const r = ranks.get(n.id) || 0;
        const y = counts.get(r) || 0;
        counts.set(r, y + 1);
        return { ...n, position: { x: r * 315, y: y * 190 } };
      }),
    );
  };
  const visible = nodes.map((n) => ({
    ...n,
    hidden:
      !!(n.data.groupId && collapsed.includes(String(n.data.groupId))) ||
      (!!query &&
        !String(n.data.title).toLowerCase().includes(query.toLowerCase())),
    style: { opacity: guided && steps[step] !== n.id?.toString() ? 0.35 : 1 },
  }));
  return (
    <div className="canvas-page">
      <div className="canvas-title">
        <button title={t("Back to journeys")} onClick={() => go("journeys")}>
          <ArrowLeft size={19} />
        </button>
        <div>
          <input
            className="title-input"
            aria-label={t("Journey title")}
            value={title}
            disabled={!canEdit}
            onChange={(e) => {
              setTitle(e.target.value);
              setDirty(true);
            }}
          />
          <small>
            <span className="dot" />{" "}
            {dirty ? t("Unsaved changes") : t("Saved to workspace")}{" "}
            <span>·</span> {count("nodes", nodes.length)} <span>·</span>{" "}
            {count("connections", edges.length)}
          </small>
        </div>
        <span className="spacer" />
        <div className="avatar-stack">
          {members
            .filter((m) => online.includes(m.id))
            .slice(0, 3)
            .map((m) => (
              <span
                className="avatar small"
                key={m.id}
                title={m.name + " · viewing now"}
              >
                {m.name[0]}
              </span>
            ))}
        </div>
        <button
          onClick={() => {
            setGuided(!guided);
            setStep(0);
          }}
        >
          <Play size={15} /> {t("Guided view")}
        </button>
        {canPublish && (
          <button
            onClick={() => {
              if (dirty) notify(t("Save your changes before publishing."));
              else onPublish();
            }}
          >
            <Share2 size={15} /> {t("Publish")}
          </button>
        )}
        <button
          className="primary"
          disabled={!canEdit || !dirty}
          onClick={save}
        >
          <Save size={15} />
          {t("Save journey")}
        </button>
      </div>
      <div className="canvas-toolbar">
        <button
          className="primary subtle"
          disabled={!canEdit}
          onClick={() => setPanel(!panel)}
        >
          <Plus size={16} /> {t("Add to journey")}
        </button>
        <div className="divider" />
        <button
          title={t("Undo")}
          disabled={!past.length || !canEdit}
          onClick={() => undo()}
        >
          <Undo2 size={16} />
        </button>
        <button
          title={t("Redo")}
          disabled={!future.length || !canEdit}
          onClick={() => undo(true)}
        >
          <Redo2 size={16} />
        </button>
        <button disabled={!canEdit} onClick={layout}>
          <LayoutGrid size={16} />
          {t("Auto layout")}
        </button>
        <span className="spacer" />
        <label className="search compact">
          <Search size={15} />
          <input
            placeholder={t("Find a node…")}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </label>
        <select
          aria-label={t("Journey visibility")}
          disabled={!canEdit}
          value={visibility}
          onChange={(e) => {
            setVisibility(e.target.value);
            setDirty(true);
          }}
        >
          <option value="Private">{t("Private")}</option>
          <option value="Team">{t("Team")}</option>
        </select>
      </div>
      {initial.timeline && (
        <div className="timeline-notice">
          <strong>{t("Generated timeline draft")}</strong>
          {" · "}
          {t(
            "Left to right: source time. Spacing does not represent duration. Lanes separate threads and tool executions.",
          )}
          {nodes.some((n) => n.data.timeline && !n.data.timelineStart) && (
            <div>
              {t(
                "Some activity has no recorded start time and is placed after dated activity.",
              )}
            </div>
          )}
          {initial.timeline.omittedRelationshipCount > 0 && (
            <div>
              {t(
                "{count} relationships could not be included because their targets were unselected or unresolved.",
                { count: initial.timeline.omittedRelationshipCount },
              )}
            </div>
          )}
        </div>
      )}
      <div className="canvas-body">
        <ReactFlow<Node, Edge>
          ariaLabelConfig={{
            "controls.zoomIn.ariaLabel": t("Zoom in"),
            "controls.zoomOut.ariaLabel": t("Zoom out"),
            "controls.fitView.ariaLabel": t("Fit view"),
            "controls.interactive.ariaLabel": t("Toggle interactivity"),
          }}
          onInit={setFlow}
          defaultViewport={initial.viewport}
          onMoveEnd={(event) => {
            if (event && canEdit) setDirty(true);
          }}
          nodes={visible}
          edges={edges}
          nodeTypes={nodeTypes}
          colorMode={theme as "light" | "dark"}
          onNodesChange={(changes) => {
            if (canEdit) {
              if (changes.some((c) => c.type === "remove")) checkpoint();
              setNodes((ns) => applyNodeChanges(changes, ns));
              if (
                changes.some(
                  (c) => c.type !== "select" && c.type !== "dimensions",
                )
              )
                setDirty(true);
            }
          }}
          onEdgesChange={(changes) => {
            if (canEdit) {
              setEdges((es) => applyEdgeChanges(changes, es));
              if (changes.some((c) => c.type !== "select")) setDirty(true);
            }
          }}
          onNodeDragStart={checkpoint}
          onConnect={(c: Connection) => {
            if (!canEdit || !c.source || !c.target || c.source === c.target)
              return;
            checkpoint();
            setEdges((es) =>
              addEdge(
                {
                  ...c,
                  label: "Continues from",
                  type: "smoothstep",
                  markerEnd: { type: "arrowclosed" },
                },
                es,
              ),
            );
            setDirty(true);
          }}
          connectionRadius={40}
          connectionLineStyle={{ stroke: "#4369bc", strokeWidth: 2 }}
          onNodeClick={(_, n) => {
            setSelected(n.id);
            setEdgeId(null);
          }}
          onNodeDoubleClick={(_, n) => {
            if (n.data.conversationId) go("explorer/" + n.data.conversationId);
            else if (n.data.kind === "group")
              setCollapsed((s) =>
                s.includes(n.id) ? s.filter((i) => i !== n.id) : [...s, n.id],
              );
          }}
          onEdgeClick={(_, e) => {
            setEdgeId(e.id);
            setSelected(null);
          }}
          nodesDraggable={canEdit}
          nodesConnectable={canEdit}
          deleteKeyCode={canEdit ? ["Backspace", "Delete"] : null}
          fitView={!initial.viewport}
          minZoom={0.15}
          maxZoom={2}
          defaultEdgeOptions={{
            type: "smoothstep",
            style: { stroke: "#96a5b8", strokeWidth: 1.4 },
            labelStyle: { fontSize: 10, fill: "#6c7789" },
            labelBgPadding: [6, 4],
            labelBgBorderRadius: 4,
          }}
        >
          <Background
            gap={22}
            size={1}
            color={theme === "light" ? "#d6dde5" : "#334155"}
          />
          <MiniMap
            pannable
            zoomable
            nodeColor={(n) =>
              n.data.kind === "decision"
                ? "#d7a64b"
                : n.data.kind === "milestone"
                  ? "#60a892"
                  : "#c0cbdc"
            }
          />
          <Controls />
        </ReactFlow>
        <div className="canvas-legend">
          <span>
            <i className="legend-source" />
            {t("Imported source")}
          </span>
          <span>
            <i className="legend-curation" />
            {t("Curator perspective")}
          </span>
          <small>{t("Click to inspect · Double-click to explore")}</small>
        </div>
        {panel && (
          <aside className="node-library">
            <div className="section-title">
              <h3>{t("Add to your journey")}</h3>
              <button
                title={t("Close node library")}
                onClick={() => setPanel(false)}
              >
                <X size={16} />
              </button>
            </div>
            <span className="eyebrow">{t("CURATOR CONTENT")}</span>
            <div className="node-types">
              {Object.entries(icons)
                .filter(([k]) => k !== "conversation")
                .map(([k, Icon]) => (
                  <button disabled={!canEdit} key={t(k)} onClick={() => add(k)}>
                    <Icon size={16} />
                    {k}
                  </button>
                ))}
            </div>
            <LibraryPicker
              presentIds={nodes.flatMap((n) =>
                typeof n.data.conversationId === "string"
                  ? [n.data.conversationId]
                  : [],
              )}
              addMany={addConversations}
              show={(conversationId) => {
                const existing = nodes.find(
                  (n) => n.data.conversationId === conversationId,
                );
                if (!existing) return;
                setSelected(existing.id);
                setEdgeId(null);
                flow?.fitView({
                  nodes: [{ id: existing.id }],
                  duration: 200,
                  padding: 0.4,
                });
              }}
            />
          </aside>
        )}
        {(node || edge) && (
          <aside className="inspector">
            <div className="section-title">
              <span className="eyebrow">
                {node ? t("NODE INSPECTOR") : t("CONNECTION")}
              </span>
              <button
                title={t("Close inspector")}
                onClick={() => {
                  setSelected(null);
                  setEdgeId(null);
                }}
              >
                <X size={17} />
              </button>
            </div>
            {edge ? (
              <>
                <h3>
                  {t(
                    edge.data?.recorded
                      ? "Recorded relationship"
                      : "Curator-defined relationship",
                  )}
                </h3>
                <select
                  disabled={!canEdit}
                  value={String(edge.label)}
                  onChange={(e) => {
                    checkpoint();
                    setEdges((es) =>
                      es.map((x) =>
                        x.id === edge.id
                          ? {
                              ...x,
                              label: e.target.value,
                              data: { ...x.data, recorded: false },
                            }
                          : x,
                      ),
                    );
                  }}
                >
                  {[...new Set([...relations, String(edge.label)])].map((r) => (
                    <option key={r} value={r}>
                      {t(r)}
                    </option>
                  ))}
                </select>
                <p>
                  {t(
                    edge.data?.recorded
                      ? "This connection comes from an imported source relationship or tool execution. You can edit or remove it in this draft."
                      : "This connection explains your interpretation. It does not change the original message tree.",
                  )}
                </p>
                <button
                  disabled={!canEdit}
                  onClick={() => {
                    checkpoint();
                    setEdges((es) => es.filter((e) => e.id !== edge.id));
                    setEdgeId(null);
                  }}
                >
                  {t("Disconnect nodes")}
                </button>
              </>
            ) : (
              node && (
                <>
                  <span className={"type-tag " + node.data.kind}>
                    {t(String(node.data.kind))}
                  </span>
                  <label>
                    {t("Title")}
                    <input
                      disabled={!canEdit}
                      value={String(node.data.title)}
                      onChange={(e) => update({ title: e.target.value })}
                    />
                  </label>
                  {node.data.timeline && <TimelineDetails data={node.data} />}
                  {node.data.recordedExecution && (
                    <div className="provenance">
                      <p>
                        {t("Recorded tool execution")} ·{" "}
                        {String(node.data.provider)} ·{" "}
                        {String(node.data.model || t("Model not provided"))}
                      </p>
                      <p>{t(String(node.data.status))}</p>
                      <button
                        onClick={() =>
                          go("explorer/" + node.data.sourceConversationId)
                        }
                      >
                        {t("Open source conversation")}
                      </button>
                    </div>
                  )}
                  {node.data.kind === "conversation" ? (
                    <>
                      <div className="provenance">
                        <Badge provider={String(node.data.provider)} />
                        <span>{t("Immutable source")}</span>
                      </div>
                      <p>
                        {String(node.data.model || "Model not available")} ·{" "}
                        {count("messages", Number(node.data.messageCount || 0))}
                      </p>
                      <button
                        className="primary full"
                        onClick={() =>
                          go("explorer/" + node.data.conversationId)
                        }
                      >
                        {t("Open conversation")} <ExternalLink size={15} />
                      </button>
                      <div className="source-excerpt">
                        {conversation?.messages.slice(0, 3).map((m: any) => (
                          <div key={m.id}>
                            <small>
                              {t("ORIGINAL")} {m.role.toUpperCase()}
                            </small>
                            <p>
                              {m.blocks
                                .map((b: any) => b.text)
                                .join("\n")
                                .slice(0, 350)}
                            </p>
                          </div>
                        ))}
                      </div>
                    </>
                  ) : (
                    <>
                      <label>{t("Curator explanation")}</label>
                      <RichEditor
                        key={node.id}
                        value={String(node.data.body || "")}
                        onChange={(body) => update({ body })}
                        disabled={!canEdit}
                      />
                      {node.data.kind === "experiment" && (
                        <select
                          value={String(node.data.status || t("Inconclusive"))}
                          onChange={(e) => update({ status: e.target.value })}
                        >
                          <option value="Inconclusive">
                            {t("Inconclusive")}
                          </option>
                          <option value="Success">{t("Success")}</option>
                          <option value="Failure">{t("Failure")}</option>
                        </select>
                      )}
                      {node.data.kind === "artifact" && (
                        <label>
                          {t("File, commit, or document reference")}
                          <input
                            value={String(node.data.reference || "")}
                            disabled={!canEdit}
                            onChange={(e) =>
                              update({ reference: e.target.value })
                            }
                          />
                        </label>
                      )}
                    </>
                  )}
                  <label>
                    {t("Group")}
                    <select
                      disabled={!canEdit}
                      value={String(node.data.groupId || "")}
                      onChange={(e) => update({ groupId: e.target.value })}
                    >
                      <option value="">{t("No group")}</option>
                      {nodes
                        .filter(
                          (n) => n.data.kind === "group" && n.id !== node.id,
                        )
                        .map((n) => (
                          <option key={n.id} value={n.id}>
                            {String(n.data.title)}
                          </option>
                        ))}
                    </select>
                  </label>
                  {node.data.kind === "group" && (
                    <button
                      onClick={() =>
                        setCollapsed((s) =>
                          s.includes(node.id)
                            ? s.filter((i) => i !== node.id)
                            : [...s, node.id],
                        )
                      }
                    >
                      {t("Collapse / expand group")}
                    </button>
                  )}
                  <label>
                    {t("Reading order")}
                    <input
                      type="number"
                      min={1}
                      max={nodes.length}
                      value={steps.indexOf(node.id) + 1}
                      disabled={!canEdit}
                      onChange={(e) => {
                        checkpoint();
                        const next = steps.filter((i) => i !== node.id);
                        next.splice(
                          Math.max(0, Number(e.target.value) - 1),
                          0,
                          node.id,
                        );
                        setSteps(next);
                      }}
                    />
                  </label>
                  <h3>{t("Node discussion")}</h3>
                  <TopicFilter
                    annotations={comments}
                    value={topic}
                    onChange={setTopic}
                  />
                  {comments
                    .filter((a) => !topic || threadTags(a).includes(topic))
                    .map((a) => (
                      <div className="thread" key={a.id}>
                        <AnnotationTopics annotation={a} />
                        {canEdit && threadTags(a).includes("action") && (
                          <button
                            type="button"
                            onClick={() =>
                              act(async () => {
                                await api(
                                  "/annotations/" + a.id,
                                  { resolved: !a.resolved },
                                  "PATCH",
                                );
                                setComments((current) =>
                                  current.map((thread) =>
                                    thread.id === a.id
                                      ? { ...thread, resolved: !a.resolved }
                                      : thread,
                                  ),
                                );
                              })
                            }
                          >
                            {t(a.resolved ? "Reopen" : "Complete action")}
                          </button>
                        )}
                        <AnnotationArtifacts
                          annotation={a}
                          canUpload={canEdit}
                          onUploaded={(file) =>
                            setComments((current) =>
                              current.map((thread) =>
                                thread.id === a.id
                                  ? {
                                      ...thread,
                                      artifacts: [
                                        ...(thread.artifacts || []),
                                        file,
                                      ],
                                    }
                                  : thread,
                              ),
                            )
                          }
                        />
                        {a.comments.map((c: any) => (
                          <div key={c.id}>
                            {members.find((m) => m.id === c.authorId)?.name}:{" "}
                            <AnnotationContent body={c.body} />
                          </div>
                        ))}
                      </div>
                    ))}
                  <form
                    onSubmit={(e) => {
                      e.preventDefault();
                      act(async () => {
                        const a = await api(
                          "/w/" + workspace + "/annotations",
                          { journeyId: journey.id, nodeId: node.id, body },
                        );
                        setComments([...comments, a]);
                        setBody("");
                      });
                    }}
                  >
                    <textarea
                      placeholder={t("Add curator discussion…")}
                      value={body}
                      onChange={(e) => setBody(e.target.value)}
                    />
                    <AnnotationTools body={body} setBody={setBody} />
                    <button disabled={!body.trim()}>{t("Add comment")}</button>
                  </form>
                  <button
                    className="danger"
                    disabled={!canEdit}
                    onClick={() => {
                      checkpoint();
                      setNodes((ns) => ns.filter((n) => n.id !== node.id));
                      setEdges((es) =>
                        es.filter(
                          (e) => e.source !== node.id && e.target !== node.id,
                        ),
                      );
                      setSelected(null);
                    }}
                  >
                    {t("Remove from journey")}
                  </button>
                </>
              )
            )}
          </aside>
        )}
        {guided && (
          <div className="guided-bar">
            <button
              disabled={step === 0}
              onClick={() => {
                setStep((s) => s - 1);
                setSelected(steps[step - 1]);
              }}
            >
              {t("← Previous")}
            </button>
            <div>
              <small>
                {t("Guided step", { current: step + 1, total: steps.length })}
              </small>
              <strong>
                {String(
                  nodes.find((n) => n.id === steps[step])?.data.title ||
                    "Add a node to begin",
                )}
              </strong>
            </div>
            <button
              disabled={step >= steps.length - 1}
              onClick={() => {
                setStep((s) => s + 1);
                setSelected(steps[step + 1]);
              }}
            >
              {t("Next →")}
            </button>
            <button
              title={t("Exit guided view")}
              onClick={() => setGuided(false)}
            >
              <X size={16} />
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

function LibraryPicker({
  presentIds,
  addMany,
  show,
}: {
  presentIds: string[];
  addMany: (items: any[]) => void;
  show: (conversationId: string) => void;
}) {
  const [q, setQ] = useState("");
  const [picked, setPicked] = useState<Record<string, any>>({});
  const { workspace } = useApp();
  const results = useLibrary(workspace, { q, limit: 20 });
  const present = new Set(presentIds);
  const chosen = Object.values(picked).filter((c) => !present.has(c.id));
  return (
    <>
      <span className="eyebrow">{t("FROM YOUR LIBRARY")}</span>
      <label className="search">
        <Search size={15} />
        <input
          placeholder={t("Search conversations, messages, code, comments…")}
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
      </label>
      {results.items.map((c) => {
        const added = present.has(c.id);
        const selected = added || !!picked[c.id];
        return (
          <button
            type="button"
            className={
              "library-option" +
              (added ? " added" : "") +
              (selected && !added ? " picked" : "")
            }
            key={c.id}
            aria-pressed={selected}
            onClick={() => {
              if (added) {
                show(c.id);
                return;
              }
              setPicked((current) => {
                const next = { ...current };
                if (next[c.id]) delete next[c.id];
                else next[c.id] = c;
                return next;
              });
            }}
          >
            <span className="library-check" aria-hidden="true">
              {selected ? "✓" : ""}
            </span>
            <span>
              <Badge provider={c.provider} />
              <strong>{c.title}</strong>
              <small>
                {added
                  ? t("On this journey")
                  : count("messages", c.messageCount)}
              </small>
            </span>
          </button>
        );
      })}
      <button
        type="button"
        className="primary"
        disabled={!chosen.length}
        onClick={() => {
          addMany(chosen);
          setPicked({});
        }}
      >
        {t("Add selected conversations")}
      </button>
      {results.more && (
        <button disabled={results.loading} onClick={results.more}>
          {t("Load more")}
        </button>
      )}
    </>
  );
}
