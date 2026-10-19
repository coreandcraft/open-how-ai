import { useEffect, useState } from "react";
import { MessageCircle, Lock, ArrowRight } from "lucide-react";
import { api, initials } from "../../shared/state";
import {
  t,
  useLocale,
  formatDate,
  formatNumber,
  count,
} from "../../shared/i18n/i18n";
import "./discussion.css";

export function DiscussionToggle({
  enabled,
  save,
  disabled = false,
}: {
  enabled: boolean;
  save: (enabled: boolean) => Promise<void>;
  disabled?: boolean;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  return (
    <div className="discussion-setting">
      <label className="discussion-switch">
        <span>{t("Allow comments & questions")}</span>
        <input
          type="checkbox"
          role="switch"
          checked={enabled}
          disabled={disabled || busy}
          onChange={async (e) => {
            setBusy(true);
            setError("");
            try {
              await save(e.target.checked);
            } catch (e) {
              setError((e as Error).message);
            } finally {
              setBusy(false);
            }
          }}
        />
      </label>
      <small>{t("Only signed-in, registered users can post or reply.")}</small>
      {error && <p role="alert">{t(error)}</p>}
    </div>
  );
}

type Entry = {
  id: string;
  parentId: string | null;
  body: string;
  kind: string;
  createdAt: string;
  author: string;
  isSharer: boolean;
};
type DiscussionData = {
  enabled: boolean;
  canManage: boolean;
  user: { name: string } | null;
  entries: Entry[];
  total: number;
};
export function Discussion({ slug }: { slug: string }) {
  useLocale();
  const [data, setData] = useState<DiscussionData | null>(null);
  const [error, setError] = useState("");
  const [body, setBody] = useState("");
  const [kind, setKind] = useState("Comment");
  const [replyTo, setReplyTo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [filter, setFilter] = useState("All");
  const [page, setPage] = useState(0);
  const endpoint = "/public/" + encodeURIComponent(slug) + "/discussion";
  const refresh = async (pageNumber = page) => {
    const next = await api<DiscussionData>(
      endpoint +
        "?page=" +
        pageNumber +
        "&kind=" +
        (filter === "All" ? "" : filter),
    );
    setData(next);
    return next;
  };
  useEffect(() => {
    let active = true;
    setData(null);
    setError("");
    api<DiscussionData>(
      endpoint + "?page=" + page + "&kind=" + (filter === "All" ? "" : filter),
    )
      .then((next) => {
        if (active) setData(next);
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    return () => {
      active = false;
    };
  }, [endpoint, page, filter]);
  const authLink = (register: boolean) =>
    "#" +
    (register ? "register" : "login") +
    "?returnTo=" +
    encodeURIComponent("public/" + slug);
  const renderEntry = (entry: Entry, reply = false) => (
    <article
      className={"discussion-entry " + (reply ? "discussion-reply" : "")}
      key={entry.id}
    >
      <span className="avatar">{initials(entry.author)}</span>
      <div className="discussion-entry-content">
        <header>
          <strong>{entry.author}</strong>
          {entry.isSharer && (
            <span className="discussion-pill">{t("Sharer")}</span>
          )}
          {!reply && <span className="discussion-pill">{t(entry.kind)}</span>}
          <time dateTime={entry.createdAt}>{formatDate(entry.createdAt)}</time>
        </header>
        <p>{entry.body}</p>
        {!reply && data?.user && (
          <button
            className="discussion-reply-button"
            onClick={() => {
              setReplyTo(entry.id);
              document.getElementById("discussion-body")?.focus();
            }}
          >
            {t("Reply")}
          </button>
        )}
        {!reply &&
          data?.entries
            .filter((child) => child.parentId === entry.id)
            .map((child) => renderEntry(child, true))}
      </div>
    </article>
  );
  return (
    <section
      className="discussion"
      id="discussion"
      tabIndex={-1}
      aria-label={t("Comments & questions")}
    >
      <div className="section-title">
        <h2>
          <MessageCircle size={22} />
          {t("Comments & questions")}
        </h2>
        {data?.enabled && (
          <small>{count("discussionThreads", data.total)}</small>
        )}
      </div>
      <p>{t("Talk about the work. Ask how it happened.")}</p>
      {data?.canManage && (
        <DiscussionToggle
          enabled={data.enabled}
          save={async (enabled) => {
            await api(endpoint, { enabled }, "PATCH");
            await refresh();
          }}
        />
      )}
      {error && (
        <p role="alert" className="discussion-error">
          {t(error)}{" "}
          <button
            onClick={() => {
              setError("");
              void refresh().catch((e) => setError(e.message));
            }}
          >
            {t("Retry")}
          </button>
        </p>
      )}
      {!data && !error && <p role="status">{t("Loading discussion…")}</p>}
      {data && !data.enabled && (
        <div className="discussion-closed">
          <Lock size={22} />
          <div>
            <h3>{t("Discussion is turned off")}</h3>
            <p>
              {t("The person sharing has not enabled comments or questions.")}
            </p>
          </div>
        </div>
      )}
      {data?.enabled && (
        <>
          {data.user ? (
            <form
              className="discussion-composer"
              onSubmit={async (e) => {
                e.preventDefault();
                if (busy || !body.trim()) return;
                setBusy(true);
                setError("");
                try {
                  await api(endpoint, { body, kind, parentId: replyTo });
                  setBody("");
                  setReplyTo(null);
                  if (page !== 0 && !replyTo) setPage(0);
                  else await refresh();
                } catch (e) {
                  setError((e as Error).message);
                  await refresh().catch(() => {});
                } finally {
                  setBusy(false);
                }
              }}
            >
              <div className="row">
                <span className="avatar">{initials(data.user.name)}</span>
                <strong>{data.user.name}</strong>
                {!replyTo && (
                  <div
                    className="discussion-tabs"
                    role="group"
                    aria-label={t("Post type")}
                  >
                    {["Comment", "Question"].map((value) => (
                      <button
                        type="button"
                        key={value}
                        aria-pressed={kind === value}
                        onClick={() => setKind(value)}
                      >
                        {t(value)}
                      </button>
                    ))}
                  </div>
                )}
              </div>
              {replyTo && (
                <div className="discussion-reply-context">
                  {t("Replying to {name}", {
                    name:
                      data.entries.find((e) => e.id === replyTo)?.author || "",
                  })}
                  <button type="button" onClick={() => setReplyTo(null)}>
                    {t("Cancel")}
                  </button>
                </div>
              )}
              <label htmlFor="discussion-body">
                {t(replyTo ? "Reply" : kind)}
              </label>
              <textarea
                id="discussion-body"
                value={body}
                onChange={(e) => setBody(e.target.value)}
                required
                maxLength={4000}
                placeholder={t("What would you like to share or ask?")}
                disabled={busy}
              />
              <footer>
                <small>
                  {t(
                    "Your name and contribution will be visible to anyone with this link.",
                  )}
                </small>
                <button className="primary" disabled={busy || !body.trim()}>
                  {t(
                    busy
                      ? "Posting…"
                      : replyTo
                        ? "Post reply"
                        : kind === "Question"
                          ? "Post question"
                          : "Post comment",
                  )}
                  <ArrowRight size={15} />
                </button>
              </footer>
            </form>
          ) : (
            <div className="discussion-guest">
              <h3>{t("Join the conversation")}</h3>
              <p>
                {t(
                  "Sign in or create an account to comment or ask a question.",
                )}
              </p>
              <div className="row">
                <a className="button primary" href={authLink(false)}>
                  {t("Sign in")}
                </a>
                <a className="button" href={authLink(true)}>
                  {t("Create account")}
                </a>
              </div>
            </div>
          )}
          <div
            className="discussion-tabs"
            role="group"
            aria-label={t("Filter discussion")}
          >
            {["All", "Question", "Comment"].map((value) => (
              <button
                key={value}
                aria-pressed={filter === value}
                onClick={() => {
                  setPage(0);
                  setReplyTo(null);
                  setFilter(value);
                }}
              >
                {t(
                  value === "Question"
                    ? "Questions"
                    : value === "Comment"
                      ? "Comments"
                      : "All",
                )}
              </button>
            ))}
          </div>
          {data.entries
            .filter(
              (e) => !e.parentId && (filter === "All" || filter === e.kind),
            )
            .reverse()
            .map((e) => renderEntry(e))}
          {!data.entries.some(
            (e) => !e.parentId && (filter === "All" || filter === e.kind),
          ) && (
            <p className="discussion-empty">
              {t("No contributions yet in this view.")}
            </p>
          )}
          {(page > 0 || data.total > 20) && (
            <div className="row">
              <button
                disabled={page === 0}
                onClick={() => setPage((p) => p - 1)}
              >
                {t("Previous")}
              </button>
              <small>
                {formatNumber(page + 1)} /{" "}
                {formatNumber(Math.max(1, Math.ceil(data.total / 20)))}
              </small>
              <button
                disabled={(page + 1) * 20 >= data.total}
                onClick={() => setPage((p) => p + 1)}
              >
                {t("Next")}
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}
