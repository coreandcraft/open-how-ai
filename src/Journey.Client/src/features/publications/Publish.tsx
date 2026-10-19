import { ConversationFilters } from "./ConversationFilters";
import {
  ReplacementRules,
  type ReplacementRule,
} from "../imports/SanitizationSettings";
import {
  AnnotationContent,
  AnnotationTopics,
} from "../review/AnnotationContent";
import { BrandMark } from "../../shared/ui/BrandMark";
import { Discussion, DiscussionToggle } from "./Discussion";
import { LanguagePicker } from "../../shared/i18n/LanguagePicker";
import { t, useLocale, count } from "../../shared/i18n/i18n";
import "@xyflow/react/dist/style.css";
import { ReactFlow, Background, Controls, MiniMap } from "@xyflow/react";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  ArrowLeft,
  ArrowRight,
  Check,
  ShieldCheck,
  ExternalLink,
  GitBranch,
  Copy,
  Trash2,
  X,
} from "lucide-react";
import { api, act, useApp, graph, loadConversation } from "../../shared/state";
import { Modal, Badge } from "../../shared/ui/ui";
import { Markdown } from "../conversations/Explorer";
export function PublishDialog({
  journey,
  conversation,
  close,
}: {
  journey?: any;
  conversation?: any;
  close: () => void;
}) {
  useLocale();
  const base = conversation
    ? "/conversations/" + conversation.id
    : "/journeys/" + journey.id;
  const [cs, setCs] = useState<any[]>([]);
  const [ids, setIds] = useState<string[]>([]);
  const [curatorNodeIds, setCurators] = useState<string[]>([]);
  const [annotations, setAnnotations] = useState<any[]>([]);
  const [annotationIds, setAnnotationIds] = useState<string[]>([]);
  const [includeAnnotations, setIncludeAnnotations] = useState(false);
  useEffect(() => {
    const workspaceId = conversation?.workspaceId || journey?.workspaceId;
    if (!workspaceId) return;
    act(async () => {
      const rows = await api(
        "/w/" +
          workspaceId +
          "/annotations" +
          (conversation ? "?conversationId=" + conversation.id : ""),
      );
      setAnnotations(
        rows.filter(
          (a: any) =>
            !a.orphaned &&
            (conversation
              ? !a.journeyId
              : !a.journeyId || a.journeyId === journey.id),
        ),
      );
    });
  }, [conversation?.id, journey?.id]);
  const eligibleAnnotations = annotations.filter((a) =>
    a.messageId
      ? ids.includes(a.messageId)
      : !conversation &&
        graph(journey).nodes.some((n: any) => n.id === a.nodeId),
  );
  const [redactions, setRedactions] = useState("");
  const [replacementRules, setReplacementRules] = useState<ReplacementRule[]>(
    [],
  );
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
  const [preview, setPreview] = useState<any>(null);
  const [selectedFindings, setSelectedFindings] = useState<string[]>([]);
  const [previewLoading, setPreviewLoading] = useState(false);
  const previewVersion = useRef(0);
  const [confirmed, setConfirmed] = useState(false);
  const [slug, setSlug] = useState("");
  const [publications, setPublications] = useState<any[]>([]);
  const [historyError, setHistoryError] = useState("");
  const [historyLoading, setHistoryLoading] = useState(true);
  const [deleting, setDeleting] = useState("");
  const [confirmDelete, setConfirmDelete] = useState("");
  const linkFor = (value: string) =>
    location.origin + location.pathname + "#public/" + value;
  useEffect(() => {
    let active = true;
    setHistoryLoading(true);
    setHistoryError("");
    api(base + "/publications")
      .then((rows) => {
        if (active) setPublications(rows);
      })
      .catch((e) => {
        if (active) setHistoryError(e.message);
      })
      .finally(() => {
        if (active) setHistoryLoading(false);
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
        ...new Set(
          graph(journey)
            .nodes.map((n: any) => n.data.conversationId)
            .filter(Boolean),
        ),
      ];
      setCs(
        await Promise.all(cids.map((id) => loadConversation(id as string))),
      );
    });
  }, [journey?.id, conversation?.id]);
  const request = {
    title: conversation ? title.trim() || undefined : undefined,
    messageIds: ids,
    curatorNodeIds,
    annotationIds: includeAnnotations
      ? annotationIds.filter((id) =>
          eligibleAnnotations.some((a) => a.id === id),
        )
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
      if (version === previewVersion.current) setPreview(result);
    } finally {
      if (version === previewVersion.current) setPreviewLoading(false);
    }
  };
  return (
    <Modal
      title={
        conversation
          ? t("Share this conversation")
          : t("Publish a curated journey")
      }
      onClose={close}
      wide
    >
      {!slug && <h3>{t("Create a new shared link")}</h3>}
      {slug ? (
        <div className="import-result">
          <Check size={36} />
          <h2>{t("Your snapshot is ready")}</h2>
          <p>{t("Future edits and imports do not change this publication.")}</p>
          <a
            className="button primary"
            href={"#public/" + slug}
            target="_blank"
          >
            {t("Open visitor experience")} <ExternalLink size={16} />
          </a>
          <button
            onClick={() =>
              act(async () => {
                await navigator.clipboard.writeText(
                  location.origin + location.pathname + "#public/" + slug,
                );
                useApp.getState().notify(t("Link copied"));
              })
            }
          >
            {t("Copy link")}
          </button>
          <input
            aria-label={t("Publication link")}
            readOnly
            value={location.origin + location.pathname + "#public/" + slug}
          />
        </div>
      ) : (
        <>
          <div className="publish-notice">
            <ShieldCheck />
            <p>
              {conversation && (
                <>
                  {t(
                    "No journey needed. Create a link to the messages you choose.",
                  )}{" "}
                </>
              )}
              {t(
                "A separate, read-only snapshot. Raw files, metadata and attachments are excluded. Annotations stay private unless you select them below.",
              )}
            </p>
          </div>
          {conversation && (
            <label>
              {t("Shared conversation title (optional)")}
              <input
                value={title}
                maxLength={200}
                placeholder={conversation.title}
                onChange={(e) => {
                  setTitle(e.target.value);
                  invalidate();
                }}
              />
              <small>
                {t("Leave blank to use the current conversation title.")}
              </small>
            </label>
          )}
          <DiscussionToggle
            enabled={discussionEnabled}
            save={async (enabled) => {
              setDiscussionEnabled(enabled);
              invalidate();
            }}
          />
          <div className="publish-grid">
            <div>
              {!conversation && <h3>{t("Curator explanations")}</h3>}
              {(conversation ? { nodes: [] } : graph(journey)).nodes
                .filter((n: any) => n.data.body)
                .map((n: any) => (
                  <label className="checkbox-row" key={n.id}>
                    <input
                      type="checkbox"
                      checked={curatorNodeIds.includes(n.id)}
                      onChange={(e) => {
                        setCurators((s) =>
                          e.target.checked
                            ? [...s, n.id]
                            : s.filter((x) => x !== n.id),
                        );
                        invalidate();
                      }}
                    />
                    {n.data.title}
                  </label>
                ))}
              <h3>{t("Choose source messages")}</h3>
              <label>
                {t("Select messages by role")}
                <select
                  value=""
                  onChange={(e) => {
                    const role = e.target.value;
                    if (!role) return;
                    setIds(
                      cs.flatMap((c) =>
                        c.messages
                          .filter((m: any) => m.role === role)
                          .map((m: any) => m.id),
                      ),
                    );
                    invalidate();
                  }}
                >
                  <option value="">{t("Choose a role")}</option>
                  {[
                    ...new Set<string>(
                      cs.flatMap((c) => c.messages.map((m: any) => m.role)),
                    ),
                  ].map((role) => (
                    <option key={role} value={role}>
                      {t(role)}
                    </option>
                  ))}
                </select>
                <small>
                  {t("Replaces the selection with messages from this role.")}
                </small>
              </label>
              <button
                onClick={() => {
                  setIds(cs.flatMap((c) => c.messages.map((m: any) => m.id)));
                  invalidate();
                }}
              >
                {t("Select all messages")}
              </button>
              <button
                onClick={() => {
                  setIds([]);
                  invalidate();
                }}
              >
                {t("Clear selection")}
              </button>
              <div className="publish-messages">
                {cs.map((c) => (
                  <details key={c.id}>
                    <summary>{c.title}</summary>
                    {c.messages.map((m: any) => (
                      <label className="checkbox-row" key={m.id}>
                        <input
                          type="checkbox"
                          checked={ids.includes(m.id)}
                          onChange={(e) => {
                            setIds((s) =>
                              e.target.checked
                                ? [...s, m.id]
                                : s.filter((id) => id !== m.id),
                            );
                            invalidate();
                          }}
                        />
                        <span>
                          <small>{t(m.role)}</small>
                          {m.blocks
                            .map((b: any) => b.text)
                            .join(" ")
                            .slice(0, 130)}
                        </span>
                      </label>
                    ))}
                  </details>
                ))}
              </div>
            </div>
            <div>
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={includeAnnotations}
                  onChange={(e) => {
                    setIncludeAnnotations(e.target.checked);
                    invalidate();
                  }}
                />
                {t("Include selected annotations")}
              </label>
              {includeAnnotations && (
                <div className="publication-annotation-options">
                  <p>
                    {t(
                      "Choose threads to share, including their current replies. Author identities and mention notifications are excluded. Only annotations on included content are available.",
                    )}
                  </p>
                  <div className="row">
                    <button
                      type="button"
                      disabled={!eligibleAnnotations.length}
                      onClick={() => {
                        setAnnotationIds(eligibleAnnotations.map((a) => a.id));
                        invalidate();
                      }}
                    >
                      {t("Select all annotations")}
                    </button>
                    <button
                      type="button"
                      disabled={!annotationIds.length}
                      onClick={() => {
                        setAnnotationIds([]);
                        invalidate();
                      }}
                    >
                      {t("Clear annotations")}
                    </button>
                  </div>
                  {!eligibleAnnotations.length && (
                    <p>
                      {t(
                        "Select source messages with annotations, or add a node annotation to this journey.",
                      )}
                    </p>
                  )}
                  {eligibleAnnotations.map((a) => (
                    <label className="checkbox-row" key={a.id}>
                      <input
                        type="checkbox"
                        checked={annotationIds.includes(a.id)}
                        onChange={(e) => {
                          setAnnotationIds((old) =>
                            e.target.checked
                              ? [...old, a.id]
                              : old.filter((id) => id !== a.id),
                          );
                          invalidate();
                        }}
                      />
                      <span>
                        {a.comments.map((c: any) => c.body).join(" · ")}
                      </span>
                    </label>
                  ))}
                </div>
              )}
              <label>
                {t("Redact exact text (one phrase per line)")}
                <textarea
                  rows={5}
                  value={redactions}
                  onChange={(e) => {
                    setRedactions(e.target.value);
                    invalidate();
                  }}
                  placeholder={t(
                    "Sensitive phrases are replaced on the server.",
                  )}
                />
              </label>
              <ReplacementRules
                rules={replacementRules}
                disabled={!rulesLoaded}
                onChange={(rules) => {
                  setReplacementRules(rules);
                  invalidate();
                }}
              />
              <label>
                {t("Publication visibility")}
                <select
                  value={visibility}
                  onChange={(e) => {
                    setVisibility(e.target.value);
                    invalidate();
                  }}
                >
                  <option value="Unlisted">{t("Unlisted")}</option>
                  <option value="Public">{t("Public")}</option>
                </select>
              </label>
              <button
                className="primary"
                disabled={
                  !rulesLoaded || previewLoading || selectedFindings.length > 0
                }
                onClick={() => act(() => reviewPreview())}
              >
                {t("Review sanitized preview")}
              </button>
              {previewLoading && <p role="status">{t("Updating preview…")}</p>}
              {preview && (
                <>
                  <h3>{t("Pre-publication review")}</h3>
                  {preview.findings.length ? (
                    <>
                      <p className="error">
                        {t("Possible sensitive text remains:")}
                      </p>
                      <p>
                        {t(
                          "Select the text you want to redact, then apply your selection.",
                        )}
                      </p>
                      <div className="row">
                        <button
                          type="button"
                          onClick={() => {
                            setSelectedFindings(preview.findings);
                            setConfirmed(false);
                          }}
                        >
                          {t("Select all findings")}
                        </button>
                        <button
                          type="button"
                          disabled={!selectedFindings.length}
                          onClick={() => setSelectedFindings([])}
                        >
                          {t("Clear selection")}
                        </button>
                      </div>
                      {preview.findings.map((s: string) => (
                        <label className="checkbox-row finding" key={s}>
                          <input
                            type="checkbox"
                            checked={selectedFindings.includes(s)}
                            onChange={(e) => {
                              setSelectedFindings((selected) =>
                                e.target.checked
                                  ? [...selected, s]
                                  : selected.filter((item) => item !== s),
                              );
                              setConfirmed(false);
                            }}
                          />
                          <span>{s}</span>
                        </label>
                      ))}
                      <button
                        type="button"
                        className="redact-selected"
                        disabled={!selectedFindings.length}
                        onClick={() =>
                          act(async () => {
                            const next = [
                              ...new Set([
                                ...redactions.split("\n").filter(Boolean),
                                ...selectedFindings,
                              ]),
                            ].join("\n");
                            setRedactions(next);
                            await reviewPreview(next);
                          })
                        }
                      >
                        {t("Redact selected ({count})", {
                          count: selectedFindings.length,
                        })}
                      </button>
                    </>
                  ) : (
                    <p className="success">
                      {t(
                        "No common credential, email, or path patterns detected.",
                      )}
                    </p>
                  )}
                  <small>
                    {t(
                      "Pattern scanning is not exhaustive. Review the exact snapshot below.",
                    )}
                  </small>
                  <label className="checkbox-row">
                    <input
                      type="checkbox"
                      checked={confirmed}
                      disabled={selectedFindings.length > 0}
                      onChange={(e) => setConfirmed(e.target.checked)}
                    />
                    {t(
                      "I reviewed the included content and explicitly confirm publication.",
                    )}
                  </label>
                  <button
                    className="primary"
                    disabled={!confirmed || selectedFindings.length > 0}
                    onClick={() =>
                      act(async () =>
                        setSlug((await api(base + "/publish", request)).slug),
                      )
                    }
                  >
                    {conversation
                      ? t(
                          visibility === "Public"
                            ? "Create public link"
                            : "Create unlisted link",
                        )
                      : t(
                          visibility === "Public"
                            ? "Publish public snapshot"
                            : "Publish unlisted snapshot",
                        )}
                  </button>
                </>
              )}
            </div>
          </div>
          {preview && (
            <div className="publication-preview">
              <Snapshot data={preview.snapshot} />
            </div>
          )}
        </>
      )}
      <section
        className="share-history"
        aria-label={t("Previously shared links")}
      >
        <h3>{t("Previously shared links")}</h3>
        <p>
          {t(
            "Copy an existing link, or delete it to revoke visitor access. Each link is a separate snapshot.",
          )}
        </p>
        {historyLoading && <p role="status">{t("Loading shared links…")}</p>}
        {historyError && (
          <p role="alert" className="error">
            {historyError}
          </p>
        )}
        {!historyLoading && !historyError && !publications.length && (
          <p>{t("No shared links yet.")}</p>
        )}
        {publications.map((p) => (
          <div className="share-history-row" key={p.id}>
            <div className="share-history-info">
              <strong>{t(p.visibility)}</strong> ·{" "}
              <time dateTime={p.createdAt}>
                {new Date(p.createdAt).toLocaleString()}
              </time>
              <a href={"#public/" + p.slug} target="_blank" rel="noreferrer">
                {linkFor(p.slug)}
              </a>
              {p.isLegacy && (
                <small>
                  {t(
                    "To enable discussion on an older link, create a new shared link.",
                  )}
                </small>
              )}
              {p.canManageDiscussion && (
                <DiscussionToggle
                  enabled={p.discussionEnabled}
                  save={async (enabled) => {
                    await api(
                      "/public/" + p.slug + "/discussion",
                      { enabled },
                      "PATCH",
                    );
                    setPublications((rows) =>
                      rows.map((row) =>
                        row.id === p.id
                          ? { ...row, discussionEnabled: enabled }
                          : row,
                      ),
                    );
                  }}
                />
              )}
            </div>
            <div className="share-history-actions">
              {confirmDelete !== p.id && (
                <button
                  type="button"
                  onClick={() =>
                    act(async () => {
                      await navigator.clipboard.writeText(linkFor(p.slug));
                      useApp.getState().notify(t("Link copied"));
                    })
                  }
                >
                  <Copy size={14} />
                  {t("Copy link")}
                </button>
              )}
              {confirmDelete !== p.id ? (
                <button
                  type="button"
                  className="share-delete"
                  aria-label={t("Delete link")}
                  title={t("Delete link")}
                  onClick={() => setConfirmDelete(p.id)}
                >
                  <Trash2 size={15} />
                </button>
              ) : (
                <div
                  className="share-delete-confirm"
                  role="group"
                  aria-label={t("Revoke this link?")}
                >
                  <span>{t("Revoke this link?")}</span>
                  <button
                    type="button"
                    className="share-delete"
                    aria-label={t("Confirm delete")}
                    title={t("Confirm delete")}
                    disabled={!!deleting}
                    onClick={async () => {
                      setDeleting(p.id);
                      setHistoryError("");
                      try {
                        await api(
                          base + "/publications/" + p.id,
                          undefined,
                          "DELETE",
                        );
                        setPublications((rows) =>
                          rows.filter((row) => row.id !== p.id),
                        );
                        setConfirmDelete("");
                        if (slug === p.slug) setSlug("");
                        useApp.getState().notify(t("Shared link deleted"));
                      } catch (e) {
                        setHistoryError((e as Error).message);
                      } finally {
                        setDeleting("");
                      }
                    }}
                  >
                    <Check size={15} />
                  </button>
                  <button
                    type="button"
                    disabled={!!deleting}
                    aria-label={t("Cancel")}
                    title={t("Cancel")}
                    onClick={() => setConfirmDelete("")}
                  >
                    <X size={15} />
                  </button>
                </div>
              )}
            </div>
          </div>
        ))}
      </section>
    </Modal>
  );
}
function SharedAnnotations({
  annotations,
  messageId,
  nodeId,
}: {
  annotations?: any[];
  messageId?: string;
  nodeId?: string;
}) {
  const threads = (annotations || []).filter((a) =>
    messageId ? a.messageId === messageId : !a.messageId && a.nodeId === nodeId,
  );
  return (
    <>
      {threads.map((a, i) => (
        <details className="shared-annotation" key={i}>
          <summary
            aria-label={t("Shared annotation") + " " + (i + 1)}
            title={t("Show or hide annotation")}
          >
            <span className="annotation-marker" aria-hidden="true">
              [!]
            </span>
            <AnnotationTopics annotation={a} />
          </summary>
          <aside className="published-curation">
            <small>
              {t("SHARED ANNOTATION")} ·{" "}
              {a.resolved ? t("Resolved") : t("Open")}
            </small>
            {a.quote && <blockquote>{a.quote}</blockquote>}
            {a.comments.map((c: any, index: number) => (
              <div key={index}>
                {index > 0 && <small>{t("Reply")} · </small>}
                <AnnotationContent body={c.text} />
              </div>
            ))}
          </aside>
        </details>
      ))}
    </>
  );
}
const publishedMessagePage = 20;

