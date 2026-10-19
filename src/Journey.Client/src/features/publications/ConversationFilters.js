import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { Fragment, useId, useMemo, useState } from "react";
import { ChevronDown, ChevronUp, Filter } from "lucide-react";
import { t } from "../../shared/i18n/i18n";
import { threadTags } from "../review/annotationParsing";
import "./conversation-filters.css";
export function ConversationFilters({ messages, annotations = [], renderMessage, }) {
    const [open, setOpen] = useState(false);
    const [search, setSearch] = useState("");
    const [role, setRole] = useState("");
    const [topic, setTopic] = useState("");
    const [annotated, setAnnotated] = useState(false);
    const panelId = useId();
    const rows = useMemo(() => {
        const byMessage = new Map();
        for (const annotation of annotations) {
            if (!annotation.messageId)
                continue;
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
    const active = Number(!!query) + Number(!!role) + Number(!!topic) + Number(annotated);
    const matching = rows
        .map((row, index) => ({ ...row, index }))
        .filter((row) => (!query || row.text.includes(query)) &&
        (!role || row.message.role === role) &&
        (!topic || row.tags.includes(topic)) &&
        (!annotated || row.annotated));
    const clear = () => {
        setSearch("");
        setRole("");
        setTopic("");
        setAnnotated(false);
    };
    return (_jsxs(_Fragment, { children: [_jsxs("section", { className: "conversation-filters", "aria-label": t("Conversation filters"), children: [_jsxs("div", { className: "conversation-filter-toolbar", children: [_jsxs("button", { type: "button", "aria-expanded": open, "aria-controls": panelId, onClick: () => setOpen((value) => !value), children: [_jsx(Filter, { size: 15, "aria-hidden": "true" }), t("Filter"), !!active && (_jsx("span", { className: "filter-count", "aria-label": t("{count} active filters", { count: active }), children: active })), open ? (_jsx(ChevronUp, { size: 15, "aria-hidden": "true" })) : (_jsx(ChevronDown, { size: 15, "aria-hidden": "true" }))] }), !!active && (_jsxs("div", { className: "conversation-filter-status", children: [_jsx("span", { role: "status", children: t("{shown} of {total} messages", {
                                            shown: matching.length,
                                            total: messages.length,
                                        }) }), _jsx("button", { type: "button", onClick: clear, children: t("Clear filters") })] }))] }), _jsxs("div", { id: panelId, hidden: !open, className: "conversation-filter-panel", children: [_jsxs("div", { className: "conversation-filter-fields", children: [_jsxs("label", { children: [t("Search conversation"), _jsx("input", { type: "search", value: search, onChange: (e) => setSearch(e.target.value), placeholder: t("Search messages and annotations…") })] }), _jsxs("label", { children: [t("Message role"), _jsxs("select", { value: role, onChange: (e) => setRole(e.target.value), children: [_jsx("option", { value: "", children: t("All roles") }), roles.map((value) => (_jsx("option", { value: value, children: t(value) }, value)))] })] }), _jsxs("label", { children: [t("Annotation topic"), _jsxs("select", { value: topic, onChange: (e) => setTopic(e.target.value), children: [_jsx("option", { value: "", children: t("All topics") }), topics.map((value) => (_jsxs("option", { value: value, children: ["#", value] }, value)))] })] })] }), _jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: annotated, onChange: (e) => setAnnotated(e.target.checked) }), t("With annotations only")] })] })] }), !matching.length && (_jsx("p", { role: "status", children: t("No messages match these filters.") })), matching.map((row, index) => {
                const hidden = row.index - (index ? matching[index - 1].index : -1) - 1;
                return (_jsxs(Fragment, { children: [hidden > 0 && (_jsx("div", { className: "conversation-hidden-gap", children: t("{count} messages hidden", { count: hidden }) })), renderMessage(row.message)] }, row.message.id));
            }), matching.length > 0 &&
                matching[matching.length - 1].index < messages.length - 1 && (_jsx("div", { className: "conversation-hidden-gap", children: t("{count} messages hidden", {
                    count: messages.length - matching[matching.length - 1].index - 1,
                }) }))] }));
}
