import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { Plug, ShieldCheck, ArrowUpRight, Plus, Copy, KeyRound, Check, Clock3, } from "lucide-react";
import { api, useApp, useJourneyOptions } from "../../shared/state";
import { t, useLocale, formatDate } from "../../shared/i18n/i18n";
import { Modal } from "../../shared/ui/ui";
import "./integrations.css";
import { GitHubIntegration } from "./GitHubIntegration";
const readTools = [
    "search_journeys",
    "get_journey",
    "get_journey_timeline",
    "get_journey_evidence",
    "get_related_conversations",
    "search_decisions",
];
const proposalTools = [
    "propose_journey_entry",
    "suggest_relationship",
    "propose_annotation",
    "propose_milestone",
];
export function Integrations({ canEdit, refresh, }) {
    useLocale();
    const { workspace } = useApp();
    const journeys = useJourneyOptions(workspace);
    const [status, setStatus] = useState();
    const [connections, setConnections] = useState([]);
    const [proposals, setProposals] = useState([]);
    const [error, setError] = useState("");
    const [busy, setBusy] = useState(false);
    const [creating, setCreating] = useState(false);
    const [name, setName] = useState("");
    const [selected, setSelected] = useState([]);
    const [mode, setMode] = useState("local");
    const [allowProposals, setAllowProposals] = useState(false);
    const [credential, setCredential] = useState();
    const [review, setReview] = useState();
    const [confirm, setConfirm] = useState(false);
    const [copied, setCopied] = useState(false);
    const load = async () => {
        const [s, data] = await Promise.all([
            api("/integrations/mcp/status"),
            api(`/integrations/mcp/w/${workspace}`),
        ]);
        setStatus(s);
        setConnections(data.connections);
        setProposals(data.proposals);
    };
    useEffect(() => {
        let alive = true;
        const reload = () => load().catch((e) => {
            if (alive)
                setError(e.message);
        });
        void reload();
        window.addEventListener("focus", reload);
        return () => {
            window.removeEventListener("focus", reload);
            alive = false;
        };
    }, [workspace]);
    const run = async (fn) => {
        setBusy(true);
        setError("");
        try {
            await fn();
        }
        catch (e) {
            setError(e.message);
        }
        finally {
            setBusy(false);
        }
    };
    const endpoint = new URL(status?.endpoint || "/mcp", location.origin).href;
    const pending = proposals.filter((p) => p.status === "pending");
    const target = journeys.find((j) => j.id === review?.journeyId);
    const stale = !!review && target?.version !== review.baseVersion;
    const config = JSON.stringify({
        url: endpoint,
        transport: "Streamable HTTP",
        ...(credential?.token
            ? { headers: { Authorization: `Bearer ${credential.token}` } }
            : {}),
    }, null, 2);
    return (_jsxs("div", { className: "mcp-page", children: [_jsxs("header", { className: "mcp-heading", children: [_jsxs("div", { children: [_jsxs("div", { className: "mcp-eyebrow", children: [t("Integrations"), " ", _jsx("span", { children: t("Experimental") })] }), _jsx("h1", { children: t("Your journey, in context.") }), _jsx("p", { children: t("Give your AI applications the context behind your work.") })] }), _jsx(Plug, { size: 32 })] }), _jsx(GitHubIntegration, { canEdit: canEdit }, workspace), error && (_jsx("p", { className: "mcp-error", role: "alert", children: error })), !status ? (_jsx("p", { role: "status", children: t("Loading…") })) : (_jsxs(_Fragment, { children: [!status.enabled && (_jsx("p", { className: "mcp-notice", children: t("MCP is disabled. Ask your administrator to enable the experimental integration.") })), _jsxs("div", { className: "mcp-layout", children: [_jsxs("div", { className: "mcp-main", children: [_jsxs("section", { className: "mcp-card", children: [_jsxs("div", { className: "mcp-card-title", children: [_jsx("span", { className: "mcp-icon", children: _jsx(Plug, { size: 24 }) }), _jsxs("div", { children: [_jsx("h2", { children: "Model Context Protocol" }), _jsx("p", { children: t("Connect compatible AI applications to openhow.") })] }), _jsx("span", { className: "mcp-pill", children: t(status.enabled ? "Enabled" : "Disabled") })] }), _jsxs("div", { className: "mcp-endpoint", children: [_jsx("span", { children: t("Server endpoint") }), _jsx("code", { children: endpoint }), _jsx("small", { children: "Streamable HTTP" })] }), _jsxs("div", { className: "mcp-trust", children: [_jsx(ShieldCheck, { size: 20 }), _jsx("p", { children: t("You choose the journeys. AI suggestions wait for your approval.") })] }), _jsxs("button", { className: "primary", disabled: !status.enabled ||
                                                    (!status.localAvailable && !status.remoteConfigured), onClick: () => {
                                                    setCreating(true);
                                                    setName("");
                                                    setSelected([]);
                                                    setAllowProposals(false);
                                                    setMode(status.localAvailable ? "local" : "oauth");
                                                    setError("");
                                                }, children: [_jsx(Plus, { size: 16 }), t("Add connection")] })] }), _jsxs("section", { className: "mcp-card", children: [_jsxs("div", { className: "mcp-section-title", children: [_jsx("h2", { children: t("Your connections") }), _jsx("span", { children: connections.filter((c) => !c.revoked && new Date(c.expiresAt) > new Date()).length })] }), !connections.length && (_jsxs("div", { className: "mcp-empty", children: [_jsx(KeyRound, { size: 24 }), _jsx("h3", { children: t("No connections yet") }), _jsx("p", { children: t("Create a connection with access to specific journeys in this workspace.") })] })), connections.map((c) => {
                                                const inactive = c.revoked || new Date(c.expiresAt) <= new Date();
                                                return (_jsxs("div", { className: "mcp-connection", children: [_jsxs("div", { children: [_jsx("strong", { children: c.name }), _jsxs("p", { children: [t(c.mode === "local"
                                                                            ? "Local development"
                                                                            : "Remote OAuth"), " ", "\u00B7", " ", t(c.allowProposals ? "Read + propose" : "Read only")] }), _jsx("small", { children: JSON.parse(c.journeyIds)
                                                                        .map((id) => journeys.find((j) => j.id === id)?.title ||
                                                                        t("Unavailable journey"))
                                                                        .join(", ") }), _jsxs("small", { children: [t("Expires"), ": ", formatDate(c.expiresAt), " \u00B7", " ", t("Last used"), ":", " ", c.lastUsedAt ? formatDate(c.lastUsedAt) : t("Never")] })] }), inactive ? (_jsx("span", { className: "mcp-pill", children: t(c.revoked ? "Revoked" : "Expired") })) : (_jsx("button", { disabled: busy, onClick: () => run(async () => {
                                                                await api(`/integrations/mcp/connections/${c.id}`, undefined, "DELETE");
                                                                await load();
                                                            }), children: t("Revoke") }))] }, c.id));
                                            })] }), _jsxs("section", { className: "mcp-card", children: [_jsxs("div", { className: "mcp-section-title", children: [_jsx("h2", { children: t("Proposals to review") }), _jsx("span", { children: pending.length }), _jsx("button", { disabled: busy, onClick: () => run(load), children: t("Refresh") })] }), _jsx("p", { className: "mcp-muted", children: t("Nothing changes until an editor approves. Publishing stays separate.") }), !pending.length && (_jsxs("div", { className: "mcp-empty", children: [_jsx(Check, { size: 24 }), _jsx("p", { children: t("No pending proposals") })] })), pending.map((p) => (_jsxs("div", { className: "mcp-connection", children: [_jsx(Clock3, { size: 18 }), _jsxs("div", { children: [_jsx("strong", { children: JSON.parse(p.payload).title || t(p.kind) }), _jsxs("p", { children: [journeys.find((j) => j.id === p.journeyId)?.title, " \u00B7", " ", t(p.kind)] }), _jsx("small", { children: formatDate(p.createdAt) })] }), _jsx("button", { disabled: !canEdit || !status.enabled, onClick: () => {
                                                            setReview(p);
                                                            setConfirm(false);
                                                            setError("");
                                                        }, children: t("Review") })] }, p.id))), proposals.some((p) => p.status !== "pending") && (_jsxs("details", { children: [_jsx("summary", { children: t("Review history") }), proposals
                                                        .filter((p) => p.status !== "pending")
                                                        .map((p) => (_jsxs("div", { className: "mcp-history", children: [_jsx("span", { children: JSON.parse(p.payload).title || t(p.kind) }), _jsx("span", { children: t(p.status) })] }, p.id)))] }))] })] }), _jsxs("aside", { className: "mcp-aside", children: [_jsxs("section", { className: "mcp-card", children: [_jsx(ShieldCheck, { size: 24 }), _jsx("h2", { children: t("Access you control") }), _jsx("p", { children: t("Permissions follow your current workspace role and selected journeys. Revoking a connection stops future calls.") }), _jsx("h3", { children: t("Read context") }), readTools.map((n) => (_jsx("code", { className: "mcp-tool", children: n }, n))), _jsx("h3", { children: t("Propose changes") }), _jsx("p", { children: t("Requires approval in openhow") }), proposalTools.map((n) => (_jsx("code", { className: "mcp-tool", children: n }, n)))] }), _jsxs("section", { className: "mcp-card", children: [_jsx("h3", { children: t("Connect your client") }), _jsx("p", { children: t("Use a client that supports Streamable HTTP and the authentication method shown when you create a connection.") }), _jsxs("a", { href: "https://modelcontextprotocol.io/docs/learn/architecture", target: "_blank", rel: "noreferrer", children: [t("About MCP"), " ", _jsx(ArrowUpRight, { size: 14 })] })] })] })] })] })), creating && (_jsx(Modal, { title: t("Add connection"), onClose: () => {
                    if (!busy)
                        setCreating(false);
                }, children: _jsxs("form", { className: "mcp-form", onSubmit: (e) => {
                        e.preventDefault();
                        void run(async () => {
                            const result = await api(`/integrations/mcp/w/${workspace}/connections`, { name, journeyIds: selected, allowProposals, mode });
                            setCredential(result);
                            setCopied(false);
                            setCreating(false);
                            await load();
                        });
                    }, children: [error && (_jsx("p", { role: "alert", className: "mcp-error", children: error })), _jsxs("label", { children: [t("Connection name"), _jsx("input", { required: true, maxLength: 80, value: name, onChange: (e) => setName(e.target.value), placeholder: t("My AI assistant") })] }), _jsxs("label", { children: [t("Connection type"), _jsxs("select", { value: mode, onChange: (e) => setMode(e.target.value), children: [status?.localAvailable && (_jsx("option", { value: "local", children: t("Local development") })), status?.remoteConfigured && (_jsx("option", { value: "oauth", children: t("Remote OAuth") }))] })] }), _jsxs("fieldset", { children: [_jsx("legend", { children: t("Choose journeys") }), journeys.length === 0 && _jsx("p", { children: t("Create a journey first.") }), journeys.map((j) => (_jsxs("label", { className: "mcp-check", children: [_jsx("input", { type: "checkbox", checked: selected.includes(j.id), onChange: (e) => setSelected((ids) => e.target.checked
                                                ? [...ids, j.id]
                                                : ids.filter((id) => id !== j.id)) }), _jsx("span", { children: j.title })] }, j.id)))] }), _jsxs("label", { className: "mcp-check", children: [_jsx("input", { type: "checkbox", disabled: !canEdit, checked: allowProposals, onChange: (e) => setAllowProposals(e.target.checked) }), _jsx("span", { children: t("Allow proposals for review") })] }), _jsx("p", { children: t("Read access is always included. Connections expire after 30 days.") }), _jsx("button", { className: "primary", disabled: busy || !selected.length || selected.length > 100, children: t("Create connection") })] }) })), credential && (_jsx(Modal, { title: t("Connection created"), onClose: () => setCredential(undefined), children: _jsxs("div", { className: "mcp-form", children: [_jsx("p", { children: t(credential.token
                                ? "Save this credential now. It is shown only once. Keep it private."
                                : "Your administrator must bind this connection ID to your OAuth access token before use.") }), credential.token ? (_jsxs(_Fragment, { children: [_jsx("p", { children: t("Enter these connection settings in your MCP client. Client configuration formats vary.") }), _jsx("pre", { className: "mcp-secret", children: config }), _jsxs("button", { onClick: () => run(async () => {
                                        await navigator.clipboard.writeText(config);
                                        setCopied(true);
                                    }), children: [_jsx(Copy, { size: 16 }), t(copied ? "Copied" : "Copy settings")] })] })) : (_jsx("code", { className: "mcp-secret", children: credential.id })), _jsxs("p", { children: [t("Expires"), ": ", formatDate(credential.expiresAt)] }), _jsx("button", { className: "primary", onClick: () => setCredential(undefined), children: t("Done") })] }) })), review && (_jsx(Modal, { title: t("Review proposal"), onClose: () => {
                    if (!busy)
                        setReview(undefined);
                }, children: _jsxs("div", { className: "mcp-form", children: [_jsx("strong", { children: target?.title }), _jsx("p", { children: t("Proposed content is untrusted. Review it before applying.") }), error && (_jsx("p", { role: "alert", className: "mcp-error", children: error })), _jsx("dl", { className: "mcp-payload", children: Object.entries(JSON.parse(review.payload))
                                .filter(([, value]) => value !== null && value !== "")
                                .map(([key, value]) => (_jsxs("div", { children: [_jsx("dt", { children: t(key) }), _jsx("dd", { children: String(value) })] }, key))) }), stale && (_jsx("p", { className: "mcp-notice", children: t("The journey changed. Reject this proposal and request a fresh one.") })), _jsxs("label", { className: "mcp-check", children: [_jsx("input", { type: "checkbox", checked: confirm, onChange: (e) => setConfirm(e.target.checked) }), _jsx("span", { children: t("I reviewed this proposal and confirm my decision.") })] }), _jsxs("div", { className: "mcp-actions", children: [_jsx("button", { disabled: !confirm || busy, onClick: () => run(async () => {
                                        await api(`/integrations/mcp/proposals/${review.id}/review`, {
                                            approve: false,
                                            expectedVersion: review.baseVersion,
                                            confirm: true,
                                        });
                                        setReview(undefined);
                                        await load();
                                    }), children: t("Reject") }), _jsx("button", { className: "primary", disabled: !confirm || busy || stale, onClick: () => run(async () => {
                                        await api(`/integrations/mcp/proposals/${review.id}/review`, {
                                            approve: true,
                                            expectedVersion: review.baseVersion,
                                            confirm: true,
                                        });
                                        setReview(undefined);
                                        await load();
                                        refresh();
                                    }), children: t("Approve and apply") })] })] }) }))] }));
}
