import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { Check, ExternalLink, FileCode2, GitBranch, Link2, ShieldCheck, } from "lucide-react";
import { api, useApp } from "../../shared/state";
import { t, useLocale } from "../../shared/i18n/i18n";
import "./github.css";
import { GitHubSetup } from "./GitHubSetup";
export function GitHubIntegration({ canEdit }) {
    useLocale();
    const { workspace } = useApp();
    const [projects, setProjects] = useState([]);
    const [selected, setSelected] = useState("");
    const [enabled, setEnabled] = useState(false);
    const [mode, setMode] = useState("Public");
    const [configured, setConfigured] = useState(true);
    const [installationUrl, setInstallationUrl] = useState(null);
    const [configurationError, setConfigurationError] = useState("");
    const [loading, setLoading] = useState(true);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");
    const [repositoryUrl, setRepositoryUrl] = useState("");
    const [branch, setBranch] = useState("");
    const [localRoot, setLocalRoot] = useState("");
    const [newName, setNewName] = useState("");
    const project = projects.find((p) => p.id === selected);
    useEffect(() => {
        let live = true;
        Promise.all([
            api("/integrations/github/status"),
            api(`/w/${workspace}/projects`),
        ])
            .then(([status, rows]) => {
            if (live) {
                setEnabled(status.enabled);
                setMode(status.mode || "Public");
                setConfigured(status.configured !== false);
                setInstallationUrl(status.installationUrl || null);
                setConfigurationError(status.configurationError || "");
                setProjects(rows);
                setSelected(rows[0]?.id || "");
            }
        })
            .catch((e) => live && setError(e.message))
            .finally(() => live && setLoading(false));
        return () => {
            live = false;
        };
    }, [workspace]);
    useEffect(() => {
        setRepositoryUrl(project?.gitHubRepositoryUrl || "");
        setBranch(project?.gitHubBranch || "");
        setLocalRoot(project?.gitHubLocalRoot || "");
    }, [project]);
    async function run(action) {
        setBusy(true);
        setError("");
        setNotice("");
        try {
            await action();
        }
        catch (e) {
            setError(e.message);
        }
        finally {
            setBusy(false);
        }
    }
    return (_jsxs("section", { className: "github-integration", "aria-label": t("GitHub integration"), children: [_jsxs("div", { className: "github-section-heading", children: [_jsxs("div", { children: [_jsx("h2", { children: t("GitHub integration") }), _jsx("p", { children: t("Link a public repository to a project in your workspace.") })] }), _jsx("span", { className: "github-badge", children: t("Experimental") })] }), loading ? (_jsx("p", { role: "status", children: t("Loading…") })) : (_jsxs(_Fragment, { children: [!enabled && (_jsx("p", { className: "github-notice", children: t("The experimental GitHub integration is disabled.") })), _jsx(GitHubSetup, {}), configurationError && (_jsx("p", { className: "github-notice", role: "alert", children: t(configurationError) })), mode === "App" && configured && (_jsxs("div", { className: "github-app-controls", children: [_jsx("p", { children: t("Install the GitHub App on your repository, then return here to link it. Organization access may need owner approval.") }), installationUrl && (_jsxs("a", { href: installationUrl, target: "_blank", rel: "noopener noreferrer", children: [t("Install or manage GitHub App"), _jsx(ExternalLink, { size: 14 })] })), canEdit && (_jsx("button", { type: "button", disabled: busy || !enabled, onClick: () => void run(async () => {
                                    await api(`/w/${workspace}/integrations/github/check`, {});
                                    setNotice(t("GitHub App credentials verified. Install it on the repository before linking."));
                                }), children: t("Check App credentials") }))] })), error && (_jsx("p", { role: "alert", className: "error", children: error })), _jsxs("div", { className: "github-layout", children: [_jsxs("form", { className: "github-card", onSubmit: (event) => {
                                    event.preventDefault();
                                    if (!project || busy || !enabled || !configured || !canEdit)
                                        return;
                                    void run(async () => {
                                        const saved = await api(`/w/${workspace}/projects/${project.id}/github`, { repositoryUrl, branch, localRoot }, "PUT");
                                        setProjects((rows) => rows.map((p) => (p.id === saved.id ? saved : p)));
                                        setNotice(t("Repository linked. Open a conversation assigned to this project to see source links."));
                                    });
                                }, children: [_jsxs("div", { className: "github-card-heading", children: [_jsx("span", { className: "github-icon", children: _jsx(GitBranch, { size: 22 }) }), _jsx("h3", { children: t("Public repository") }), project?.gitHubRepositoryUrl && (_jsxs("span", { className: "github-connected", children: [_jsx(Check, { size: 14 }), t("Connected")] }))] }), _jsxs("label", { children: [t("Project"), _jsxs("select", { disabled: busy, value: selected, onChange: (e) => {
                                                    setSelected(e.target.value);
                                                    setNotice("");
                                                }, children: [_jsx("option", { value: "", disabled: true, children: t("Choose a project") }), projects.map((p) => (_jsx("option", { value: p.id, children: p.name }, p.id)))] })] }), _jsxs("fieldset", { disabled: busy || !canEdit || !enabled || !configured, children: [_jsxs("details", { children: [_jsx("summary", { children: t("Create a project") }), _jsxs("div", { className: "github-create", children: [_jsx("input", { "aria-label": t("Project name"), value: newName, maxLength: 150, onChange: (e) => setNewName(e.target.value) }), _jsx("button", { type: "button", disabled: !newName.trim(), onClick: () => void run(async () => {
                                                                    const added = await api(`/w/${workspace}/projects`, { name: newName });
                                                                    setProjects((rows) => [...rows, added]);
                                                                    setSelected(added.id);
                                                                    setNewName("");
                                                                }), children: t("Create") })] })] }), _jsxs("label", { children: [t("Repository URL"), _jsx("input", { type: "url", required: true, value: repositoryUrl, maxLength: 250, placeholder: "https://github.com/owner/repository", onChange: (e) => setRepositoryUrl(e.target.value) })] }), _jsx("p", { className: "github-help", children: t(mode === "App"
                                                    ? "Public repositories only. The GitHub App needs access to the selected repository."
                                                    : "Public repositories only. No GitHub sign-in or token required.") }), _jsxs("label", { children: [t("Branch"), _jsx("input", { value: branch, maxLength: 250, placeholder: t("Repository default branch"), onChange: (e) => setBranch(e.target.value) })] }), _jsxs("label", { children: [t("Project folder in tool calls"), _jsx("input", { value: localRoot, maxLength: 1024, placeholder: "C:\\projects\\my-app or /workspace/my-app", onChange: (e) => setLocalRoot(e.target.value) })] }), _jsx("p", { className: "github-help", children: t("Required for absolute file paths. Use the repository root from the imported conversation, not this server.") })] }), _jsxs("div", { className: "github-actions", children: [_jsxs("button", { className: "primary", disabled: busy || !canEdit || !enabled || !configured || !project, type: "submit", children: [_jsx(Link2, { size: 15 }), t(project?.gitHubRepositoryUrl
                                                        ? "Save connection"
                                                        : "Link repository")] }), project?.gitHubRepositoryUrl && (_jsx("button", { disabled: busy || !canEdit, type: "button", onClick: () => void run(async () => {
                                                    await api(`/w/${workspace}/projects/${project.id}/github`, undefined, "DELETE");
                                                    setProjects((rows) => rows.map((p) => p.id === project.id
                                                        ? {
                                                            ...p,
                                                            gitHubRepositoryUrl: null,
                                                            gitHubBranch: null,
                                                            gitHubLocalRoot: null,
                                                        }
                                                        : p));
                                                }), children: t("Unlink") }))] }), notice && (_jsx("p", { className: "github-success", role: "status", children: notice })), !canEdit && (_jsx("p", { className: "github-help", children: t("An editor can manage repository connections.") }))] }), _jsxs("aside", { className: "github-card github-explanation", children: [_jsx("h3", { children: t("In your conversations") }), _jsx("p", { children: t("Read-file tool calls can link to the matching file and line range on GitHub.") }), _jsxs("div", { className: "github-example", children: [_jsx(FileCode2, { size: 17 }), _jsx("code", { children: "src/auth/session.ts" }), _jsx(ExternalLink, { size: 14 })] }), _jsx("p", { children: t("Recorded commits stay pinned. Without a recorded commit, links are labeled Latest on GitHub.") }), _jsx("p", { children: t("Local, uncommitted changes may differ. Original tool payloads stay preserved.") }), _jsx("p", { children: t("Assign conversations using Source grouping & import history → Application project.") }), project?.gitHubRepositoryUrl && (_jsxs("a", { href: project.gitHubRepositoryUrl, target: "_blank", rel: "noopener noreferrer", children: [project.gitHubRepositoryUrl.replace("https://github.com/", ""), _jsx(ExternalLink, { size: 14 })] })), _jsxs("div", { className: "github-trust", children: [_jsx(ShieldCheck, { size: 17 }), t("Conversation sharing stays unchanged.")] })] })] })] }))] }));
}
