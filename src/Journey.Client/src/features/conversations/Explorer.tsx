import {
  AnnotationContent,
  AnnotationTopics,
  AnnotationTools,
  TopicFilter,
} from "../review/AnnotationContent";
import { threadTags } from "../review/annotationParsing";
import { SourceHistory } from "../imports/SourceHistory";
import { SuppliedFile, type SuppliedFileRecord } from "../imports/SuppliedFile";
import { AnnotationArtifacts } from "../review/AnnotationArtifacts";
import {
  t,
  useLocale,
  formatNumber,
  formatDate,
  count,
} from "../../shared/i18n/i18n";
import { PublishDialog } from "../publications/Publish";
import { useState, useEffect, useRef } from "react";
import ReactMarkdown, { defaultUrlTransform } from "react-markdown";
import remarkGfm from "remark-gfm";
import rehypeHighlight from "rehype-highlight";
import {
  Search,
  MessageSquare,
  Bookmark,
  GitBranch,
  ArrowLeft,
  Columns2,
  Check,
  MoreHorizontal,
  ChevronDown,
  Link2,
  X,
  Download,
  Eye,
} from "lucide-react";
import {
  api,
  act,
  go,
  useApp,
  initials,
  loadConversation,
} from "../../shared/state";
import { Badge } from "../../shared/ui/ui";
import { MessageRole } from "./MessageRole";
import { ToolCall } from "./ToolCall";
import { SourceActivity } from "./SourceActivity";
import { EmbeddedImage, ImageSource } from "./EmbeddedImage";
import { embeddedImages, embeddedMarkdown } from "./embeddedImages";
import type { GitHubSource } from "./githubSource";
import {
  blockHasContent,
  describeActivity,
  describeTool,
  groupSourceBlocks,
  messageHasContent,
  pointerView,
  sourceText,
  type SourceBlock,
} from "./messagePresentation";
import { Wrench } from "lucide-react";
import "./messages.css";
const markdownLimit = 48_000;
const readerPage = 40;
export function Markdown({ text }: { text: string }) {
  useLocale();
  const { markdown, images } = embeddedMarkdown(text);
  if (markdown.length > markdownLimit)
    return (
      <>
        <p className="source-notice">
          {t("Large source kept as text so the page stays responsive.")}
        </p>
        <pre className="source-text">{text}</pre>
      </>
    );
  return (
    <>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeHighlight]}
        urlTransform={(url) => images.has(url) ? url : defaultUrlTransform(url)}
        components={{
          img: ({ src, alt }) => src && images.has(src) ? (
            <EmbeddedImage key={images.get(src)!} src={images.get(src)!} alt={alt} />
          ) : (
            <span className="attachment">
              {t("Image reference:")} {alt || "attachment"}{" "}
              {t("· remote loading disabled")}
            </span>
          ),
          a: ({ href, children }) => (
            <a href={href} target="_blank" rel="noreferrer">
              {children}
            </a>
          ),
        }}
      >
        {markdown}
      </ReactMarkdown>
      {images.size > 0 && <ImageSource payload={text} />}
    </>
  );
}
async function fingerprint(text: string) {
  return Array.from(
    new Uint8Array(
      await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text)),
    ),
  )
    .map((v) => v.toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase();
}
function textRange(el: HTMLElement, start: number, end: number) {
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
export function Message({
  message,
  annotations = [],
  plain = false,
  original = false,
  onSelect,
  onThread,
  onPin,
  pinned = false,
  onLink,
  github,
  conversationId,
  attachments = [],
  filesReady = false,
}: {
  message: any;
  annotations?: any[];
  plain?: boolean;
  original?: boolean;
  onSelect?: (a: any) => void;
  onThread?: (id: string) => void;
  onPin?: () => void;
  pinned?: boolean;
  onLink?: () => void;
  github?: GitHubSource | null;
  conversationId?: string;
  attachments?: SuppliedFileRecord[];
  filesReady?: boolean;
}) {
  useLocale();
  const refs = useRef<Record<string, HTMLElement | null>>({});
  const ranges = useRef<{ range: Range; id: string }[]>([]);
  useEffect(() => {
    ranges.current = [];
    if (original) return;
    for (const block of message.blocks) {
      const el = refs.current[block.id];
      if (!el) continue;
      const text = el.textContent || "";
      for (const a of annotations.filter((a) => !a.orphaned)) {
        const anchor = JSON.parse(a.anchor);
        if (anchor.blockId !== block.id || !anchor.quote) continue;
        const exact =
          plain && text.slice(anchor.start, anchor.end) === anchor.quote;
        const start = exact ? anchor.start : text.indexOf(anchor.quote);
        if (start < 0 || (!exact && text.indexOf(anchor.quote, start + 1) >= 0))
          continue;
        const range = textRange(el, start, start + anchor.quote.length);
        if (range) ranges.current.push({ range, id: a.id });
      }
    }
    const registry = (CSS as any).highlights;
    if (registry && (window as any).Highlight) {
      registry.set(
        "annotation-" + message.id,
        new (window as any).Highlight(...ranges.current.map((x) => x.range)),
      );
      const style = document.createElement("style");
      style.textContent = `::highlight(annotation-${message.id}) { background: #f6d77777; color: inherit; }`;
      document.head.append(style);
      return () => {
        registry.delete("annotation-" + message.id);
        style.remove();
      };
    }
  }, [annotations, plain, original, message]);
  const select = async (block: any, e: React.MouseEvent) => {
    if (!onSelect) return;
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed) return;
    const el = refs.current[block.id];
    if (
      !el ||
      !el.contains(selection.anchorNode) ||
      !el.contains(selection.focusNode)
    )
      return;
    const quote = selection.toString();
    let start = block.text.indexOf(quote);
    if (plain) {
      const range = selection.getRangeAt(0);
      const pre = range.cloneRange();
      pre.selectNodeContents(el);
      pre.setEnd(range.startContainer, range.startOffset);
      start = pre.toString().length;
    } else if (start < 0 || block.text.indexOf(quote, start + 1) >= 0) {
      useApp
        .getState()
        .notify(t("Switch to Source text to anchor this selection precisely."));
      return;
    }
    if (start < 0) return;
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
  const renderBlock = (b: SourceBlock) => {
    const images = embeddedImages(b);
    if (images.length) {
      return (
        <div
          key={b.id}
          className="message-content embedded-image-block"
          onMouseUp={(e) => select(b, e)}
        >
          {images.map((src) => <EmbeddedImage key={src} src={src} />)}
          <ImageSource
            payload={sourceText(b)}
            sourceRef={(el) => { refs.current[b.id] = el; }}
          />
        </div>
      );
    }
    const media = pointerView(b);
    const files = attachments.filter((file) => file.blockId === b.id);
    if (media) {
      const ready = files.filter((file) => file.availability === "available");
      return (
        <div key={b.id}>
          {ready.map((file) => (
            <SuppliedFile
              key={file.id}
              file={{
                ...file,
                view: file.view && file.view !== "download" ? file.view : media,
              }}
            />
          ))}
          {filesReady && ready.length === 0 && (
            <p className="supplied-file-missing">
              {t("This file was not included in the import.")}
            </p>
          )}
        </div>
      );
    }
    const isText = b.kind === "text" || b.kind === "markdown";
    const tool = !isText && describeTool(b);
    return (
      <div key={b.id}>
      <div
        ref={
          isText
            ? (el) => {
                refs.current[b.id] = el;
              }
            : undefined
        }
        className={"message-content" + (isText ? "" : " tool-call-block")}
        onMouseUp={(e) => select(b, e)}
        onClick={(e) => {
          if (window.getSelection()?.isCollapsed)
            for (const r of ranges.current)
              for (const rect of r.range.getClientRects())
                if (
                  e.clientX >= rect.left &&
                  e.clientX <= rect.right &&
                  e.clientY >= rect.top &&
                  e.clientY <= rect.bottom
                )
                  onThread?.(r.id);
        }}
      >
        {isText ? (
          plain ? (
            <pre className="source-text">{b.text}</pre>
          ) : (
            <Markdown text={b.text} />
          )
        ) : tool && (tool.isCall || tool.isResult) ? (
          <ToolCall
            block={b}
            github={github}
            conversationId={conversationId}
            plain={plain}
            sourceRef={(el) => {
              refs.current[b.id] = el;
            }}
          />
        ) : (
          <SourceActivity
            block={b}
            plain={plain}
            sourceRef={(el) => {
              refs.current[b.id] = el;
            }}
          />
        )}
      </div>
        {files.map((file) => (
          <SuppliedFile key={file.id} file={file} />
        ))}
      </div>
    );
  };
  const contentBlocks = (message.blocks || []).filter(blockHasContent);
  if (!contentBlocks.length && (original || annotations.length === 0))
    return null;
  return (
    <article
      className={
        "message message-card " + message.role + (pinned ? " pinned" : "")
      }
      data-message-role={message.role}
      id={"message-" + message.id}
    >
      <header>
        <MessageRole role={message.role} />
        {(message.role === "user" || message.role === "assistant") && (
          <span className="message-source-label">
            {message.role === "user"
              ? t("Original prompt")
              : message.role === "assistant"
                ? t("Original AI response")
                : t(message.role)}
          </span>
        )}
        {message.model && <span className="model-badge">{message.model}</span>}
        <span className="spacer" />
        {message.timestamp && (
          <time>
            {formatDate(message.timestamp, {
              month: "short",
              day: "numeric",
              year: "numeric",
              hour: "2-digit",
              minute: "2-digit",
            })}
          </time>
        )}
        {onPin && (
          <button
            title={t("Bookmark message")}
            className={pinned ? "active" : ""}
            onClick={onPin}
          >
            <Bookmark size={14} />
          </button>
        )}
        {onLink && (
          <button title={t("Use this message as evidence")} onClick={onLink}>
            <Link2 size={14} /> {t("Use as evidence")}
          </button>
        )}
        {onSelect && (
          <button
            title={t("Comment on whole message")}
            onClick={() => onSelect({ messageId: message.id, whole: true })}
          >
            <MessageSquare size={14} />
          </button>
        )}
      </header>
      {contentBlocks.length > 0 && (
      <details open>
        <summary>{t("Message content")}</summary>
        {groupSourceBlocks(contentBlocks).map((group) =>
          group.tools ? (
            <section
              className="tool-call-group"
              aria-label={t("Tool calls")}
              key={group.blocks[0].id}
            >
              <header>
                <Wrench size={14} aria-hidden="true" />
                <span>{t("Tool calls")}</span>
                <span className="tool-call-count">
                  {formatNumber(group.blocks.length)}
                </span>
              </header>
              {group.blocks.map(renderBlock)}
            </section>
          ) : (
            renderBlock(group.blocks[0])
          ),
        )}
      </details>
      )}
      {!original && annotations.length > 0 && (
        <button
          className="annotation-count"
          onClick={() => onThread?.(annotations[0].id)}
        >
          <MessageSquare size={12} />
          {count("comments", annotations.length)}
        </button>
      )}
    </article>
  );
}
function outlinePreview(message: { blocks?: SourceBlock[] }) {
  const block = (message.blocks || []).find(blockHasContent);
  const media = block ? pointerView(block) : null;
  if (block && embeddedImages(block).length) return t("Image");
  if (media === "image") return t("Image");
  if (media === "audio") return t("Audio");
  if (media === "video") return t("Video");
  if (media === "pdf") return t("PDF");
  if (media === "download") return t("File");
  const text = (block?.text || "").replace(/\s+/g, " ").trim();
  if (text) return text.slice(0, 65);
  const preview = block ? describeActivity(block).preview : "";
  return preview.replace(/\s+/g, " ").trim().slice(0, 65) || t("Content block");
}
export function Explorer({
  id,
  user,
  members,
  canComment,
  revision,
}: {
  id: string;
  user: any;
  members: any[];
  canComment: boolean;
  revision: number;
}) {
  useLocale();
  const [c, setC] = useState<any>(null);
  const [suppliedFiles, setSuppliedFiles] = useState<SuppliedFileRecord[]>([]);
  const [filesReady, setFilesReady] = useState(false);
  const [github, setGithub] = useState<{
    id: string;
    source: GitHubSource | null;
  }>();
  useEffect(() => {
    let live = true;
    setFilesReady(false);
    api<{ attachments: SuppliedFileRecord[] }>(
      `/conversations/${id}/recovered-source`,
    )
      .then((data) => {
        if (live) setSuppliedFiles(data.attachments || []);
      })
      .catch(() => {
        if (live) setSuppliedFiles([]);
      })
      .finally(() => {
        if (live) setFilesReady(true);
      });
    return () => {
      live = false;
    };
  }, [id, revision]);
  useEffect(() => {
    if (!c || c.id !== id) return;
    let live = true;
    setGithub(undefined);
    api<{ source: GitHubSource | null }>(`/conversations/${id}/github`)
      .then((data) => {
        if (live) setGithub({ id, source: data.source });
      })
      .catch(() => {
        if (live) setGithub(undefined);
      });
    return () => {
      live = false;
    };
  }, [id, c?.id, c?.applicationProjectId, revision]);
  const [sharing, setSharing] = useState(false);
  const [annotations, setAnnotations] = useState<any[]>([]);
  const [q, setQ] = useState("");
  const [role, setRole] = useState("");
  const [plain, setPlain] = useState(false);
  const [original, setOriginal] = useState(false);
  const [selected, setSelected] = useState<any>(null);
  const [active, setActive] = useState("");
  const [composing, setComposing] = useState(false);
  const [body, setBody] = useState("");
  const [filter, setFilter] = useState("open");
  const [topic, setTopic] = useState("");
  const [person, setPerson] = useState("");
  const [reply, setReply] = useState<Record<string, string>>({});
  const [pins, setPins] = useState<string[]>(
    JSON.parse(localStorage.getItem("pins") || "[]"),
  );
  const [branch, setBranch] = useState("");
  const [page, setPage] = useState(0);
  const [relevant, setRelevant] = useState<any[]>([]);
  const { setWorkspace, notify } = useApp();
  const refresh = () => {
    if (!c || c.id !== id) return;
    return act(async () =>
      setAnnotations(
        await api("/w/" + c.workspaceId + "/annotations?conversationId=" + id),
      ),
    );
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
      if (!cancelled) setRelevant(journeys);
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
    if (thread) setActive(thread);
    if (message && c) {
      const index = [...c.messages]
        .sort((a, b) => a.order - b.order)
        .filter((m) => messageHasContent(m))
        .findIndex((m) => m.id === message);
      if (index >= 0) setPage(Math.floor(index / readerPage));
      setTimeout(
        () =>
          document
            .getElementById("message-" + message)
            ?.scrollIntoView({ block: "center" }),
        100,
      );
    }
  }, [c]);
  if (!c)
    return <div className="loading">{t("Opening original conversation…")}</div>;
  const ordered = [...c.messages].sort((a, b) => a.order - b.order);
  const roles = Array.from(new Set<string>(ordered.map((m) => m.role)));
  const path = new Set<string>();
  let cursor = branch;
  while (cursor && !path.has(cursor)) {
    path.add(cursor);
    cursor = c.messages.find((m: any) => m.id === cursor)?.parentId;
  }
  const messages = ordered.filter(
    (m) =>
      (messageHasContent(m) ||
        annotations.some((a) => a.messageId === m.id)) &&
      (!role || m.role === role) &&
      (!branch || path.has(m.id)) &&
      (!q ||
        m.blocks.some((b: any) =>
          (b.text || "").toLowerCase().includes(q.toLowerCase()),
        )),
  );
  const leaves = ordered.filter(
    (m) => !ordered.some((x) => x.parentId === m.id),
  );
  const pageCount = Math.max(1, Math.ceil(messages.length / readerPage));
  const safePage = Math.min(page, pageCount - 1);
  const start = safePage * readerPage;
  const visible = messages.slice(start, start + readerPage);
  const openMessage = (m: any) => {
    const index = messages.indexOf(m);
    setPage(Math.floor(Math.max(index, 0) / readerPage));
    setTimeout(
      () =>
        document
          .getElementById("message-" + m.id)
          ?.scrollIntoView({ behavior: "smooth", block: "start" }),
      50,
    );
  };
  const submit = () =>
    act(async () => {
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
  const showThread = (id: string) => {
    setActive(id);
    setOriginal(false);
    setFilter("all");
    setTopic("");
    setTimeout(
      () =>
        document
          .getElementById("thread-" + id)
          ?.scrollIntoView({ block: "center" }),
      50,
    );
  };
  return (
    <div className="explorer">
      {sharing && (
        <PublishDialog conversation={c} close={() => setSharing(false)} />
      )}
      <div className="explorer-heading">
        <button title={t("Back to library")} onClick={() => go("library")}>
          <ArrowLeft size={17} />
        </button>
        <div>
          <h2>{c.title}</h2>
          <div className="row">
            <Badge provider={c.provider} />
            <small>
              {count("messages", c.messages.length)} ·{" "}
              {c.demo ? t("Demonstration data") : t("Imported source")}{" "}
              {t("· Read-only original")}
            </small>
          </div>
        </div>
        <span className="spacer" />
        {["Owner", "Admin"].includes(
          members.find((m) => m.id === user.id)?.role,
        ) && (
          <button className="primary" onClick={() => setSharing(true)}>
            {t("Share conversation")}
          </button>
        )}
        <button onClick={() => go("compare?left=" + id)}>
          <Columns2 size={16} />
          {t("Compare")}
        </button>
        <a className="button" href={"/api/sources/" + c.sourceId}>
          <Download size={15} />
          {t("Original file")}
        </a>
      </div>
      <div className="explorer-panes">
        <aside className="outline">
          <span className="eyebrow">{t("CONVERSATION OUTLINE")}</span>
          <SourceHistory
            conversation={c}
            onUpdated={() => {
              act(async () => setC(await loadConversation(id)));
              refresh();
            }}
            canEdit={["Owner", "Admin", "Editor"].includes(
              members.find((m) => m.id === user.id)?.role,
            )}
          />
          <label className="search compact">
            <Search size={15} />
            <input
              placeholder={t("Find in conversation…")}
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                setPage(0);
              }}
            />
          </label>
          <label>
            {t("Source branches")}
            <select
              value={branch}
              onChange={(e) => {
                setBranch(e.target.value);
                setPage(0);
              }}
            >
              <option value="">
                {t("All paths", { count: leaves.length })}
              </option>
              {leaves.map((m: any, i: number) => (
                <option value={m.id} key={m.id}>
                  {t("Path")} {i + 1} {t("· ends at #")}
                  {m.order + 1}
                </option>
              ))}
            </select>
          </label>
          <div className="message-tree">
            {messages.map((m: any) => (
              <button
                className={
                  "outline-message" + (pins.includes(m.id) ? " bookmarked" : "")
                }
                data-message-role={m.role}
                key={m.id}
                onClick={() => openMessage(m)}
              >
                <span>{String(ordered.indexOf(m) + 1).padStart(2, "0")}</span>
                <div>
                  <div className="outline-role">
                    <MessageRole role={m.role} />
                    {ordered.filter((x) => x.parentId === m.parentId).length >
                    1 ? (
                      <small>{t(" · branch")}</small>
                    ) : null}
                  </div>
                  <span className="outline-preview">
                    {outlinePreview(m)}
                  </span>
                </div>
                {pins.includes(m.id) && <Bookmark size={12} />}
              </button>
            ))}
          </div>
          <span className="eyebrow">{t("APPEARS IN")}</span>
          {relevant.map((j) => (
            <a className="journey-link" key={j.id} href={"#canvas/" + j.id}>
              <GitBranch size={14} />
              {j.title}
            </a>
          ))}
        </aside>
        <main className="reader">
          <div className="reader-toolbar">
            <span>
              <Eye size={14} />{" "}
              {original ? t("Original only") : t("Review mode")}
            </span>
            <div className="message-role-filter">
              <label>
                {t("Message role")}
                <select
                  value={role}
                  onChange={(e) => {
                    setRole(e.target.value);
                    setPage(0);
                  }}
                >
                  <option value="">{t("All roles")}</option>
                  {roles.map((value) => (
                    <option key={value} value={value}>
                      {value.charAt(0).toUpperCase() + value.slice(1)}
                    </option>
                  ))}
                </select>
              </label>
              <small role="status">
                {t("Messages shown", {
                  shown: messages.length,
                  total: ordered.length,
                })}
              </small>
            </div>
            <div className="row">
              <button
                className={plain ? "active" : ""}
                onClick={() => setPlain(!plain)}
              >
                {plain ? t("Reading view") : t("Source text")}
              </button>
              <button onClick={() => setOriginal(!original)}>
                {original ? t("Show annotations") : t("Hide annotations")}
              </button>
            </div>
          </div>
          <div className="source-notice">
            {c.demo ? t("SIMULATED DEMONSTRATION") : t("PRESERVED SOURCE")}{" "}
            <span>{t("· Curation never overwrites this conversation.")}</span>
          </div>
          {messages.length > readerPage && (
            <div className="public-pagination">
              <button
                disabled={safePage === 0}
                onClick={() => setPage(safePage - 1)}
              >
                {t("Earlier messages")}
              </button>
              <span>
                {t("Showing {from}–{to} of {total}", {
                  from: start + 1,
                  to: Math.min(start + readerPage, messages.length),
                  total: messages.length,
                })}
              </span>
              <button
                disabled={start + readerPage >= messages.length}
                onClick={() => setPage(safePage + 1)}
              >
                {t("Later messages")}
              </button>
            </div>
          )}
          {visible.map((m: any) => (
            <Message
              key={m.id}
              message={m}
              github={github?.id === id ? github.source : null}
              conversationId={id}
              attachments={suppliedFiles}
              filesReady={filesReady}
              plain={plain}
              original={original}
              annotations={annotations.filter((a) => a.messageId === m.id)}
              onThread={showThread}
              onSelect={
                canComment && !original
                  ? (a) => {
                      setSelected(a);
                      if (a.whole) setComposing(true);
                    }
                  : undefined
              }
              pinned={pins.includes(m.id)}
              onPin={() => {
                const next = pins.includes(m.id)
                  ? pins.filter((x) => x !== m.id)
                  : [...pins, m.id];
                setPins(next);
                localStorage.setItem("pins", JSON.stringify(next));
              }}
            />
          ))}
          {!messages.length && (
            <div className="empty">{t("No matching messages.")}</div>
          )}
        </main>
        {!original && (
          <aside className="review-pane">
            <div className="section-title">
              <h3>
                {t("Discussion")}{" "}
                <span className="count">
                  {formatNumber(annotations.length)}
                </span>
              </h3>
              <MessageSquare size={17} />
            </div>
            <div className="review-filters">
              <TopicFilter
                annotations={annotations}
                value={topic}
                onChange={setTopic}
              />
              <select
                aria-label={t("Comment status")}
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
              >
                <option value="open">{t("Open threads")}</option>
                <option value="resolved">{t("Resolved")}</option>
                <option value="all">{t("All threads")}</option>
              </select>
              <select
                aria-label={t("Comment author")}
                value={person}
                onChange={(e) => setPerson(e.target.value)}
              >
                <option value="">{t("Everyone")}</option>
                {members.map((m) => (
                  <option value={m.id} key={m.id}>
                    {m.name}
                  </option>
                ))}
              </select>
            </div>
            <p className="muted small-text">
              {t(
                "Select a passage to add a contextual comment. Source text mode supports exact code and Markdown ranges.",
              )}
            </p>
            {composing && selected && (
              <div className="thread composing">
                <strong>{t("New contextual comment")}</strong>
                {selected.quote && <blockquote>{selected.quote}</blockquote>}
                <MentionInput body={body} setBody={setBody} members={members} />
                <AnnotationTools body={body} setBody={setBody} />
                <div className="row">
                  <button
                    onClick={() => {
                      setComposing(false);
                      setSelected(null);
                    }}
                  >
                    {t("Cancel")}
                  </button>
                  <button
                    className="primary"
                    disabled={!body.trim()}
                    onClick={submit}
                  >
                    {t("Post comment")}
                  </button>
                </div>
              </div>
            )}
            {annotations
              .filter(
                (a) =>
                  (filter === "all" ||
                    a.resolved === (filter === "resolved")) &&
                  (!person || a.authorId === person) &&
                  (!topic || threadTags(a).includes(topic)),
              )
              .map((a) => (
                <div
                  className={"thread " + (active === a.id ? "focused" : "")}
                  id={"thread-" + a.id}
                  key={a.id}
                  onClick={() => setActive(a.id)}
                >
                  <div className="thread-top">
                    <span className="avatar tiny">
                      {initials(
                        members.find((m) => m.id === a.authorId)?.name ||
                          "Curator",
                      )}
                    </span>
                    <strong>
                      {members.find((m) => m.id === a.authorId)?.name ||
                        "Curator"}
                    </strong>
                    <button
                      title={
                        a.resolved
                          ? t("Reopen thread")
                          : t(
                              threadTags(a).includes("action")
                                ? "Complete action"
                                : "Resolve thread",
                            )
                      }
                      disabled={!canComment}
                      onClick={() =>
                        act(async () => {
                          await api(
                            "/annotations/" + a.id,
                            { resolved: !a.resolved },
                            "PATCH",
                          );
                          refresh();
                        })
                      }
                    >
                      {a.resolved ? (
                        t("Reopen")
                      ) : threadTags(a).includes("action") ? (
                        t("Complete action")
                      ) : (
                        <Check size={15} />
                      )}
                    </button>
                  </div>
                  <AnnotationTopics annotation={a} />
                  {a.orphaned && (
                    <p className="error">
                      {t("Orphaned anchor — review required")}
                    </p>
                  )}
                  {JSON.parse(a.anchor).quote && (
                    <blockquote
                      onClick={() =>
                        document
                          .getElementById("message-" + a.messageId)
                          ?.scrollIntoView({
                            block: "center",
                            behavior: "smooth",
                          })
                      }
                    >
                      {JSON.parse(a.anchor).quote}
                    </blockquote>
                  )}
                  {a.comments.map((co: any, i: number) => (
                    <div className="comment" key={co.id}>
                      {i > 0 && (
                        <strong>
                          {members.find((m) => m.id === co.authorId)?.name ||
                            "Teammate"}
                        </strong>
                      )}
                      <AnnotationContent body={co.body} />
                      <div className="row">
                        <small>
                          {formatDate(co.createdAt, { dateStyle: "medium" })}
                        </small>
                        {co.authorId === user.id && (
                          <>
                            <button
                              onClick={() => {
                                const b = prompt(t("Edit comment"), co.body);
                                if (b !== null)
                                  act(async () => {
                                    await api(
                                      "/comments/" + co.id,
                                      { body: b },
                                      "PUT",
                                    );
                                    refresh();
                                  });
                              }}
                            >
                              {t("Edit")}
                            </button>
                            <button
                              onClick={() =>
                                act(async () => {
                                  await api(
                                    "/comments/" + co.id,
                                    undefined,
                                    "DELETE",
                                  );
                                  refresh();
                                })
                              }
                            >
                              {t("Delete")}
                            </button>
                          </>
                        )}
                      </div>
                    </div>
                  ))}
                  <AnnotationArtifacts
                    annotation={a}
                    canUpload={canComment}
                    onUploaded={() => refresh()}
                  />
                  {canComment && (
                    <form
                      onSubmit={(e) => {
                        e.preventDefault();
                        act(async () => {
                          await api("/annotations/" + a.id + "/replies", {
                            body: reply[a.id],
                          });
                          setReply({ ...reply, [a.id]: "" });
                          refresh();
                        });
                      }}
                    >
                      <MentionInput
                        body={reply[a.id] || ""}
                        setBody={(b) => setReply({ ...reply, [a.id]: b })}
                        members={members}
                        placeholder={t("Reply or @mention…")}
                      />
                      <button disabled={!reply[a.id]?.trim()}>
                        {t("Reply")}
                      </button>
                    </form>
                  )}
                </div>
              ))}
          </aside>
        )}
      </div>
      {selected && !composing && !selected.whole && (
        <div
          className="selection-toolbar"
          style={{ left: selected.x, top: selected.y }}
        >
          <button className="primary" onClick={() => setComposing(true)}>
            <MessageSquare size={14} />
            {t("Add comment")}
          </button>
          <button
            title={t("Dismiss selection")}
            onClick={() => setSelected(null)}
          >
            <X size={14} />
          </button>
        </div>
      )}
    </div>
  );
}
function MentionInput({
  body,
  setBody,
  members,
  placeholder = t("Add your perspective. Use @ to mention…"),
}: {
  body: string;
  setBody: (b: string) => void;
  members: any[];
  placeholder?: string;
}) {
  useLocale();
  const match = body.match(/@([^@\n]*)$/);
  return (
    <div className="mention-input">
      <textarea
        value={body}
        onChange={(e) => setBody(e.target.value)}
        placeholder={placeholder}
      />
      {match && (
        <div className="mention-menu">
          {members
            .filter((m) =>
              m.name.toLowerCase().includes(match[1].toLowerCase()),
            )
            .map((m) => (
              <button
                type="button"
                key={m.id}
                onClick={() =>
                  setBody(
                    body.slice(0, body.lastIndexOf("@")) + "@" + m.name + " ",
                  )
                }
              >
                <span className="avatar tiny">{initials(m.name)}</span>
                {m.name}
              </button>
            ))}
        </div>
      )}
    </div>
  );
}
