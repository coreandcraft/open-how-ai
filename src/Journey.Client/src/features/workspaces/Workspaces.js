import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { t, useLocale } from "../../shared/i18n/i18n";
import { useState } from "react";
import { ArrowRight, Check, Layers3, LockKeyhole, Plus, Users, } from "lucide-react";
import { api, go, useApp } from "../../shared/state";
import { Modal } from "../../shared/ui/ui";
export function CreateWorkspaceDialog({ close, created, }) {
    useLocale();
    const [name, setName] = useState("");
    const [type, setType] = useState("Personal");
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState("");
    return (_jsx(Modal, { title: t("Create workspace"), onClose: close, children: _jsxs("form", { onSubmit: async (e) => {
                e.preventDefault();
                setError("");
                setBusy(true);
                try {
                    created(await api("/workspaces", {
                        name: name.trim(),
                        type,
                    }));
                }
                catch (e) {
                    setError(e.message);
                }
                finally {
                    setBusy(false);
                }
            }, children: [_jsx("p", { children: t("A separate home for conversations, journeys, and comparisons. Create as many workspaces as you need.") }), _jsxs("label", { children: [t("Workspace name"), _jsx("input", { autoFocus: true, required: true, maxLength: 100, value: name, onChange: (e) => setName(e.target.value), placeholder: t("e.g. Product research, Side projects, Design team") })] }), _jsxs("fieldset", { className: "workspace-type-options", children: [_jsx("legend", { children: t("Workspace type") }), _jsxs("label", { className: type === "Personal" ? "chosen" : "", children: [_jsx("input", { type: "radio", name: "workspaceType", value: "Personal", checked: type === "Personal", onChange: () => setType("Personal") }), _jsx(LockKeyhole, { size: 19 }), _jsxs("span", { children: [_jsx("strong", { children: t("Personal") }), _jsx("small", { children: t("Private to you. Organize your own projects.") })] })] }), _jsxs("label", { className: type === "Team" ? "chosen" : "", children: [_jsx("input", { type: "radio", name: "workspaceType", value: "Team", checked: type === "Team", onChange: () => setType("Team") }), _jsx(Users, { size: 19 }), _jsxs("span", { children: [_jsx("strong", { children: t("Team") }), _jsx("small", { children: t("Invite people and assign access roles.") })] })] })] }), _jsxs("p", { className: "workspace-privacy", children: [type === "Personal"
                            ? t("Only you can access this workspace.")
                            : t("Starts with you as owner. Invite teammates when you’re ready."), " ", t("Existing conversations stay in their current workspace.")] }), error && (_jsx("p", { className: "error", role: "alert", children: error })), _jsxs("div", { className: "modal-footer", children: [_jsx("button", { type: "button", disabled: busy, onClick: close, children: t("Cancel") }), _jsxs("button", { className: "primary", disabled: busy || !name.trim(), children: [busy ? t("Creating…") : t("Create workspace"), _jsx(Plus, { size: 15 })] })] })] }) }));
}
export function Workspaces({ workspaces, onCreate, refresh, }) {
    useLocale();
    const { workspace, setWorkspace, notify } = useApp();
    const [renaming, setRenaming] = useState(null);
    const [name, setName] = useState("");
    const [error, setError] = useState("");
    const [busy, setBusy] = useState(false);
    const [token, setToken] = useState("");
    const [joining, setJoining] = useState(false);
    const open = (w, path) => {
        setWorkspace(w.id);
        go(path);
    };
    return (_jsxs("section", { className: "page workspace-management", children: [_jsxs("div", { className: "page-heading", children: [_jsxs("div", { children: [_jsx("span", { className: "eyebrow", children: t("A HOME FOR EVERY PROJECT") }), _jsx("h1", { children: t("Workspaces") }), _jsx("p", { className: "subtitle", children: t("Organize separate areas for your work. Keep them personal or collaborate with a team.") })] }), _jsxs("button", { className: "primary", onClick: onCreate, children: [_jsx(Plus, { size: 17 }), t("Create workspace")] })] }), _jsxs("div", { className: "workspace-explainer", children: [_jsx(Layers3, { size: 22 }), _jsx("p", { children: t("Each workspace has its own conversation library, journeys, comparisons, and access settings. Personal library and Cloudplay Studio are starting examples—not a fixed limit.") })] }), _jsxs("div", { className: "workspace-grid", children: [workspaces.map((w) => (_jsxs("article", { className: "workspace-card " + (w.id === workspace ? "current" : ""), children: [_jsxs("div", { className: "row", children: [_jsx("span", { className: "workspace-card-icon " + (w.teamId ? "team" : "personal"), children: w.teamId ? _jsx(Users, { size: 22 }) : _jsx(LockKeyhole, { size: 21 }) }), _jsx("span", { className: "spacer" }), w.id === workspace && (_jsxs("span", { className: "workspace-current", children: [_jsx(Check, { size: 12 }), t("Current workspace")] }))] }), _jsx("h2", { children: w.name }), _jsxs("div", { className: "row", children: [_jsx("span", { className: "type-tag", children: w.teamId ? t("Team workspace") : t("Personal workspace") }), _jsx("span", { className: "muted", children: t(w.role) })] }), _jsx("p", { children: w.teamId
                                    ? t("A shared library for your team. Membership and permissions are managed separately.")
                                    : t("A private library for your own conversations and projects. Only you have access.") }), _jsxs("footer", { children: [_jsxs("button", { className: "primary subtle", onClick: () => open(w, "dashboard"), children: [t("Open workspace"), _jsx(ArrowRight, { size: 14 })] }), w.teamId && (_jsx("button", { onClick: () => open(w, "team"), children: t("Members & access") })), ["Owner", "Admin"].includes(w.role) && (_jsx("button", { className: "workspace-rename", onClick: () => {
                                            setRenaming(w);
                                            setName(w.name);
                                            setError("");
                                        }, children: t("Rename") }))] })] }, w.id))), _jsxs("button", { className: "workspace-add-card", onClick: onCreate, children: [_jsx(Plus, { size: 26 }), _jsx("strong", { children: t("Create another workspace") }), _jsx("span", { children: t("For a project, a team, or a new area of work") })] })] }), _jsxs("div", { className: "workspace-join panel", children: [_jsxs("div", { children: [_jsx("h2", { children: t("Invited to a team workspace?") }), _jsx("p", { children: t("Accept an invitation using the account with the invited email address.") })] }), _jsxs("form", { onSubmit: async (e) => {
                            e.preventDefault();
                            setJoining(true);
                            setError("");
                            try {
                                await api("/invitations/" + encodeURIComponent(token.trim()) + "/accept", {});
                                setToken("");
                                refresh();
                                notify(t("Joined workspace. Select it above to get started."));
                            }
                            catch (e) {
                                setError(e.message);
                            }
                            finally {
                                setJoining(false);
                            }
                        }, children: [_jsx("input", { "aria-label": t("Workspace invitation token"), placeholder: t("Paste invitation token"), value: token, onChange: (e) => setToken(e.target.value), required: true }), _jsx("button", { disabled: joining || !token.trim(), children: joining ? t("Joining…") : t("Join workspace") })] }), error && !renaming && (_jsx("p", { className: "error", role: "alert", children: error }))] }), renaming && (_jsx(Modal, { title: t("Rename workspace"), onClose: () => setRenaming(null), children: _jsxs("form", { onSubmit: async (e) => {
                        e.preventDefault();
                        setBusy(true);
                        try {
                            await api("/w/" + renaming.id, { name: name.trim() }, "PUT");
                            setRenaming(null);
                            refresh();
                            notify(t("Workspace renamed"));
                        }
                        catch (e) {
                            setError(e.message);
                        }
                        finally {
                            setBusy(false);
                        }
                    }, children: [_jsxs("label", { children: [t("Workspace name"), _jsx("input", { autoFocus: true, required: true, maxLength: 100, value: name, onChange: (e) => setName(e.target.value) })] }), error && (_jsx("p", { className: "error", role: "alert", children: error })), _jsxs("div", { className: "modal-footer", children: [_jsx("button", { type: "button", onClick: () => setRenaming(null), children: t("Cancel") }), _jsx("button", { className: "primary", disabled: busy || !name.trim(), children: busy ? t("Saving…") : t("Save name") })] })] }) }))] }));
}
