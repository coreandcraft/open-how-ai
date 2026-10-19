import { t, useLocale, formatNumber, count } from "../../shared/i18n/i18n";
import { useEffect, useState } from "react";
import { ArrowRight, Check, Link2, Plus, Save, X } from "lucide-react";
import { api, act, useApp, useLibrary, useJourneyOptions, loadConversation, type Page } from "../../shared/state";
import { Badge } from "../../shared/ui/ui";
import { Message } from "../conversations/Explorer";
type Finding = {
  left: string;
  right: string;
  note: string;
  criterion?: string;
};
const excerpt = (m: any) =>
  m?.blocks
    ?.filter((b: any) => ["text", "markdown"].includes(b.kind))
    .map((b: any) => b.text)
    .join("\n") || t("Structured content — open the source to inspect.");
export function Comparison({
  conversations,
  canEdit,
}: {
  conversations: number;
  canEdit: boolean;
}) {
  useLocale();
  const params = new URLSearchParams(location.hash.split("?")[1]);
  const [left, setLeft] = useState(params.get("left") || "");
  const [right, setRight] = useState("");
  const [docs, setDocs] = useState<any[]>([]);
  const [sessions, setSessions] = useState<any[]>([]);
  const [savedId, setSavedId] = useState("");
  const [question, setQuestion] = useState("");
  const [links, setLinks] = useState<Finding[]>([]);
  const [picks, setPicks] = useState(["", ""]);
  const [note, setNote] = useState("");
  const [criterion, setCriterion] = useState("Approach");
  const [jid, setJid] = useState("");
  const [outcome, setOutcome] = useState("Undecided");
  const [rationale, setRationale] = useState("");
  const [view, setView] = useState<"sources" | "findings">("sources");
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [ranges, setRanges] = useState<number[][]>([
    [1, 999],
    [1, 999],
  ]);
  const { workspace, notify } = useApp();
  const journeys = useJourneyOptions(workspace);
  const [searchQ, setSearchQ] = useState("");
  const search = useLibrary(workspace, { q: searchQ, limit: 40 });
  useEffect(() => {
    if (!left && search.items.length) setLeft(search.items[0].id);
    if (!right && search.items.length)
      setRight(
        search.items.find((c) => c.id !== (left || search.items[0].id))?.id ||
          "",
      );
  }, [search.items, left]);
  useEffect(() => {
    let live = true;
    setDocs([]);
    if (left && right)
      loadConversation(left)
        .then(async (l) => {
          const r = await loadConversation(right);
          if (live) setDocs([l, r]);
        })
        .catch((e) => notify(e.message));
    return () => {
      live = false;
    };
  }, [left, right]);
  const loadSessions = () =>
    act(async () => {
      const page = await api<Page>("/w/" + workspace + "/comparisons");
      setSessions(page.items);
    });
  useEffect(() => {
    loadSessions();
  }, [workspace]);
  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (dirty) e.preventDefault();
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty]);
  const change = () => setDirty(true);
  const load = (id: string) => {
    if (dirty && !confirm(t("Discard unsaved comparison changes?"))) return;
    if (!id) {
      setSavedId("");
      setQuestion("");
      setLinks([]);
      setOutcome("Undecided");
      setRationale("");
      setJid("");
      setRanges([
        [1, 999],
        [1, 999],
      ]);
      setPicks(["", ""]);
      setDirty(false);
      return;
    }
    act(async () => {
      const s = await api("/comparisons/" + id);
      setSavedId(s.id);
      setLeft(s.leftId);
      setRight(s.rightId);
      setQuestion(s.question || s.title);
      setLinks(JSON.parse(s.links));
      setOutcome(s.outcome || "Undecided");
      setRationale(s.rationale || "");
      setJid(s.journeyId || "");
      setRanges(JSON.parse(s.ranges || "[[1,999],[1,999]]"));
      setPicks(["", ""]);
      setDirty(false);
      setView("findings");
    });
  };
  const pickConversation = (index: number, id: string) => {
    if (
      links.length &&
      !confirm(
        t(
          "Changing a source clears its evidence findings and decision. Continue?",
        ),
      )
    )
      return;
    (index ? setRight : setLeft)(id);
    setLinks([]);
    setPicks(["", ""]);
    setOutcome("Undecided");
    setRationale("");
    change();
  };
  const selected = picks.map((id, i) =>
    docs[i]?.messages.find((m: any) => m.id === id),
  );
  const save = () =>
    act(async () => {
      setBusy(true);
      try {
        const s = await api(
          savedId
            ? "/comparisons/" + savedId
            : "/w/" + workspace + "/comparisons",
          {
            title: question.trim(),
            question: question.trim(),
            journeyId: jid || null,
            leftId: left,
            rightId: right,
            links: JSON.stringify(links),
            outcome,
            rationale,
            ranges: JSON.stringify(ranges),
          },
          savedId ? "PUT" : "POST",
        );
        setSavedId(s.id);
        setDirty(false);
        await loadSessions();
        notify(
          t(
            jid
              ? "Comparison saved to journey"
              : "Comparison saved in workspace",
          ),
        );
      } finally {
        setBusy(false);
      }
    });
  const jump = (link: Finding) => {
    setView("sources");
    setRanges([
      [1, 999],
      [1, 999],
    ]);
    setPicks([link.left, link.right]);
    setTimeout(() => {
      [link.left, link.right].forEach((id, i) =>
        document
          .querySelector("#compare-" + i + " #message-" + id)
          ?.scrollIntoView({ block: "center", behavior: "smooth" }),
      );
    }, 60);
  };
  if (conversations < 2)
    return (
      <section className="page">
        <span className="eyebrow">
          {t("TURN DIFFERENT ANSWERS INTO A DECISION")}
        </span>
        <h1>{t("Compare approaches")}</h1>
        <p>
          {t(
            "Import at least two conversations, then compare their evidence and record which approach fits your project.",
          )}
        </p>
        <a className="button primary" href="#library">
          {t("Open conversation library")}
        </a>
      </section>
    );
  return (
    <div className="comparison decision-comparison">
      <div className="page-heading">
        <div>
          <span className="eyebrow">
            {t("TURN DIFFERENT ANSWERS INTO A DECISION")}
          </span>
          <h1>{t("Compare approaches")}</h1>
          <p className="subtitle">
            {t(
              "What differs, why it matters, and which approach you’ll use. No journey required.",
            )}
          </p>
        </div>
        <div className="row">
          <select
            aria-label={t("Saved comparisons")}
            value={savedId}
            onChange={(e) => load(e.target.value)}
          >
            <option value="">{t("New comparison")}</option>
            {sessions.map((s) => (
              <option value={s.id} key={s.id}>
                {s.title}
              </option>
            ))}
          </select>
          <button title={t("New comparison")} onClick={() => load("")}>
            <Plus size={16} />
          </button>
        </div>
      </div>
      <div className="comparison-purpose">
        <label>
          {t("What are you deciding?")}
          <input
            placeholder={t(
              "e.g. Should we run emulators in the browser or stream from a server?",
            )}
            value={question}
            onChange={(e) => {
              setQuestion(e.target.value);
              change();
            }}
          />
        </label>
        <div className="comparison-guide">
          <span>
            <b>1</b> {t("Choose two conversations")}
          </span>
          <ArrowRight size={13} />
          <span>
            <b>2</b> {t("Pair evidence & explain differences")}
          </span>
          <ArrowRight size={13} />
          <span>
            <b>3</b> {t("Record your decision")}
          </span>
        </div>
      </div>
      <div className="comparison-tabs">
        <button
          className={view === "sources" ? "active" : ""}
          onClick={() => setView("sources")}
        >
          {t("1. Read & collect evidence")}
        </button>
        <button
          className={view === "findings" ? "active" : ""}
          onClick={() => setView("findings")}
        >
          {t("2. Findings & decision")}{" "}
          <span className="count">{formatNumber(links.length)}</span>
        </button>
        <span className="spacer" />
        <small>
          {dirty
            ? t("Unsaved changes")
            : savedId
              ? t("Saved comparison")
              : t("Draft comparison")}
        </small>
      </div>
      {view === "sources" ? (
        <>
          <div className="compare-controls">
            {[left, right].map((v, i) => (
              <div key={i}>
                <label className="source-choice">
                  {i ? t("B · Second approach") : t("A · First approach")}
                  <input
                    placeholder={t("Search conversations, messages, code, comments…")}
                    value={searchQ}
                    onChange={(e) => setSearchQ(e.target.value)}
                  />
                  <select
                    aria-label={
                      i ? t("Right conversation") : t("Left conversation")
                    }
                    value={v}
                    onChange={(e) => pickConversation(i, e.target.value)}
                  >
                    <option value="">{t("Choose conversation…")}</option>
                    {[
                      ...search.items,
                      ...docs.filter(
                        (d) => d && !search.items.some((c) => c.id === d.id),
                      ),
                    ].map((c) => (
                      <option
                        value={c.id}
                        key={c.id}
                        disabled={c.id === (i ? left : right)}
                      >
                        {c.provider} · {c.title}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  {t("Messages")}
                  <input
                    aria-label={t("First message {index}", { index: i + 1 })}
                    type="number"
                    min={1}
                    value={ranges[i][0]}
                    onChange={(e) => {
                      setRanges((rs) =>
                        rs.map((r, n) =>
                          n === i
                            ? [
                                Number(e.target.value),
                                Math.max(Number(e.target.value), r[1]),
                              ]
                            : r,
                        ),
                      );
                      change();
                    }}
                  />
                  {t("to")}
                  <input
                    aria-label={t("Last message {index}", { index: i + 1 })}
                    type="number"
                    min={ranges[i][0]}
                    value={ranges[i][1] === 999 ? "" : ranges[i][1]}
                    placeholder={t("all")}
                    onChange={(e) => {
                      setRanges((rs) =>
                        rs.map((r, n) =>
                          n === i
                            ? [
                                r[0],
                                Math.max(r[0], Number(e.target.value) || 999),
                              ]
                            : r,
                        ),
                      );
                      change();
                    }}
                  />
                </label>
              </div>
            ))}
          </div>
          <div className="compare-readers">
            {docs.length === 0 ? (
              <div className="loading">
                {t(
                  "Choose two conversations to inspect their original evidence.",
                )}
              </div>
            ) : (
              docs.map((c, i) => (
                <div className="compare-reader" id={"compare-" + i} key={c.id}>
                  <div className="source-notice">
                    <Badge provider={c.provider} />
                    {c.demo ? t("Demonstration source") : t("Original source")}
                    <span>{t("Scroll independently")}</span>
                  </div>
                  {[...c.messages]
                    .sort((a: any, b: any) => a.order - b.order)
                    .filter(
                      (m: any) =>
                        m.order + 1 >= ranges[i][0] &&
                        m.order + 1 <= ranges[i][1],
                    )
                    .map((m: any) => (
                      <div
                        className={picks[i] === m.id ? "comparison-picked" : ""}
                        key={m.id}
                      >
                        <Message
                          message={m}
                          onLink={() =>
                            setPicks((p) =>
                              p.map((x, n) => (n === i ? m.id : x)),
                            )
                          }
                        />
                        <a
                          className="source-link"
                          href={"#explorer/" + c.id + "?message=" + m.id}
                        >
                          {t("Open original message ↗")}
                        </a>
                      </div>
                    ))}
                </div>
              ))
            )}
          </div>
          <div className="evidence-composer">
            <div className="evidence-picks">
              {selected.map((m, i) => (
                <div key={i}>
                  <strong>
                    {i ? "B" : "A"} ·{" "}
                    {m ? t("Evidence selected") : t("Select a message above")}
                  </strong>
                  <p>
                    {m
                      ? excerpt(m).slice(0, 150)
                      : t("Use “Use as evidence” on a relevant response.")}
                  </p>
                </div>
              ))}
            </div>
            <div className="row">
              <select
                aria-label={t("Comparison criterion")}
                value={criterion}
                onChange={(e) => setCriterion(e.target.value)}
              >
                {[
                  "Approach",
                  "Tradeoff",
                  "Risk",
                  "Cost",
                  "Performance",
                  "Implementation",
                  "Agreement",
                ].map((c) => (
                  <option key={c} value={c}>
                    {t(c)}
                  </option>
                ))}
              </select>
              <input
                aria-label={t("Finding")}
                placeholder={t(
                  "Explain the difference: A avoids server cost; B supports more games…",
                )}
                value={note}
                onChange={(e) => setNote(e.target.value)}
              />
              <button
                className="primary"
                disabled={selected.some((m) => !m) || !note.trim()}
                onClick={() => {
                  setLinks((s) => [
                    ...s,
                    {
                      left: picks[0],
                      right: picks[1],
                      criterion,
                      note: note.trim(),
                    },
                  ]);
                  setPicks(["", ""]);
                  setNote("");
                  change();
                  notify(
                    t("Finding added. Open Findings & decision to review it."),
                  );
                }}
              >
                <Link2 size={15} />
                {t("Add finding")}
              </button>
              <button onClick={() => setView("findings")}>
                {t("Review findings")} <ArrowRight size={14} />
              </button>
            </div>
          </div>
        </>
      ) : (
        <div className="decision-workspace">
          <section>
            <h2>{t("Evidence behind the choice")}</h2>
            <p>
              {t(
                "These are your observations, linked to the original messages. No automatic model ranking or inferred matches.",
              )}
            </p>
            {links.length === 0 ? (
              <div className="comparison-empty">
                <Link2 />
                <h3>{t("Give the comparison a useful takeaway")}</h3>
                <p>
                  {t(
                    "Pick a response from each conversation, then explain a concrete difference in approach, risk, cost, or implementation.",
                  )}
                </p>
                <button onClick={() => setView("sources")}>
                  {t("Collect evidence")}
                </button>
              </div>
            ) : (
              links.map((l, i) => (
                <article className="finding-card" key={i}>
                  <div className="row">
                    <span className="type-tag">
                      {t(l.criterion || "Finding")}
                    </span>
                    <strong>{l.note}</strong>
                    <span className="spacer" />
                    <button
                      aria-label={t("Remove finding {index}", { index: i + 1 })}
                      onClick={() => {
                        setLinks((ls) => ls.filter((_, n) => n !== i));
                        change();
                      }}
                    >
                      <X size={14} />
                    </button>
                  </div>
                  <div className="finding-quotes">
                    {[l.left, l.right].map((id, n) => (
                      <blockquote key={n}>
                        <small>
                          {n ? "B" : "A"} · {docs[n]?.provider}
                        </small>
                        <p>
                          {excerpt(
                            docs[n]?.messages.find((m: any) => m.id === id),
                          ).slice(0, 700)}
                        </p>
                        <a
                          href={
                            "#explorer/" + (n ? right : left) + "?message=" + id
                          }
                        >
                          {t("Read source ↗")}
                        </a>
                      </blockquote>
                    ))}
                  </div>
                  <button onClick={() => jump(l)}>
                    {t("Inspect both source messages")}
                  </button>
                </article>
              ))
            )}
          </section>
          <aside className="decision-record">
            <span className="eyebrow">{t("YOUR TAKEAWAY")}</span>
            <h2>{t("Record the decision")}</h2>
            <label>
              {t("Chosen approach")}
              <select
                value={outcome}
                onChange={(e) => {
                  setOutcome(e.target.value);
                  change();
                }}
              >
                <option value="Undecided">{t("Still evaluating")}</option>
                <option value="Left">
                  {t("Use A —")} {docs[0]?.title || t("first approach")}
                </option>
                <option value="Right">
                  {t("Use B —")} {docs[1]?.title || t("second approach")}
                </option>
                <option value="Combine">{t("Combine both approaches")}</option>
                <option value="Neither">{t("Choose neither")}</option>
              </select>
            </label>
            <label>
              {t("Why this choice?")}
              <textarea
                rows={6}
                value={rationale}
                onChange={(e) => {
                  setRationale(e.target.value);
                  change();
                }}
                placeholder={t(
                  "Explain the deciding tradeoff, what you’re keeping, and what still needs validation.",
                )}
              />
            </label>
            <p>{count("evidence", links.length)}</p>
            <small>
              {t("Curator decision · Original responses remain unchanged.")}
            </small>
          </aside>
        </div>
      )}
      <footer className="comparison-savebar">
        <div>
          <strong>
            {question || t("Name the question you’re deciding above")}
          </strong>
          <small>
            {outcome === "Undecided"
              ? t("Draft decision")
              : outcome === "Combine"
                ? t("Decision: combine approaches")
                : outcome === "Neither"
                  ? t("Decision: neither approach")
                  : t("Decision: approach {choice}", {
                      choice: outcome === "Left" ? "A" : "B",
                    })}{" "}
            · {count("findings", links.length)}
          </small>
        </div>
        <span className="spacer" />
        <select
          aria-label={t("Comparison destination")}
          value={jid}
          onChange={(e) => {
            setJid(e.target.value);
            change();
          }}
        >
          <option value="">{t("Workspace only · no journey")}</option>
          {journeys.map((j) => (
            <option value={j.id} key={j.id}>
              {j.title}
            </option>
          ))}
        </select>
        <button
          className="primary"
          disabled={
            !canEdit ||
            busy ||
            !question.trim() ||
            !left ||
            !right ||
            left === right ||
            (outcome !== "Undecided" && !rationale.trim())
          }
          onClick={save}
        >
          <Save size={15} />
          {busy ? t("Saving…") : t("Save comparison")}
        </button>
      </footer>
    </div>
  );
}
