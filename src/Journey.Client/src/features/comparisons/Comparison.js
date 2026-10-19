import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { t, useLocale, formatNumber, count } from "../../shared/i18n/i18n";
import { useEffect, useState } from "react";
import { ArrowRight, Link2, Plus, Save, X } from "lucide-react";
import { api, act, useApp, useLibrary, useJourneyOptions, loadConversation } from "../../shared/state";
import { Badge } from "../../shared/ui/ui";
import { Message } from "../conversations/Explorer";
const excerpt = (m) => m?.blocks
    ?.filter((b) => ["text", "markdown"].includes(b.kind))
    .map((b) => b.text)
    .join("\n") || t("Structured content — open the source to inspect.");
export function Comparison({ conversations, canEdit, }) {
    useLocale();
    const params = new URLSearchParams(location.hash.split("?")[1]);
    const [left, setLeft] = useState(params.get("left") || "");
    const [right, setRight] = useState("");
    const [docs, setDocs] = useState([]);
    const [sessions, setSessions] = useState([]);
    const [savedId, setSavedId] = useState("");
    const [question, setQuestion] = useState("");
    const [links, setLinks] = useState([]);
    const [picks, setPicks] = useState(["", ""]);
    const [note, setNote] = useState("");
    const [criterion, setCriterion] = useState("Approach");
    const [jid, setJid] = useState("");
    const [outcome, setOutcome] = useState("Undecided");
    const [rationale, setRationale] = useState("");
    const [view, setView] = useState("sources");
    const [dirty, setDirty] = useState(false);
    const [busy, setBusy] = useState(false);
    const [ranges, setRanges] = useState([
        [1, 999],
        [1, 999],
    ]);
    const { workspace, notify } = useApp();
    const journeys = useJourneyOptions(workspace);
    const [searchQ, setSearchQ] = useState("");
    const search = useLibrary(workspace, { q: searchQ, limit: 40 });
    useEffect(() => {
        if (!left && search.items.length)
            setLeft(search.items[0].id);
        if (!right && search.items.length)
            setRight(search.items.find((c) => c.id !== (left || search.items[0].id))?.id ||
                "");
    }, [search.items, left]);
    useEffect(() => {
        let live = true;
        setDocs([]);
        if (left && right)
            loadConversation(left)
                .then(async (l) => {
                const r = await loadConversation(right);
                if (live)
                    setDocs([l, r]);
            })
                .catch((e) => notify(e.message));
        return () => {
            live = false;
        };
    }, [left, right]);
    const loadSessions = () => act(async () => {
        const page = await api("/w/" + workspace + "/comparisons");
        setSessions(page.items);
    });
    useEffect(() => {
        loadSessions();
    }, [workspace]);
    useEffect(() => {
        const handler = (e) => {
            if (dirty)
                e.preventDefault();
        };
        window.addEventListener("beforeunload", handler);
        return () => window.removeEventListener("beforeunload", handler);
    }, [dirty]);
    const change = () => setDirty(true);
    const load = (id) => {
        if (dirty && !confirm(t("Discard unsaved comparison changes?")))
            return;
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
    const pickConversation = (index, id) => {
        if (links.length &&
            !confirm(t("Changing a source clears its evidence findings and decision. Continue?")))
            return;
        (index ? setRight : setLeft)(id);
        setLinks([]);
        setPicks(["", ""]);
        setOutcome("Undecided");
        setRationale("");
        change();
    };
    const selected = picks.map((id, i) => docs[i]?.messages.find((m) => m.id === id));
    const save = () => act(async () => {
        setBusy(true);
        try {
            const s = await api(savedId
                ? "/comparisons/" + savedId
                : "/w/" + workspace + "/comparisons", {
                title: question.trim(),
                question: question.trim(),
                journeyId: jid || null,
                leftId: left,
                rightId: right,
                links: JSON.stringify(links),
                outcome,
                rationale,
                ranges: JSON.stringify(ranges),
            }, savedId ? "PUT" : "POST");
            setSavedId(s.id);
            setDirty(false);
            await loadSessions();
            notify(t(jid
                ? "Comparison saved to journey"
                : "Comparison saved in workspace"));
        }
        finally {
            setBusy(false);
        }
    });
    const jump = (link) => {
        setView("sources");
        setRanges([
            [1, 999],
            [1, 999],
        ]);
        setPicks([link.left, link.right]);
        setTimeout(() => {
            [link.left, link.right].forEach((id, i) => document
                .querySelector("#compare-" + i + " #message-" + id)
                ?.scrollIntoView({ block: "center", behavior: "smooth" }));
        }, 60);
    };
    if (conversations < 2)
        return (_jsxs("section", { className: "page", children: [_jsx("span", { className: "eyebrow", children: t("TURN DIFFERENT ANSWERS INTO A DECISION") }), _jsx("h1", { children: t("Compare approaches") }), _jsx("p", { children: t("Import at least two conversations, then compare their evidence and record which approach fits your project.") }), _jsx("a", { className: "button primary", href: "#library", children: t("Open conversation library") })] }));
    return (_jsxs("div", { className: "comparison decision-comparison", children: [_jsxs("div", { className: "page-heading", children: [_jsxs("div", { children: [_jsx("span", { className: "eyebrow", children: t("TURN DIFFERENT ANSWERS INTO A DECISION") }), _jsx("h1", { children: t("Compare approaches") }), _jsx("p", { className: "subtitle", children: t("What differs, why it matters, and which approach you’ll use. No journey required.") })] }), _jsxs("div", { className: "row", children: [_jsxs("select", { "aria-label": t("Saved comparisons"), value: savedId, onChange: (e) => load(e.target.value), children: [_jsx("option", { value: "", children: t("New comparison") }), sessions.map((s) => (_jsx("option", { value: s.id, children: s.title }, s.id)))] }), _jsx("button", { title: t("New comparison"), onClick: () => load(""), children: _jsx(Plus, { size: 16 }) })] })] }), _jsxs("div", { className: "comparison-purpose", children: [_jsxs("label", { children: [t("What are you deciding?"), _jsx("input", { placeholder: t("e.g. Should we run emulators in the browser or stream from a server?"), value: question, onChange: (e) => {
                                    setQuestion(e.target.value);
                                    change();
                                } })] }), _jsxs("div", { className: "comparison-guide", children: [_jsxs("span", { children: [_jsx("b", { children: "1" }), " ", t("Choose two conversations")] }), _jsx(ArrowRight, { size: 13 }), _jsxs("span", { children: [_jsx("b", { children: "2" }), " ", t("Pair evidence & explain differences")] }), _jsx(ArrowRight, { size: 13 }), _jsxs("span", { children: [_jsx("b", { children: "3" }), " ", t("Record your decision")] })] })] }), _jsxs("div", { className: "comparison-tabs", children: [_jsx("button", { className: view === "sources" ? "active" : "", onClick: () => setView("sources"), children: t("1. Read & collect evidence") }), _jsxs("button", { className: view === "findings" ? "active" : "", onClick: () => setView("findings"), children: [t("2. Findings & decision"), " ", _jsx("span", { className: "count", children: formatNumber(links.length) })] }), _jsx("span", { className: "spacer" }), _jsx("small", { children: dirty
                            ? t("Unsaved changes")
                            : savedId
                                ? t("Saved comparison")
                                : t("Draft comparison") })] }), view === "sources" ? (_jsxs(_Fragment, { children: [_jsx("div", { className: "compare-controls", children: [left, right].map((v, i) => (_jsxs("div", { children: [_jsxs("label", { className: "source-choice", children: [i ? t("B · Second approach") : t("A · First approach"), _jsx("input", { placeholder: t("Search conversations, messages, code, comments…"), value: searchQ, onChange: (e) => setSearchQ(e.target.value) }), _jsxs("select", { "aria-label": i ? t("Right conversation") : t("Left conversation"), value: v, onChange: (e) => pickConversation(i, e.target.value), children: [_jsx("option", { value: "", children: t("Choose conversation…") }), [
                                                    ...search.items,
                                                    ...docs.filter((d) => d && !search.items.some((c) => c.id === d.id)),
                                                ].map((c) => (_jsxs("option", { value: c.id, disabled: c.id === (i ? left : right), children: [c.provider, " \u00B7 ", c.title] }, c.id)))] })] }), _jsxs("label", { children: [t("Messages"), _jsx("input", { "aria-label": t("First message {index}", { index: i + 1 }), type: "number", min: 1, value: ranges[i][0], onChange: (e) => {
                                                setRanges((rs) => rs.map((r, n) => n === i
                                                    ? [
                                                        Number(e.target.value),
                                                        Math.max(Number(e.target.value), r[1]),
                                                    ]
                                                    : r));
                                                change();
                                            } }), t("to"), _jsx("input", { "aria-label": t("Last message {index}", { index: i + 1 }), type: "number", min: ranges[i][0], value: ranges[i][1] === 999 ? "" : ranges[i][1], placeholder: t("all"), onChange: (e) => {
                                                setRanges((rs) => rs.map((r, n) => n === i
                                                    ? [
                                                        r[0],
                                                        Math.max(r[0], Number(e.target.value) || 999),
                                                    ]
                                                    : r));
                                                change();
                                            } })] })] }, i))) }), _jsx("div", { className: "compare-readers", children: docs.length === 0 ? (_jsx("div", { className: "loading", children: t("Choose two conversations to inspect their original evidence.") })) : (docs.map((c, i) => (_jsxs("div", { className: "compare-reader", id: "compare-" + i, children: [_jsxs("div", { className: "source-notice", children: [_jsx(Badge, { provider: c.provider }), c.demo ? t("Demonstration source") : t("Original source"), _jsx("span", { children: t("Scroll independently") })] }), [...c.messages]
                                    .sort((a, b) => a.order - b.order)
                                    .filter((m) => m.order + 1 >= ranges[i][0] &&
                                    m.order + 1 <= ranges[i][1])
                                    .map((m) => (_jsxs("div", { className: picks[i] === m.id ? "comparison-picked" : "", children: [_jsx(Message, { message: m, onLink: () => setPicks((p) => p.map((x, n) => (n === i ? m.id : x))) }), _jsx("a", { className: "source-link", href: "#explorer/" + c.id + "?message=" + m.id, children: t("Open original message ↗") })] }, m.id)))] }, c.id)))) }), _jsxs("div", { className: "evidence-composer", children: [_jsx("div", { className: "evidence-picks", children: selected.map((m, i) => (_jsxs("div", { children: [_jsxs("strong", { children: [i ? "B" : "A", " \u00B7", " ", m ? t("Evidence selected") : t("Select a message above")] }), _jsx("p", { children: m
                                                ? excerpt(m).slice(0, 150)
                                                : t("Use “Use as evidence” on a relevant response.") })] }, i))) }), _jsxs("div", { className: "row", children: [_jsx("select", { "aria-label": t("Comparison criterion"), value: criterion, onChange: (e) => setCriterion(e.target.value), children: [
                                            "Approach",
                                            "Tradeoff",
                                            "Risk",
                                            "Cost",
                                            "Performance",
                                            "Implementation",
                                            "Agreement",
                                        ].map((c) => (_jsx("option", { value: c, children: t(c) }, c))) }), _jsx("input", { "aria-label": t("Finding"), placeholder: t("Explain the difference: A avoids server cost; B supports more games…"), value: note, onChange: (e) => setNote(e.target.value) }), _jsxs("button", { className: "primary", disabled: selected.some((m) => !m) || !note.trim(), onClick: () => {
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
                                            notify(t("Finding added. Open Findings & decision to review it."));
                                        }, children: [_jsx(Link2, { size: 15 }), t("Add finding")] }), _jsxs("button", { onClick: () => setView("findings"), children: [t("Review findings"), " ", _jsx(ArrowRight, { size: 14 })] })] })] })] })) : (_jsxs("div", { className: "decision-workspace", children: [_jsxs("section", { children: [_jsx("h2", { children: t("Evidence behind the choice") }), _jsx("p", { children: t("These are your observations, linked to the original messages. No automatic model ranking or inferred matches.") }), links.length === 0 ? (_jsxs("div", { className: "comparison-empty", children: [_jsx(Link2, {}), _jsx("h3", { children: t("Give the comparison a useful takeaway") }), _jsx("p", { children: t("Pick a response from each conversation, then explain a concrete difference in approach, risk, cost, or implementation.") }), _jsx("button", { onClick: () => setView("sources"), children: t("Collect evidence") })] })) : (links.map((l, i) => (_jsxs("article", { className: "finding-card", children: [_jsxs("div", { className: "row", children: [_jsx("span", { className: "type-tag", children: t(l.criterion || "Finding") }), _jsx("strong", { children: l.note }), _jsx("span", { className: "spacer" }), _jsx("button", { "aria-label": t("Remove finding {index}", { index: i + 1 }), onClick: () => {
                                                    setLinks((ls) => ls.filter((_, n) => n !== i));
                                                    change();
                                                }, children: _jsx(X, { size: 14 }) })] }), _jsx("div", { className: "finding-quotes", children: [l.left, l.right].map((id, n) => (_jsxs("blockquote", { children: [_jsxs("small", { children: [n ? "B" : "A", " \u00B7 ", docs[n]?.provider] }), _jsx("p", { children: excerpt(docs[n]?.messages.find((m) => m.id === id)).slice(0, 700) }), _jsx("a", { href: "#explorer/" + (n ? right : left) + "?message=" + id, children: t("Read source ↗") })] }, n))) }), _jsx("button", { onClick: () => jump(l), children: t("Inspect both source messages") })] }, i))))] }), _jsxs("aside", { className: "decision-record", children: [_jsx("span", { className: "eyebrow", children: t("YOUR TAKEAWAY") }), _jsx("h2", { children: t("Record the decision") }), _jsxs("label", { children: [t("Chosen approach"), _jsxs("select", { value: outcome, onChange: (e) => {
                                            setOutcome(e.target.value);
                                            change();
                                        }, children: [_jsx("option", { value: "Undecided", children: t("Still evaluating") }), _jsxs("option", { value: "Left", children: [t("Use A —"), " ", docs[0]?.title || t("first approach")] }), _jsxs("option", { value: "Right", children: [t("Use B —"), " ", docs[1]?.title || t("second approach")] }), _jsx("option", { value: "Combine", children: t("Combine both approaches") }), _jsx("option", { value: "Neither", children: t("Choose neither") })] })] }), _jsxs("label", { children: [t("Why this choice?"), _jsx("textarea", { rows: 6, value: rationale, onChange: (e) => {
                                            setRationale(e.target.value);
                                            change();
                                        }, placeholder: t("Explain the deciding tradeoff, what you’re keeping, and what still needs validation.") })] }), _jsx("p", { children: count("evidence", links.length) }), _jsx("small", { children: t("Curator decision · Original responses remain unchanged.") })] })] })), _jsxs("footer", { className: "comparison-savebar", children: [_jsxs("div", { children: [_jsx("strong", { children: question || t("Name the question you’re deciding above") }), _jsxs("small", { children: [outcome === "Undecided"
                                        ? t("Draft decision")
                                        : outcome === "Combine"
                                            ? t("Decision: combine approaches")
                                            : outcome === "Neither"
                                                ? t("Decision: neither approach")
                                                : t("Decision: approach {choice}", {
                                                    choice: outcome === "Left" ? "A" : "B",
                                                }), " ", "\u00B7 ", count("findings", links.length)] })] }), _jsx("span", { className: "spacer" }), _jsxs("select", { "aria-label": t("Comparison destination"), value: jid, onChange: (e) => {
                            setJid(e.target.value);
                            change();
                        }, children: [_jsx("option", { value: "", children: t("Workspace only · no journey") }), journeys.map((j) => (_jsx("option", { value: j.id, children: j.title }, j.id)))] }), _jsxs("button", { className: "primary", disabled: !canEdit ||
                            busy ||
                            !question.trim() ||
                            !left ||
                            !right ||
                            left === right ||
                            (outcome !== "Undecided" && !rationale.trim()), onClick: save, children: [_jsx(Save, { size: 15 }), busy ? t("Saving…") : t("Save comparison")] })] })] }));
}