function excerptBlocks(message: any) {
  return (message.blocks || []).filter(
    (block: any) => typeof block.text === "string" && block.text.trim(),
  );
}

function shownPublishedMessages(messages: any[], annotations?: any[]) {
  const annotated = new Set(
    (annotations || [])
      .map((annotation) => annotation.messageId)
      .filter((id: unknown): id is string => typeof id === "string" && !!id),
  );
  return messages.filter(
    (message) => excerptBlocks(message).length > 0 || annotated.has(message.id),
  );
}

function PublishedMessage({
  message,
  annotations,
  shared = false,
}: {
  message: any;
  annotations?: any[];
  shared?: boolean;
}) {
  const blocks = excerptBlocks(message);
  const threads = (annotations || []).filter(
    (annotation) => annotation.messageId === message.id,
  );
  if (!blocks.length && !threads.length) return null;
  return (
    <article className="public-message" data-message-role={message.role}>
      {blocks.length > 0 && (
        <>
          <small className="public-message-label">
            <strong>
              {t("ORIGINAL")} {t(message.role)}
            </strong>{" "}
            {t(shared ? "· SHARED EXCERPT" : "· PUBLISHED EXCERPT")}
          </small>
          {blocks.map((block: any, index: number) => (
            <Markdown key={index} text={block.text} />
          ))}
        </>
      )}
      {threads.length > 0 && (
        <SharedAnnotations annotations={annotations} messageId={message.id} />
      )}
    </article>
  );
}

