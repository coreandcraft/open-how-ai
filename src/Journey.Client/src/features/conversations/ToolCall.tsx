import {
  FileText,
  Search,
  SquarePen,
  Terminal,
  Wrench,
  ExternalLink,
  FileCode2,
} from "lucide-react";
import { useEffect, useState } from "react";
import { api } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
import { describeTool, type SourceBlock } from "./messagePresentation";
import { githubSourceLink, type GitHubSource } from "./githubSource";
import { SourceDisclosure } from "./SourceActivity";

export function ToolCall({
  block,
  sourceRef,
  github,
  conversationId,
  plain = false,
}: {
  block: SourceBlock;
  sourceRef: (element: HTMLElement | null) => void;
  github?: GitHubSource | null;
  conversationId?: string;
  plain?: boolean;
}) {
  const tool = describeTool(block);
  const link = githubSourceLink(block, github);
  const [expanded, setExpanded] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [preview, setPreview] = useState<{
    lines: string[];
    startLine: number;
    totalLines: number;
  }>();
  useEffect(() => {
    setExpanded(false);
    setPreview(undefined);
    setError("");
  }, [link?.url, conversationId]);
  useEffect(() => {
    if (!expanded || !link || !conversationId || preview) return;
    let live = true;
    setLoading(true);
    setError("");
    const query = new URLSearchParams({
      path: link.path,
      startLine: String(link.startLine || 1),
    });
    api(`/conversations/${conversationId}/github/file?${query}`)
      .then((data) => {
        if (live) setPreview(data);
      })
      .catch((e) => {
        if (live) setError(e.message);
      })
      .finally(() => {
        if (live) setLoading(false);
      });
    return () => {
      live = false;
    };
  }, [expanded, link?.url, conversationId, preview]);
  const Icon =
    tool.icon === "read"
      ? FileText
      : tool.icon === "edit"
        ? SquarePen
        : tool.icon === "search"
          ? Search
          : tool.icon === "run"
            ? Terminal
            : Wrench;
  const raw = block.text || block.metadata || "";
  return (
    <SourceDisclosure
      icon={Icon}
      title={tool.isResult ? t("Tool result") : tool.name || t("Tool call")}
      detail={tool.target}
      detailTitle={tool.path || tool.target}
      detailFallback={t("Preserved source block")}
      path={tool.path}
      sourceLabel={t("Original tool payload")}
      payload={raw}
      sourceRef={sourceRef}
      plain={plain}
      summaryExtra={
        link && (
          <a
            className="tool-github-link"
            href={link.url}
            target="_blank"
            rel="noopener noreferrer"
            onClick={(e) => e.stopPropagation()}
          >
            {t(link.pinned ? "View on GitHub" : "Latest on GitHub")}
            <ExternalLink size={13} aria-hidden="true" />
          </a>
        )
      }
      footer={
        link && (
          <div className="tool-github-details">
            <span>
              {link.path}
              {link.startLine
                ? ` · ${link.startLine}${link.endLine ? `–${link.endLine}` : ""}`
                : ""}
            </span>
            <span>
              {link.pinned
                ? `${link.reference.slice(0, 7)} · ${t("Session commit")}`
                : `${link.reference} · ${t("Original revision unknown")}`}
            </span>
            {conversationId && (
              <button
                type="button"
                className="tool-source-toggle"
                aria-expanded={expanded}
                aria-controls={`github-preview-${block.id}`}
                onClick={() => setExpanded((value) => !value)}
              >
                <FileCode2 size={13} />
                {t(expanded ? "Close source preview" : "Preview source")}
              </button>
            )}
            {expanded && (
              <section
                id={`github-preview-${block.id}`}
                className="github-source-preview"
                aria-label={t("GitHub source preview")}
              >
                <header>
                  <strong>{t("GitHub revision")}</strong>
                  <span>{link.reference}</span>
                </header>
                <p>
                  {t(
                    link.pinned
                      ? "Local, uncommitted changes may differ from this commit."
                      : "This file may have changed since the conversation.",
                  )}
                </p>
                {loading && <p role="status">{t("Loading…")}</p>}
                {error && (
                  <p role="alert" className="error">
                    {error}
                  </p>
                )}
                {preview && (
                  <>
                    <div className="github-source-code">
                      {preview.lines.map((line, index) => {
                        const number = preview.startLine + index;
                        const highlighted =
                          !!link.startLine &&
                          number >= link.startLine &&
                          number <= (link.endLine || link.startLine);
                        return (
                          <div
                            className={highlighted ? "highlighted" : ""}
                            key={number}
                          >
                            <span aria-hidden="true">{number}</span>
                            <code>{line || " "}</code>
                          </div>
                        );
                      })}
                    </div>
                    {(preview.startLine > 1 ||
                      preview.lines.length < preview.totalLines) && (
                      <p>
                        {t(
                          "Preview shows part of the file. Open GitHub for the full source.",
                        )}
                      </p>
                    )}
                  </>
                )}
              </section>
            )}
          </div>
        )
      }
    />
  );
}
