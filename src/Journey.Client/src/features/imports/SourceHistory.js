import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { RecoveredSource } from "./RecoveredSource";
import { useEffect, useState } from "react";
import { api, act, useLibrary } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
export function SourceHistory({ conversation, canEdit, onUpdated, }) {
    const [projects, setProjects] = useState([]);
    const [links, setLinks] = useState([]);
    const [conflicts, setConflicts] = useState([]);
    const picker = useLibrary(conversation.workspaceId, { limit: 40 });
    const [project, setProject] = useState(conversation.applicationProjectId || "");
    const [confirm, setConfirm] = useState(null);
    const [reviewResult, setReviewResult] = useState("");
    const [newName, setNewName] = useState("");
    const load = async () => {
        const base = "/w/" + conversation.workspaceId;
        const [p, l, c] = await Promise.all([
            api(base + "/projects"),
            api(base + "/source-relationships"),
            api(base + "/import-conflicts"),
        ]);
        setProjects(p);
        setLinks(l.filter((r) => r.conversationId === conversation.id ||
            r.targetConversationId === conversation.id));
        setConflicts(c.filter((r) => r.conversationId === conversation.id));
    };
    useEffect(() => {
        setProject(conversation.applicationProjectId || "");
        act(load);
    }, [conversation.id]);
    return (_jsxs("details", { className: "source-history", children: [_jsx("summary", { children: t("Source grouping & import history") }), _jsxs("p", { children: [conversation.provider, " \u00B7 ", conversation.sourceFormat] }), conversation.externalProjectId && (_jsxs("p", { children: [t("Original project"), ":", " ", conversation.sourceProjectName || conversation.externalProjectId] })), conversation.sourcePath && (_jsxs("p", { children: [t("Original workspace path"), ": ", conversation.sourcePath] })), _jsxs("label", { children: [t("Application project"), _jsxs("select", { disabled: !canEdit, value: project, onChange: (e) => {
                            const value = e.target.value;
                            act(async () => {
                                await api("/conversations/" + conversation.id + "/project", { projectId: value || null }, "PUT");
                                setProject(value);
                                onUpdated?.();
                            });
                        }, children: [_jsx("option", { value: "", children: t("Unassigned") }), projects.map((p) => (_jsx("option", { value: p.id, children: p.name }, p.id)))] })] }), canEdit && (_jsxs("form", { onSubmit: (e) => {
                    e.preventDefault();
                    act(async () => {
                        const p = await api("/w/" + conversation.workspaceId + "/projects", { name: newName });
                        await api("/conversations/" + conversation.id + "/project", { projectId: p.id }, "PUT");
                        setProject(p.id);
                        setNewName("");
                        await load();
                        onUpdated?.();
                    });
                }, children: [_jsx("input", { "aria-label": t("New application project"), placeholder: t("New application project"), value: newName, maxLength: 150, onChange: (e) => setNewName(e.target.value) }), _jsx("button", { disabled: !newName.trim(), children: t("Create & associate") })] })), links.map((r) => (_jsxs("div", { children: [_jsxs("p", { children: [r.kind, " \u00B7 ", r.status] }), r.targetConversationId ? (_jsx("a", { href: "#explorer/" +
                            (r.conversationId === conversation.id
                                ? r.targetConversationId
                                : r.conversationId), children: t("Open linked session") })) : (_jsxs(_Fragment, { children: [_jsxs("small", { children: [t("Unresolved source ID"), ": ", r.externalTargetId] }), canEdit && (_jsxs("select", { "aria-label": t("Resolve session relationship"), value: "", onChange: (e) => act(async () => {
                                    await api("/source-relationships/" + r.id, { targetId: e.target.value }, "PUT");
                                    await load();
                                }), children: [_jsx("option", { value: "", children: t("Associate a session…") }), picker.items
                                        .filter((c) => c.id !== conversation.id)
                                        .map((c) => (_jsx("option", { value: c.id, children: c.title }, c.id)))] }))] }))] }, r.id))), reviewResult && _jsx("p", { role: "status", children: reviewResult }), conflicts.map((conflict) => (_jsxs("details", { children: [_jsxs("summary", { children: [t("Changed source message"), " \u00B7 ", conflict.status] }), _jsx("p", { children: t("The original message and its annotations were preserved. Incoming version:") }), _jsx("pre", { children: JSON.parse(conflict.incoming)
                            .blocks.map((b) => b.text)
                            .join("\n") }), canEdit && conflict.status === "unresolved" && (_jsxs("div", { children: [confirm === conflict.id ? (_jsxs("div", { children: [_jsx("p", { children: t("Accept this source revision? The previous version is retained. Comments whose quotes cannot be recovered will be marked for review.") }), _jsx("button", { onClick: () => act(async () => {
                                            const result = await api("/import-conflicts/" + conflict.id + "/accept", {});
                                            setReviewResult(`${result.recovered} annotations recovered · ${result.orphaned} need review`);
                                            setConfirm(null);
                                            await load();
                                            onUpdated?.();
                                        }), children: t("Accept revision") }), _jsx("button", { onClick: () => setConfirm(null), children: t("Cancel") })] })) : (_jsx("button", { onClick: () => setConfirm(conflict.id), children: t("Review & accept incoming") })), _jsx("button", { onClick: () => act(async () => {
                                    await api("/import-conflicts/" + conflict.id + "/keep-original", {});
                                    await load();
                                }), children: t("Keep original · mark reviewed") })] }))] }, conflict.id))), _jsx(RecoveredSource, { id: conversation.id }, conflicts.map((c) => c.status).join()), _jsxs("details", { children: [_jsx("summary", { children: t("Provenance") }), _jsx("pre", { children: JSON.stringify(JSON.parse(conversation.provenance || "{}"), null, 2) })] })] }));
}
