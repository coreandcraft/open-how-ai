import { Fragment, useId, useMemo, useState, type ReactNode } from "react";
import { ChevronDown, ChevronUp, Filter } from "lucide-react";
import { t } from "../../shared/i18n/i18n";
import { threadTags } from "../review/annotationParsing";
import "./conversation-filters.css";

type Message = { id: string; role: string; blocks: { text: string }[] };
type Annotation = {
  messageId?: string;
  quote?: string;
  comments: { text: string }[];
};

export function ConversationFilters({
  messages,
  annotations = [],
  renderMessage,
}: {
  messages: Message[];
  annotations?: Annotation[];
  renderMessage: (message: Message) => ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [role, setRole] = useState("");
  const [topic, setTopic] = useState("");
  const [annotated, setAnnotated] = useState(false);
  const panelId = useId();
  const rows = useMemo(() => {
    const byMessage = new Map<string, Annotation[]>();
    for (const annotation of annotations) {
      if (!annotation.messageId) continue;
      const existing = byMessage.get(annotation.messageId) || [];
      existing.push(annotation);
      byMessage.set(annotation.messageId, existing);
    }
    return messages.map((message) => {
      const threads = byMessage.get(message.id) || [];
      return {
        message,
        annotated: threads.length > 0,
        tags: [...new Set(threads.flatMap(threadTags))],
        text: [
          ...message.blocks.map((b) => b.text),
          ...threads.flatMap((a) => [
            a.quote || "",
            ...a.comments.map((c) => c.text),
          ]),
        ]
          .join("\n")
          .toLowerCase(),
      };
    });
  }, [messages, annotations]);
  const roles = [...new Set(messages.map((m) => m.role))];
  const topics = [...new Set(rows.flatMap((row) => row.tags))].sort();
  const query = search.trim().toLowerCase();
  const active =
    Number(!!query) + Number(!!role) + Number(!!topic) + Number(annotated);
  const matching = rows
    .map((row, index) => ({ ...row, index }))
    .filter(
      (row) =>
        (!query || row.text.includes(query)) &&
        (!role || row.message.role === role) &&
        (!topic || row.tags.includes(topic)) &&
        (!annotated || row.annotated),
    );
  const clear = () => {
    setSearch("");
    setRole("");
    setTopic("");
    setAnnotated(false);
  };
  return (
    <>
      <section
        className="conversation-filters"
        aria-label={t("Conversation filters")}
      >
        <div className="conversation-filter-toolbar">
          <button
            type="button"
            aria-expanded={open}
            aria-controls={panelId}
            onClick={() => setOpen((value) => !value)}
          >
            <Filter size={15} aria-hidden="true" />
            {t("Filter")}
            {!!active && (
              <span
                className="filter-count"
                aria-label={t("{count} active filters", { count: active })}
              >
                {active}
              </span>
            )}
            {open ? (
              <ChevronUp size={15} aria-hidden="true" />
            ) : (
              <ChevronDown size={15} aria-hidden="true" />
            )}
          </button>
          {!!active && (
            <div className="conversation-filter-status">
              <span role="status">
                {t("{shown} of {total} messages", {
                  shown: matching.length,
                  total: messages.length,
                })}
              </span>
              <button type="button" onClick={clear}>
                {t("Clear filters")}
              </button>
            </div>
          )}
        </div>
        <div id={panelId} hidden={!open} className="conversation-filter-panel">
          <div className="conversation-filter-fields">
            <label>
              {t("Search conversation")}
              <input
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={t("Search messages and annotations…")}
              />
            </label>
            <label>
              {t("Message role")}
              <select value={role} onChange={(e) => setRole(e.target.value)}>
                <option value="">{t("All roles")}</option>
                {roles.map((value) => (
                  <option key={value} value={value}>
                    {t(value)}
                  </option>
                ))}
              </select>
            </label>
            <label>
              {t("Annotation topic")}
              <select value={topic} onChange={(e) => setTopic(e.target.value)}>
                <option value="">{t("All topics")}</option>
                {topics.map((value) => (
                  <option key={value} value={value}>
                    #{value}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={annotated}
              onChange={(e) => setAnnotated(e.target.checked)}
            />
            {t("With annotations only")}
          </label>
        </div>
      </section>
      {!matching.length && (
        <p role="status">{t("No messages match these filters.")}</p>
      )}
      {matching.map((row, index) => {
        const hidden = row.index - (index ? matching[index - 1].index : -1) - 1;
        return (
          <Fragment key={row.message.id}>
            {hidden > 0 && (
              <div className="conversation-hidden-gap">
                {t("{count} messages hidden", { count: hidden })}
              </div>
            )}
            {renderMessage(row.message)}
          </Fragment>
        );
      })}
      {matching.length > 0 &&
        matching[matching.length - 1].index < messages.length - 1 && (
          <div className="conversation-hidden-gap">
            {t("{count} messages hidden", {
              count: messages.length - matching[matching.length - 1].index - 1,
            })}
          </div>
        )}
    </>
  );
}
