import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { MessageCircle, Lock, ArrowRight } from "lucide-react";
import { api, initials } from "../../shared/state";
import { t, useLocale, formatDate, formatNumber, count, } from "../../shared/i18n/i18n";
import "./discussion.css";
export function DiscussionToggle({ enabled, save, disabled = false, }) {
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState("");
    return (_jsxs("div", { className: "discussion-setting", children: [_jsxs("label", { className: "discussion-switch", children: [_jsx("span", { children: t("Allow comments & questions") }), _jsx("input", { type: "checkbox", role: "switch", checked: enabled, disabled: disabled || busy, onChange: async (e) => {
                            setBusy(true);
                            setError("");
                            try {
                                await save(e.target.checked);
                            }
                            catch (e) {
                                setError(e.message);
                            }
                            finally {
                                setBusy(false);
                            }
                        } })] }), _jsx("small", { children: t("Only signed-in, registered users can post or reply.") }), error && _jsx("p", { role: "alert", children: t(error) })] }));
}
export function Discussion({ slug }) {
    useLocale();
    const [data, setData] = useState(null);
    const [error, setError] = useState("");
    const [body, setBody] = useState("");
    const [kind, setKind] = useState("Comment");
    const [replyTo, setReplyTo] = useState(null);
    const [busy, setBusy] = useState(false);
    const [filter, setFilter] = useState("All");
    const [page, setPage] = useState(0);
    const endpoint = "/public/" + encodeURIComponent(slug) + "/discussion";
    const refresh = async (pageNumber = page) => {
        const next = await api(endpoint +
            "?page=" +
            pageNumber +
            "&kind=" +
            (filter === "All" ? "" : filter));
        setData(next);
        return next;
    };
    useEffect(() => {
        let active = true;
        setData(null);
        setError("");
        api(endpoint + "?page=" + page + "&kind=" + (filter === "All" ? "" : filter))
            .then((next) => {
            if (active)
                setData(next);
        })
            .catch((e) => {
            if (active)
                setError(e.message);
        });
        return () => {
            active = false;
        };
    }, [endpoint, page, filter]);
    const authLink = (register) => "#" +
        (register ? "register" : "login") +
        "?returnTo=" +
        encodeURIComponent("public/" + slug);
    const renderEntry = (entry, reply = false) => (_jsxs("article", { className: "discussion-entry " + (reply ? "discussion-reply" : ""), children: [_jsx("span", { className: "avatar", children: initials(entry.author) }), _jsxs("div", { className: "discussion-entry-content", children: [_jsxs("header", { children: [_jsx("strong", { children: entry.author }), entry.isSharer && (_jsx("span", { className: "discussion-pill", children: t("Sharer") })), !reply && _jsx("span", { className: "discussion-pill", children: t(entry.kind) }), _jsx("time", { dateTime: entry.createdAt, children: formatDate(entry.createdAt) })] }), _jsx("p", { children: entry.body }), !reply && data?.user && (_jsx("button", { className: "discussion-reply-button", onClick: () => {
                            setReplyTo(entry.id);
                            document.getElementById("discussion-body")?.focus();
                        }, children: t("Reply") })), !reply &&
                        data?.entries
                            .filter((child) => child.parentId === entry.id)
                            .map((child) => renderEntry(child, true))] })] }, entry.id));
    return (_jsxs("section", { className: "discussion", id: "discussion", tabIndex: -1, "aria-label": t("Comments & questions"), children: [_jsxs("div", { className: "section-title", children: [_jsxs("h2", { children: [_jsx(MessageCircle, { size: 22 }), t("Comments & questions")] }), data?.enabled && (_jsx("small", { children: count("discussionThreads", data.total) }))] }), _jsx("p", { children: t("Talk about the work. Ask how it happened.") }), data?.canManage && (_jsx(DiscussionToggle, { enabled: data.enabled, save: async (enabled) => {
                    await api(endpoint, { enabled }, "PATCH");
                    await refresh();
                } })), error && (_jsxs("p", { role: "alert", className: "discussion-error", children: [t(error), " ", _jsx("button", { onClick: () => {
                            setError("");
                            void refresh().catch((e) => setError(e.message));
                        }, children: t("Retry") })] })), !data && !error && _jsx("p", { role: "status", children: t("Loading discussion…") }), data && !data.enabled && (_jsxs("div", { className: "discussion-closed", children: [_jsx(Lock, { size: 22 }), _jsxs("div", { children: [_jsx("h3", { children: t("Discussion is turned off") }), _jsx("p", { children: t("The person sharing has not enabled comments or questions.") })] })] })), data?.enabled && (_jsxs(_Fragment, { children: [data.user ? (_jsxs("form", { className: "discussion-composer", onSubmit: async (e) => {
                            e.preventDefault();
                            if (busy || !body.trim())
                                return;
                            setBusy(true);
                            setError("");
                            try {
                                await api(endpoint, { body, kind, parentId: replyTo });
                                setBody("");
                                setReplyTo(null);
                                if (page !== 0 && !replyTo)
                                    setPage(0);
                                else
                                    await refresh();
                            }
                            catch (e) {
                                setError(e.message);
                                await refresh().catch(() => { });
                            }
                            finally {
                                setBusy(false);
                            }
                        }, children: [_jsxs("div", { className: "row", children: [_jsx("span", { className: "avatar", children: initials(data.user.name) }), _jsx("strong", { children: data.user.name }), !replyTo && (_jsx("div", { className: "discussion-tabs", role: "group", "aria-label": t("Post type"), children: ["Comment", "Question"].map((value) => (_jsx("button", { type: "button", "aria-pressed": kind === value, onClick: () => setKind(value), children: t(value) }, value))) }))] }), replyTo && (_jsxs("div", { className: "discussion-reply-context", children: [t("Replying to {name}", {
                                        name: data.entries.find((e) => e.id === replyTo)?.author || "",
                                    }), _jsx("button", { type: "button", onClick: () => setReplyTo(null), children: t("Cancel") })] })), _jsx("label", { htmlFor: "discussion-body", children: t(replyTo ? "Reply" : kind) }), _jsx("textarea", { id: "discussion-body", value: body, onChange: (e) => setBody(e.target.value), required: true, maxLength: 4000, placeholder: t("What would you like to share or ask?"), disabled: busy }), _jsxs("footer", { children: [_jsx("small", { children: t("Your name and contribution will be visible to anyone with this link.") }), _jsxs("button", { className: "primary", disabled: busy || !body.trim(), children: [t(busy
                                                ? "Posting…"
                                                : replyTo
                                                    ? "Post reply"
                                                    : kind === "Question"
                                                        ? "Post question"
                                                        : "Post comment"), _jsx(ArrowRight, { size: 15 })] })] })] })) : (_jsxs("div", { className: "discussion-guest", children: [_jsx("h3", { children: t("Join the conversation") }), _jsx("p", { children: t("Sign in or create an account to comment or ask a question.") }), _jsxs("div", { className: "row", children: [_jsx("a", { className: "button primary", href: authLink(false), children: t("Sign in") }), _jsx("a", { className: "button", href: authLink(true), children: t("Create account") })] })] })), _jsx("div", { className: "discussion-tabs", role: "group", "aria-label": t("Filter discussion"), children: ["All", "Question", "Comment"].map((value) => (_jsx("button", { "aria-pressed": filter === value, onClick: () => {
                                setPage(0);
                                setReplyTo(null);
                                setFilter(value);
                            }, children: t(value === "Question"
                                ? "Questions"
                                : value === "Comment"
                                    ? "Comments"
                                    : "All") }, value))) }), data.entries
                        .filter((e) => !e.parentId && (filter === "All" || filter === e.kind))
                        .reverse()
                        .map((e) => renderEntry(e)), !data.entries.some((e) => !e.parentId && (filter === "All" || filter === e.kind)) && (_jsx("p", { className: "discussion-empty", children: t("No contributions yet in this view.") })), (page > 0 || data.total > 20) && (_jsxs("div", { className: "row", children: [_jsx("button", { disabled: page === 0, onClick: () => setPage((p) => p - 1), children: t("Previous") }), _jsxs("small", { children: [formatNumber(page + 1), " /", " ", formatNumber(Math.max(1, Math.ceil(data.total / 20)))] }), _jsx("button", { disabled: (page + 1) * 20 >= data.total, onClick: () => setPage((p) => p + 1), children: t("Next") })] }))] }))] }));
}