function publishedMessages(step: any, data: any) {
  if (step?.messages?.length) return step.messages;
  const id = step?.conversationId;
  if (!id) return [];
  return (
    (data.sources || []).find((source: any) => source.id === id)?.messages || []
  );
}

function Snapshot({ data }: { data: any }) {
  useLocale();
  const [index, setIndex] = useState(0);
  const [messagePage, setMessagePage] = useState(0);
  const [all, setAll] = useState(false);
  const [canvas, setCanvas] = useState(false);
  const steps = data.steps || [];
  const totalMessages = useMemo(
    () =>
      steps.reduce(
        (sum: number, step: any) =>
          sum +
          shownPublishedMessages(
            publishedMessages(step, data),
            data.annotations,
          ).length,
        0,
      ),
    [steps, data],
  );
  const heavy = totalMessages > publishedMessagePage * 2;
  const flowNodes = useMemo(() => {
    const raw: { x: number; y: number }[] = steps.map((s: any, i: number) => ({
      x: Number.isFinite(s.position?.x) ? s.position.x : (i % 4) * 280,
      y: Number.isFinite(s.position?.y)
        ? s.position.y
        : Math.floor(i / 4) * 170,
    }));
    const gap = (values: number[]) => {
      const sorted = [
        ...new Set(values.map((value) => Math.round(value))),
      ].sort((a, b) => a - b);
      let min = Infinity;
      for (let i = 1; i < sorted.length; i++)
        min = Math.min(min, sorted[i] - sorted[i - 1]);
      return min;
    };
    const minGap = Math.min(
      gap(raw.map((point) => point.x)),
      gap(raw.map((point) => point.y)),
    );
    const scale = !Number.isFinite(minGap) || minGap >= 240 ? 1 : 260 / minGap;
    const minX = raw.length ? Math.min(...raw.map((point) => point.x)) : 0;
    const minY = raw.length ? Math.min(...raw.map((point) => point.y)) : 0;
    return steps.map((s: any, i: number) => ({
      id: s.id,
      position: {
        x: (raw[i].x - minX) * scale,
        y: (raw[i].y - minY) * scale,
      },
      data: { label: s.title },
      style: { fontSize: 12, width: 220, padding: 16 },
    }));
  }, [steps]);
  const openChapter = (next: number) => {
    setIndex(next);
    setMessagePage(0);
    setAll(false);
    setCanvas(false);
  };
  if (data.kind === "conversation")
    return (
      <div className="public-content standalone-share">
        <span className="eyebrow">{t("SHARED CONVERSATION")}</span>
        <h1>{data.title}</h1>
        <Badge provider={data.provider} />
        <p className="subtitle">
          {t("Selected messages · Read-only snapshot ·")}{" "}
          {count(
            "messages",
            shownPublishedMessages(data.messages || [], data.annotations)
              .length,
          )}
        </p>
        <ConversationFilters
          messages={shownPublishedMessages(
            data.messages || [],
            data.annotations,
          )}
          annotations={data.annotations}
          renderMessage={(m) => (
            <PublishedMessage
              key={m.id}
              message={m}
              annotations={data.annotations}
              shared
            />
          )}
        />
      </div>
    );
  return (
    <div className="public-content">
      <span className="eyebrow">{t("A CURATED AI JOURNEY")}</span>
      <h1>{data.title}</h1>
      <p className="subtitle">{data.description}</p>
      <div className="public-progress">
        <progress max={Math.max(steps.length, 1)} value={index + 1} />
        <span>
          {t("Chapters shown", {
            current: steps.length ? index + 1 : 0,
            total: steps.length,
          })}
        </span>
        {heavy ? (
          <span>
            {t(
              "Open one chapter at a time. This publication has too many messages to show at once.",
            )}
          </span>
        ) : (
          <button onClick={() => setAll(!all)}>
            {all ? t("Guided reading") : t("Read all chapters")}
          </button>
        )}
      </div>
      <p className="subtitle">
        Chapters are a recommended reading order, not an execution timeline.
        Explore the canvas to see parallel branches and shared outcomes.
      </p>
      <button onClick={() => setCanvas(!canvas)}>
        {canvas ? t("Return to guided reading") : t("Explore journey canvas")}
      </button>
      {canvas ? (
        <div className="public-canvas">
          <ReactFlow
            ariaLabelConfig={{
              "controls.zoomIn.ariaLabel": t("Zoom in"),
              "controls.zoomOut.ariaLabel": t("Zoom out"),
              "controls.fitView.ariaLabel": t("Fit view"),
              "controls.interactive.ariaLabel": t("Toggle interactivity"),
            }}
            nodes={flowNodes}
            edges={data.connections || []}
            onlyRenderVisibleElements
            minZoom={0.05}
            nodesDraggable={false}
            nodesConnectable={false}
            elementsSelectable
            onNodeClick={(_, node) => {
              const next = steps.findIndex((s: any) => s.id === node.id);
              if (next >= 0) openChapter(next);
            }}
            fitView
            fitViewOptions={{ padding: 0.15, minZoom: 0.35 }}
          >
            <Background />
            <Controls />
            <MiniMap />
          </ReactFlow>
        </div>
      ) : (
        <div className="public-layout">
          <aside>
            {steps.map((s: any, i: number) => (
              <button
                className={index === i ? "active" : ""}
                key={s.id}
                onClick={() => openChapter(i)}
              >
                <span>{String(i + 1).padStart(2, "0")}</span>
                {s.title}
              </button>
            ))}
          </aside>
          <main>
            {(all && !heavy ? steps : steps.slice(index, index + 1)).map(
              (s: any) => {
                const messages = shownPublishedMessages(
                  publishedMessages(s, data),
                  data.annotations,
                );
                const start = messagePage * publishedMessagePage;
                const page = messages.slice(
                  start,
                  start + publishedMessagePage,
                );
                return (
                  <section key={s.id}>
                    <span className="eyebrow">
                      {s.kind === "conversation"
                        ? t("PRESERVED CONVERSATION")
                        : t("CURATOR MILESTONE")}
                    </span>
                    <h2>{s.title}</h2>
                    {s.provider && <Badge provider={s.provider} />}{" "}
                    {s.commentary && (
                      <div className="published-curation">
                        <small>{t("CURATOR EXPLANATION")}</small>
                        <Markdown text={s.commentary} />
                      </div>
                    )}
                    <SharedAnnotations
                      annotations={data.annotations}
                      nodeId={s.id}
                    />
                    {page.map((m: any) => (
                      <PublishedMessage
                        key={m.id}
                        message={m}
                        annotations={data.annotations}
                      />
                    ))}
                    {messages.length > publishedMessagePage && (
                      <div className="public-pagination">
                        <button
                          disabled={start === 0}
                          onClick={() => setMessagePage((page) => page - 1)}
                        >
                          {t("Earlier messages")}
                        </button>
                        <span>
                          {t("Showing {from}–{to} of {total}", {
                            from: start + 1,
                            to: Math.min(
                              start + publishedMessagePage,
                              messages.length,
                            ),
                            total: messages.length,
                          })}
                        </span>
                        <button
                          disabled={
                            start + publishedMessagePage >= messages.length
                          }
                          onClick={() => setMessagePage((page) => page + 1)}
                        >
                          {t("Later messages")}
                        </button>
                      </div>
                    )}
                  </section>
                );
              },
            )}
            {!steps.length && (
              <p>{t("No source messages were selected for publication.")}</p>
            )}
            <div className="public-pagination">
              <button
                disabled={index === 0}
                onClick={() => {
                  setIndex((i) => i - 1);
                  setMessagePage(0);
                }}
              >
                <ArrowLeft size={16} />
                {t("Previous")}
              </button>
              <button
                disabled={index >= steps.length - 1}
                className="primary"
                onClick={() => {
                  setIndex((i) => i + 1);
                  setMessagePage(0);
                }}
              >
                {t("Next chapter")}
                <ArrowRight size={16} />
              </button>
            </div>
          </main>
        </div>
      )}
    </div>
  );
}
export function PublicJourney({ slug }: { slug: string }) {
  useLocale();
  const [data, setData] = useState<any>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    api("/public/" + slug)
      .then(setData)
      .catch((e) => setError(e.message));
  }, [slug]);
  return (
    <div className="public-page">
      <header>
        <a className="brand" href="#dashboard">
          <BrandMark />
          openhow
        </a>
        <span>
          {data?.kind === "conversation"
            ? t("Shared conversation")
            : t("Published journey")}{" "}
          {t("· Read-only snapshot")}
        </span>
        <LanguagePicker />
        <button
          className="discussion-jump"
          onClick={() => {
            const section = document.getElementById("discussion");
            section?.scrollIntoView({ behavior: "smooth" });
            section?.focus({ preventScroll: true });
          }}
        >
          {t("Discussion")}
        </button>
        <a href="#dashboard">{t("Open your workspace ↗")}</a>
      </header>
      {data ? (
        <Snapshot data={data} />
      ) : (
        <div className="empty">{error || t("Opening published journey…")}</div>
      )}
      {data && <Discussion key={slug} slug={slug} />}
      <footer>
        {t(
          "The original conversations are the source. The journey is a curated perspective.",
        )}
      </footer>
    </div>
  );
}
