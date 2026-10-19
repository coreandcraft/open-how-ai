import { jsx as _jsx, Fragment as _Fragment, jsxs as _jsxs } from "react/jsx-runtime";
import { AccountScreen, AccountSecurity } from "../features/accounts/Accounts";
import { SanitizationSettings } from "../features/imports/SanitizationSettings";
import { BrandMark } from "../shared/ui/BrandMark";
import { LandingPage } from "../features/landing/LandingPage";
import { t, useLocale, formatNumber, formatDate, count, } from "../shared/i18n/i18n";
import { LanguagePicker } from "../shared/i18n/LanguagePicker";
import { Workspaces, CreateWorkspaceDialog, } from "../features/workspaces/Workspaces";
import React, { useEffect, useState, useRef, lazy, Suspense } from "react";
import { createRoot } from "react-dom/client";
import { HubConnectionBuilder } from "@microsoft/signalr";
import { Plug, Menu, ArrowUpRight, BookOpen, GitBranch, LayoutDashboard, Layers3, Library, Users, Bell, Settings, Plus, Search, ChevronDown, Sun, Moon, LogOut, ArrowRight, Upload, Command, X, Trash2, } from "lucide-react";
import { api, useApp, go, act, initials, providerClass, useLibrary, } from "../shared/state";
const Integrations = lazy(() => import("../features/integrations/Integrations").then((m) => ({
    default: m.Integrations,
})));
const JourneyCanvas = lazy(() => import("../features/journeys/Canvas").then((m) => ({
    default: m.JourneyCanvas,
})));
const Explorer = lazy(() => import("../features/conversations/Explorer").then((m) => ({
    default: m.Explorer,
})));
const Comparison = lazy(() => import("../features/comparisons/Comparison").then((m) => ({
    default: m.Comparison,
})));
const ImportDialog = lazy(() => import("../features/imports/Import").then((m) => ({
    default: m.ImportDialog,
})));
const PublishDialog = lazy(() => import("../features/publications/Publish").then((m) => ({
    default: m.PublishDialog,
})));
const PublicJourney = lazy(() => import("../features/publications/Publish").then((m) => ({
    default: m.PublicJourney,
})));
import { BuildJourneyDialog } from "../features/journeys/BuildJourneyDialog";
import { Modal, Badge } from "../shared/ui/ui";
import "./styles.css";
import "./blueprint.css";
import { JourneyPreview, } from "../features/journeys/JourneyPreview";
function App() {
    const mobileMenu = useRef(null);
    useLocale();
    const [route, setRoute] = useState(location.hash.slice(1) || "home");
    const authRoute = route.split("?")[0];
    useEffect(() => {
        const publicLanding = ["home", "landing", "landing-content"].includes(route);
        let robots = document.querySelector('meta[name="robots"]');
        if (!robots) {
            robots = document.createElement("meta");
            robots.name = "robots";
            document.head.appendChild(robots);
        }
        robots.content = publicLanding ? "index, follow" : "noindex, nofollow";
        document.title = publicLanding
            ? "openhow — The story behind the work"
            : "openhow — Your workspace";
    }, [route]);
    const [user, setUser] = useState(null);
    const [ready, setReady] = useState(false);
    const [workspaces, setWorkspaces] = useState([]);
    const [summary, setSummary] = useState({ conversations: 0, journeys: 0, providers: [] });
    const [recent, setRecent] = useState([]);
    const [journeys, setJourneys] = useState([]);
    const [journeysCursor, setJourneysCursor] = useState(null);
    const [members, setMembers] = useState([]);
    const [notifications, setNotifications] = useState([]);
    const [importing, setImporting] = useState(false);
    const [create, setCreate] = useState(false);
    const [createWorkspace, setCreateWorkspace] = useState(false);
    const [name, setName] = useState("");
    const [publish, setPublish] = useState(null);
    const [revision, setRevision] = useState(0);
    const [online, setOnline] = useState([]);
    const { workspace, setWorkspace, theme, toggleTheme, toast, notify } = useApp();
    const signOut = () => act(async () => {
        await api("/logout", {});
        mobileMenu.current?.close();
        window.history.replaceState(null, "", location.pathname + location.search);
        setRoute("home");
        setUser(null);
    });
    const refresh = () => setRevision((v) => v + 1);
    const session = () => api("/me")
        .then(setUser)
        .catch(() => setUser(null))
        .finally(() => setReady(true));
    useEffect(() => {
        session();
        const f = () => setRoute(location.hash.slice(1) || "home");
        window.addEventListener("hashchange", f);
        return () => window.removeEventListener("hashchange", f);
    }, []);
    useEffect(() => {
        document.documentElement.dataset.theme = theme;
    }, [theme]);
    useEffect(() => {
        if (!user)
            return;
        act(async () => {
            const ws = await api("/workspaces");
            setWorkspaces(ws);
            if (!ws.some((w) => w.id === workspace)) {
                setWorkspace(ws[0]?.id || "");
                return;
            }
            const [s, l, j, m, n] = await Promise.all([
                api("/w/" + workspace + "/summary"),
                api("/w/" + workspace + "/library?limit=5"),
                api("/w/" + workspace + "/journeys"),
                api("/w/" + workspace + "/members"),
                api("/notifications"),
            ]);
            setSummary(s);
            setRecent(l.items);
            setJourneys(j.items);
            setJourneysCursor(j.nextCursor);
            setMembers(m);
            setNotifications(n);
        });
    }, [user, workspace, revision]);
    useEffect(() => {
        if (!user || !workspace)
            return;
        const connection = new HubConnectionBuilder()
            .withUrl("/hubs/collaboration")
            .withAutomaticReconnect()
            .build();
        connection.on("changed", refresh);
        connection.on("presence", setOnline);
        connection.onreconnected(() => {
            connection.invoke("Join", workspace);
            refresh();
        });
        connection
            .start()
            .then(() => connection.invoke("Join", workspace))
            .catch(() => { });
        return () => {
            void connection.stop();
        };
    }, [user, workspace]);
    if (route === "landing")
        return _jsx(LandingPage, {});
    if (route.startsWith("public/"))
        return _jsx(PublicJourney, { slug: route.split("/")[1] }, route);
    if (!ready)
        return _jsx("div", { className: "loading", children: t("Opening your workspace…") });
    if (!user && (route === "home" || route === "landing-content"))
        return _jsx(LandingPage, {});
    if (!user || ["confirm-email", "reset-password"].includes(authRoute))
        return (_jsxs(_Fragment, { children: [_jsx(AccountScreen, { route: route, done: () => {
                        if ([
                            "register",
                            "login",
                            "two-factor",
                            "reset-password",
                            "confirm-email",
                        ].includes(authRoute)) {
                            const returnTo = new URLSearchParams(route.split("?")[1] || "").get("returnTo");
                            go(returnTo && /^public\/[a-zA-Z0-9_-]+$/.test(returnTo)
                                ? returnTo
                                : "dashboard");
                        }
                        session();
                    } }, route), toast && _jsx("div", { className: "toast", children: toast })] }));
    const current = workspaces.find((w) => w.id === workspace);
    const canEdit = ["Owner", "Admin", "Editor"].includes(current?.role);
    const canComment = current?.role !== "Viewer";
    const section = ["home", "login", "register"].includes(route)
        ? "dashboard"
        : route.split("?")[0].split("/")[0];
    const id = route.split("/")[1]?.split("?")[0];
    const moreJourneys = () => journeysCursor &&
        act(async () => {
            const page = await api("/w/" +
                workspace +
                "/journeys?cursor=" +
                encodeURIComponent(journeysCursor));
            setJourneys((js) => [...js, ...page.items]);
            setJourneysCursor(page.nextCursor);
        });
    const nav = [
        ["dashboard", "Overview", LayoutDashboard],
        ["library", "Conversations", Library],
        ["journeys", "Journeys", GitBranch],
        ["compare", "Comparisons", BookOpen],
        ["workspaces", "Workspaces", Layers3],
        ["team", "Members & access", Users],
        ["integrations", "Integrations", Plug],
    ];
    return (_jsxs("div", { className: "app", children: [_jsx("dialog", { ref: mobileMenu, className: "mobile-navigation", "aria-label": t("Navigation menu"), onClick: (e) => {
                    if (e.target === e.currentTarget)
                        mobileMenu.current?.close();
                }, children: _jsxs("div", { className: "mobile-navigation-content", children: [_jsxs("header", { children: [_jsx("strong", { children: "openhow" }), _jsx("button", { type: "button", "aria-label": t("Close menu"), onClick: () => mobileMenu.current?.close(), children: _jsx(X, { size: 21 }) })] }), _jsxs("label", { children: [t("Workspace"), _jsx("select", { "aria-label": t("Mobile workspace"), value: workspace, onChange: (e) => {
                                        setWorkspace(e.target.value);
                                        go("dashboard");
                                        mobileMenu.current?.close();
                                    }, children: workspaces.map((w) => (_jsx("option", { value: w.id, children: w.name }, w.id))) })] }), _jsxs("button", { type: "button", onClick: () => {
                                mobileMenu.current?.close();
                                setCreateWorkspace(true);
                            }, children: [_jsx(Plus, { size: 17 }), t("Create workspace")] }), _jsxs("nav", { children: [nav.map(([key, label, Icon]) => (_jsxs("a", { href: "#" + key, "aria-current": section === key ? "page" : undefined, onClick: () => mobileMenu.current?.close(), children: [_jsx(Icon, { size: 18 }), t(label)] }, key))), _jsxs("a", { href: "#notifications", onClick: () => mobileMenu.current?.close(), children: [_jsx(Bell, { size: 18 }), t("Notifications")] }), _jsxs("a", { href: "#settings", onClick: () => mobileMenu.current?.close(), children: [_jsx(Settings, { size: 18 }), t("Settings")] })] }), _jsxs("footer", { children: [_jsx(LanguagePicker, {}), _jsx("button", { type: "button", onClick: toggleTheme, "aria-label": t("Toggle theme"), children: theme === "light" ? _jsx(Moon, { size: 18 }) : _jsx(Sun, { size: 18 }) }), _jsx("button", { type: "button", onClick: signOut, children: t("Sign out") })] })] }) }), _jsxs("aside", { className: "sidebar", children: [_jsxs("a", { className: "brand", href: "#dashboard", children: [_jsx("span", { className: "brand-icon", children: _jsx(BrandMark, { size: 21 }) }), "openhow", _jsx("span", { className: "beta", children: t("BETA") })] }), _jsxs("div", { className: "workspace-switcher", children: [_jsxs("label", { className: "workspace-picker", children: [_jsx("span", { className: "workspace-avatar", children: current?.name?.[0]?.toUpperCase() || "W" }), _jsx("select", { "aria-label": t("Workspace"), value: workspace, onChange: (e) => {
                                            setWorkspace(e.target.value);
                                            go("dashboard");
                                        }, children: workspaces.map((w) => (_jsx("option", { value: w.id, children: w.name }, w.id))) }), _jsx(ChevronDown, { size: 14, "aria-hidden": "true" })] }), _jsx("button", { className: "workspace-create", "aria-label": t("Create workspace"), title: t("Create workspace"), onClick: () => setCreateWorkspace(true), children: _jsx(Plus, { size: 17 }) })] }), _jsx("div", { className: "nav-label", children: t("WORKSPACE") }), _jsx("nav", { children: nav.map(([key, label, Icon]) => (_jsxs("a", { className: section === key ||
                                (key === "journeys" && section === "canvas") ||
                                (key === "library" && section === "explorer")
                                ? "active"
                                : "", href: "#" + key, children: [_jsx(Icon, { size: 18 }), t(label), key === "library" && (_jsx("span", { className: "count", children: formatNumber(summary.conversations) }))] }, key))) }), _jsxs("div", { className: "nav-label journeys-label", children: [t("YOUR JOURNEYS"), _jsx("button", { title: t("Create journey"), disabled: !canEdit, onClick: () => setCreate(true), children: _jsx(Plus, { size: 15 }) })] }), journeys.slice(0, 5).map((j) => (_jsxs("a", { className: "journey-nav", href: "#canvas/" + j.id, children: [_jsx("span", { className: "dot" }), j.title] }, j.id))), _jsxs("div", { className: "sidebar-bottom", children: [_jsxs("div", { className: "source-card", children: [_jsx(GitBranch, { size: 20 }), _jsx("strong", { children: t("Conversations become stories.") }), _jsx("p", { children: t("Your original sources stay intact. Your perspective connects them.") })] }), _jsxs("a", { href: "#notifications", children: [_jsx(Bell, { size: 17 }), " ", t("Notifications"), " ", _jsx("span", { className: "count", children: notifications.filter((n) => !n.read).length })] }), _jsxs("a", { href: "#settings", children: [_jsx(Settings, { size: 17 }), " ", t("Settings")] }), _jsxs("div", { className: "user", children: [_jsx("span", { className: "avatar", children: initials(user.name) }), _jsxs("div", { children: [_jsx("strong", { children: user.name }), _jsxs("small", { children: [t(current?.role || ""), " ", t("· Local account")] })] }), _jsx("button", { title: t("Sign out"), onClick: signOut, children: _jsx(LogOut, { size: 16 }) })] })] })] }), _jsxs("div", { className: "main", children: [_jsxs("header", { className: "topbar", children: [_jsxs("button", { className: "mobile-menu-toggle", type: "button", "aria-label": t("Open menu"), "aria-haspopup": "dialog", onClick: () => mobileMenu.current?.showModal(), children: [_jsx(Menu, { size: 22 }), _jsx("span", { children: "openhow" })] }), _jsxs("div", { children: [_jsx("span", { className: "muted", children: t("Workspace") }), _jsx("span", { className: "slash", children: "/" }), _jsx("strong", { children: section === "canvas"
                                            ? t("Journey canvas")
                                            : section === "explorer"
                                                ? t("Conversation explorer")
                                                : t(nav.find((x) => x[0] === section)?.[1] ||
                                                    (section === "settings"
                                                        ? "Settings"
                                                        : section === "notifications"
                                                            ? "Notifications"
                                                            : section)) })] }), _jsxs("div", { className: "row", children: [_jsx(LanguagePicker, {}), _jsxs("span", { className: "local-status", children: [_jsx("i", {}), " ", t("Local workspace")] }), _jsx("button", { title: t("Toggle theme"), onClick: toggleTheme, children: theme === "light" ? _jsx(Moon, { size: 17 }) : _jsx(Sun, { size: 17 }) }), _jsx("button", { title: t("Notifications"), onClick: () => go("notifications"), children: _jsx(Bell, { size: 17 }) }), _jsx("span", { className: "avatar small", children: initials(user.name) })] })] }), section === "canvas" && id ? (_jsx(CanvasRoute, { id: id, revision: revision, canEdit: canEdit, canPublish: ["Owner", "Admin"].includes(current?.role), online: online, onPublish: setPublish, refresh: refresh, user: user, members: members }, id)) : section === "explorer" && id ? (_jsx(Explorer, { id: id, user: user, members: members, canComment: canComment, revision: revision }, id + workspace)) : section === "compare" ? (_jsx(Comparison, { conversations: summary.conversations, canEdit: canEdit }, workspace)) : section === "library" ? (_jsx(LibraryView, { providers: summary.providers.map((p) => p.provider), revision: revision, importing: () => setImporting(true), canEdit: canEdit, refresh: refresh })) : section === "integrations" ? (_jsx(Integrations, { canEdit: canEdit, refresh: refresh }, workspace)) : section === "workspaces" ? (_jsx(Workspaces, { workspaces: workspaces, onCreate: () => setCreateWorkspace(true), refresh: refresh })) : section === "team" ? (current?.teamId ? (_jsx(Team, { members: members, role: current?.role, refresh: refresh })) : (_jsxs("section", { className: "page", children: [_jsx("span", { className: "eyebrow", children: t("PERSONAL WORKSPACE") }), _jsx("h1", { children: t("Members & access") }), _jsx("p", { children: t("This workspace is private to you. Create a team workspace when you want to collaborate.") }), _jsx("button", { className: "primary", onClick: () => go("workspaces"), children: t("Manage workspaces") })] }))) : section === "notifications" ? (_jsxs("div", { className: "page", children: [_jsx("h1", { children: t("Your inbox") }), _jsx("p", { className: "subtitle", children: t("Conversations that need your attention.") }), notifications.length === 0 && (_jsx("div", { className: "empty", children: t("You’re all caught up.") })), notifications.map((n) => (_jsxs("div", { className: "notification", children: [_jsx("span", { className: "dot " + (n.read ? "muted-dot" : "") }), _jsxs("div", { children: [_jsx("a", { href: "#" + n.link, children: n.text }), _jsx("small", { children: formatDate(n.createdAt) })] }), _jsx("button", { onClick: () => act(async () => {
                                            await api("/notifications/" + n.id, {}, "PATCH");
                                            refresh();
                                        }), children: n.read ? t("Mark unread") : t("Mark read") })] }, n.id)))] })) : section === "settings" ? (_jsxs("div", { className: "page", children: [_jsx("h1", { children: t("Workspace settings") }), _jsx(AccountSecurity, {}), _jsx(SanitizationSettings, {}), _jsxs("div", { className: "panel settings", children: [_jsx("h3", { children: t("Language preference") }), _jsx(LanguagePicker, {}), _jsx("p", { children: t("Language help") }), _jsx("h3", { children: t("Appearance") }), _jsx("p", { children: t("Make room for focused reading.") }), _jsx("button", { onClick: toggleTheme, children: t(theme === "light" ? "Use dark theme" : "Use light theme") }), _jsx("h3", { children: t("Privacy defaults") }), _jsx("p", { children: t("New journeys are private. Imports stay in the workspace you explicitly choose. Publishing creates a separate, read-only snapshot.") }), _jsx("h3", { children: t("Local data") }), _jsx("p", { children: t("Conversations, original files, discussions, and layouts are stored in SQLite on this computer.") }), _jsxs("a", { href: "/openapi/v1.json", target: "_blank", children: [t("OpenAPI specification"), " ", _jsx(ArrowUpRight, { size: 14 })] })] })] })) : (_jsxs("section", { className: "page", children: [_jsxs("div", { className: "page-heading", children: [_jsxs("div", { children: [_jsx("span", { className: "eyebrow", children: section === "journeys"
                                                    ? t("THE BIGGER PICTURE")
                                                    : t("YOUR IDEAS, CONNECTED") }), _jsx("h1", { children: section === "journeys"
                                                    ? t("Your journeys")
                                                    : t("Good to see you, {name}.", {
                                                        name: user.name.split(" ")[0],
                                                    }) }), _jsx("p", { className: "subtitle", children: section === "journeys"
                                                    ? t("Curate the path from first question to final result.")
                                                    : t("Pick up a thread. Follow an idea. See how it all connects.") })] }), _jsxs("button", { className: "primary", disabled: !canEdit, onClick: () => setCreate(true), children: [_jsx(Plus, { size: 17 }), t("New journey")] })] }), section !== "journeys" && (_jsxs(_Fragment, { children: [_jsxs("div", { className: "stats", children: [_jsxs("div", { children: [_jsx(Library, {}), _jsx("strong", { children: formatNumber(summary.conversations) }), _jsx("span", { children: t("Conversations") })] }), _jsxs("div", { children: [_jsx(GitBranch, {}), _jsx("strong", { children: formatNumber(summary.journeys) }), _jsx("span", { children: t("Connected journeys") })] }), _jsxs("div", { children: [_jsx(Command, {}), _jsx("strong", { children: formatNumber(summary.providers.length) }), _jsx("span", { children: t("AI perspectives") })] }), _jsxs("div", { children: [_jsx(Users, {}), _jsx("strong", { children: formatNumber(members.filter((member) => member.id !== user.id).length) }), _jsx("span", { children: t("Collaborators") })] })] }), _jsxs("div", { className: "section-title", children: [_jsx("h2", { children: t("Continue your journey") }), _jsxs("a", { href: "#journeys", children: [t("View all"), " ", _jsx(ArrowRight, { size: 14 })] })] })] })), _jsx("div", { className: "journey-grid " +
                                    (section === "journeys" ? "" : "overview-journeys"), children: journeys.map((j, i) => (_jsxs("a", { className: "journey-card " + (i === 0 ? "featured" : ""), href: "#canvas/" + j.id, children: [_jsxs("div", { className: "mini-graph", children: [_jsx(JourneyPreview, { journey: j }), _jsx("span", { className: "graph-badge", children: j.visibility === "Private"
                                                        ? t("Private draft")
                                                        : t("Team journey") })] }), _jsxs("div", { className: "journey-card-body", children: [_jsxs("div", { className: "row", children: [_jsx("span", { className: "eyebrow", children: i === 0 ? t("FEATURED JOURNEY") : t("IN PROGRESS") }), _jsx(ArrowUpRight, { size: 18 })] }), _jsx("h2", { children: j.title }), _jsx("p", { children: j.description ||
                                                        t("A new story waiting to be connected.") }), _jsxs("footer", { children: [_jsxs("div", { className: "provider-dots", children: [_jsx("span", { children: "\u25C9" }), _jsx("span", { children: "\u2733" }), _jsx("span", { children: "\u2318" })] }), _jsx("span", { children: count("nodes", j.nodeCount) }), _jsx("span", { className: "spacer" }), _jsx("span", { className: "avatar tiny", children: initials(user.name) })] })] })] }, j.id))) }), section === "journeys" && journeysCursor && (_jsx("button", { className: "load-more", onClick: moreJourneys, children: t("Load more") })), section !== "journeys" && (_jsxs(_Fragment, { children: [_jsxs("div", { className: "section-title", children: [_jsx("h2", { children: t("Recently added conversations") }), _jsxs("a", { href: "#library", children: [t("Open library"), " ", _jsx(ArrowRight, { size: 14 })] })] }), _jsx(ConversationTable, { items: recent }), _jsxs("div", { className: "import-banner", children: [_jsx("div", { className: "upload-icon", children: _jsx(Upload, {}) }), _jsxs("div", { children: [_jsx("h3", { children: t("Your next journey starts with a conversation.") }), _jsx("p", { children: t("Bring your ChatGPT, Claude, or other AI conversations into one place.") })] }), _jsxs("button", { disabled: !canEdit, onClick: () => setImporting(true), children: [t("Import conversations"), " ", _jsx(ArrowRight, { size: 16 })] })] })] })), journeys.length === 0 && (_jsx("div", { className: "empty", children: t("Create a journey, then add conversations from your library.") }))] }))] }), createWorkspace && (_jsx(CreateWorkspaceDialog, { close: () => setCreateWorkspace(false), created: (w) => {
                    setWorkspaces((ws) => [...ws, w]);
                    setWorkspace(w.id);
                    setSummary({ conversations: 0, journeys: 0, providers: [] });
                    setRecent([]);
                    setJourneys([]);
                    setMembers([]);
                    setCreateWorkspace(false);
                    refresh();
                    go("workspaces");
                    notify(t("Workspace created. You can now import conversations or create journeys."));
                } })), importing && (_jsx(ImportDialog, { close: () => setImporting(false), done: refresh, workspaces: workspaces })), " ", create && (_jsx(Modal, { title: t("Start a new journey"), onClose: () => setCreate(false), children: _jsxs("form", { onSubmit: (e) => {
                        e.preventDefault();
                        act(async () => {
                            const j = await api("/w/" + workspace + "/journeys", { name });
                            setCreate(false);
                            setName("");
                            refresh();
                            go("canvas/" + j.id);
                        });
                    }, children: [_jsxs("label", { children: [t("Journey title"), _jsx("input", { autoFocus: true, value: name, onChange: (e) => setName(e.target.value), placeholder: t("What are you building or exploring?"), required: true })] }), _jsx("p", { className: "muted", children: t("Your journey starts private. Add conversations without changing the originals.") }), _jsxs("button", { className: "primary", children: [t("Create journey"), " ", _jsx(ArrowRight, { size: 16 })] })] }) })), publish && (_jsx(PublishDialog, { journey: publish, close: () => setPublish(null) })), " ", toast && (_jsxs("div", { className: "toast", role: "status", children: [toast, _jsx("button", { "aria-label": t("Dismiss"), onClick: () => notify(""), children: _jsx(X, { size: 14 }) })] }))] }));
}
function ConversationTable({ items, canEdit = false, confirm = "", setConfirm = () => { }, onDelete = () => { }, selected, onSelect, }) {
    useLocale();
    return (_jsxs("div", { className: "conversation-table" + (canEdit ? " can-edit" : ""), children: [_jsxs("div", { className: "table-head", children: [_jsx("span", { children: t("CONVERSATION") }), _jsx("span", { children: t("PROVIDER") }), _jsx("span", { children: t("MESSAGES") }), _jsx("span", { children: t("SOURCE") }), canEdit && _jsx("span", { className: "sr-only", children: t("Delete") })] }), items.map((c) => (_jsxs("div", { className: "table-row", children: [_jsxs("div", { className: "conversation-title-cell", children: [onSelect && (_jsx("input", { type: "checkbox", checked: !!selected?.[c.id], "aria-label": t("Select {title}", { title: c.title }), onChange: () => onSelect(c) })), _jsxs("a", { href: "#explorer/" + c.id, children: [_jsx("span", { className: "conversation-icon " + providerClass(c.provider), children: _jsx(BookOpen, { size: 17 }) }), _jsxs("div", { children: [_jsx("strong", { children: c.title }), _jsxs("small", { children: [c.model || t("Model not provided"), c.demo ? t(" · Demonstration") : ""] })] })] })] }), _jsx(Badge, { provider: c.provider }), _jsx("span", { children: count("messages", c.messageCount) }), _jsx("span", { className: "muted", children: c.sourceFormat }), canEdit && (_jsx("span", { className: "row-actions", children: confirm === c.id ? (_jsxs(_Fragment, { children: [_jsx("button", { className: "danger", onClick: () => onDelete(c.id), children: t("Delete") }), _jsx("button", { "aria-label": t("Dismiss"), onClick: () => setConfirm(""), children: _jsx(X, { size: 14 }) })] })) : (_jsx("button", { "aria-label": t("Delete conversation"), title: t("Delete conversation"), onClick: () => setConfirm(c.id), children: _jsx(Trash2, { size: 15 }) })) }))] }, c.id))), items.length === 0 && (_jsx("div", { className: "empty", children: t("No conversations found. Import a source to get started.") }))] }));
}
function CanvasRoute({ id, revision, onPublish, ...props }) {
    useLocale();
    const [journey, setJourney] = useState(null);
    useEffect(() => {
        act(async () => setJourney(await api("/journeys/" + id)));
    }, [id, revision]);
    if (!journey)
        return _jsx("div", { className: "loading", children: t("Opening journey…") });
    return (_jsx(JourneyCanvas, { journey: journey, onPublish: () => onPublish(journey), ...props }));
}
function groupConversations(items) {
    const groups = new Map();
    for (const item of items) {
        const id = item.applicationProjectId || "";
        const name = id ? item.projectName || t("No project") : t("No project");
        const group = groups.get(id) ?? { id, name, items: [] };
        group.items.push(item);
        groups.set(id, group);
    }
    return [...groups.values()].sort((a, b) => {
        if (!a.id)
            return 1;
        if (!b.id)
            return -1;
        return a.name.localeCompare(b.name);
    });
}
function LibraryView({ providers, revision, importing, canEdit, refresh, }) {
    useLocale();
    const [q, setQ] = useState("");
    const [project, setProject] = useState("");
    const [projects, setProjects] = useState([]);
    const [provider, setProvider] = useState("");
    const [sort, setSort] = useState("");
    const [confirm, setConfirm] = useState("");
    const [confirmProject, setConfirmProject] = useState("");
    const [selected, setSelected] = useState({});
    const [building, setBuilding] = useState(false);
    const toggle = (c) => setSelected((current) => {
        const next = { ...current };
        if (next[c.id])
            delete next[c.id];
        else if (Object.keys(next).length < 100)
            next[c.id] = { id: c.id, title: c.title };
        return next;
    });
    const { workspace, notify } = useApp();
    useEffect(() => {
        setSelected({});
        setBuilding(false);
        setConfirmProject("");
    }, [workspace]);
    const results = useLibrary(workspace, { q, provider, project, sort }, revision);
    const groups = groupConversations(results.items);
    const remove = (id) => act(async () => {
        await api("/conversations/" + id, {}, "DELETE");
        setConfirm("");
        setSelected((current) => {
            const next = { ...current };
            delete next[id];
            return next;
        });
        notify(t("Conversation deleted"));
        refresh();
    });
    const removeProject = (group) => act(async () => {
        setConfirmProject("");
        const result = await api("/w/" + workspace + "/projects/" + group.id + "/conversations", undefined, "DELETE");
        const removed = new Set(group.items.map((item) => item.id));
        setSelected((current) => {
            const next = { ...current };
            for (const id of removed)
                delete next[id];
            return next;
        });
        if (removed.has(confirm))
            setConfirm("");
        notify(t("Deleted {count} conversations", { count: result.deleted }));
        refresh();
    });
    useEffect(() => {
        act(async () => setProjects(await api("/w/" + workspace + "/projects")));
    }, [workspace, revision]);
    return (_jsxs("section", { className: "page", children: [_jsxs("div", { className: "page-heading", children: [_jsxs("div", { children: [_jsx("span", { className: "eyebrow", children: t("THE SOURCE MATERIAL") }), _jsx("h1", { children: t("Conversation library") }), _jsx("p", { className: "subtitle", children: t("Original conversations. Every perspective, in one place.") })] }), _jsxs("button", { className: "primary", disabled: !canEdit, onClick: importing, children: [_jsx(Upload, { size: 17 }), t("Import conversations")] })] }), _jsxs("div", { className: "filterbar", children: [_jsxs("label", { className: "search", children: [_jsx(Search, { size: 17 }), _jsx("input", { placeholder: t("Search conversations, messages, code, comments…"), value: q, onChange: (e) => setQ(e.target.value) })] }), _jsxs("select", { "aria-label": t("Provider filter"), value: provider, onChange: (e) => setProvider(e.target.value), children: [_jsx("option", { value: "", children: t("All providers") }), providers.map((p) => (_jsx("option", { children: p }, p)))] }), _jsxs("select", { "aria-label": t("Application project filter"), value: project, onChange: (e) => setProject(e.target.value), children: [_jsx("option", { value: "", children: t("All projects") }), projects.map((p) => (_jsx("option", { value: p.id, children: p.name }, p.id)))] }), _jsxs("select", { "aria-label": t("Sort"), value: sort, onChange: (e) => setSort(e.target.value), children: [_jsx("option", { value: "", children: t("Newest") }), _jsx("option", { value: "messages", children: t("Message count") })] }), _jsx("span", { className: "muted", children: count("conversations", results.items.length) })] }), canEdit && (_jsxs("div", { className: "journey-selection-bar", children: [_jsx("button", { disabled: !results.items.length || results.loading, onClick: () => setSelected((current) => {
                            const next = { ...current };
                            for (const c of results.items) {
                                if (Object.keys(next).length >= 100)
                                    break;
                                next[c.id] = { id: c.id, title: c.title };
                            }
                            return next;
                        }), children: t("Select loaded conversations") }), _jsx("span", { role: "status", children: t("{count} conversations selected", {
                            count: Object.keys(selected).length,
                        }) }), !!Object.keys(selected).length && (_jsx("button", { onClick: () => setSelected({}), children: t("Clear selection") })), _jsx("span", { className: "muted", children: t("Select up to 100. Selection is kept across filters.") }), _jsxs("button", { className: "primary", disabled: !Object.keys(selected).length, onClick: () => setBuilding(true), children: [_jsx(GitBranch, { size: 16 }), t("Build journey")] })] })), building && canEdit && (_jsx(BuildJourneyDialog, { selected: Object.values(selected), close: () => setBuilding(false), refresh: refresh })), results.items.length === 0 ? (_jsx(ConversationTable, { items: [], canEdit: canEdit, confirm: confirm, setConfirm: setConfirm, onDelete: remove })) : (groups.map((group) => (_jsxs("section", { className: "library-group", children: [_jsxs("div", { className: "library-group-heading", children: [_jsxs("h2", { children: [group.name, _jsx("span", { children: count("conversations", group.items.length) })] }), canEdit && group.id && (confirmProject === group.id ? (_jsxs("div", { className: "confirm-delete", role: "alert", children: [_jsx("span", { children: t("Delete every conversation in {name}? This cannot be undone.", { name: group.name }) }), _jsx("button", { className: "danger", onClick: () => removeProject(group), children: t("Delete all") }), _jsx("button", { "aria-label": t("Dismiss"), onClick: () => setConfirmProject(""), children: _jsx(X, { size: 14 }) })] })) : (_jsxs("button", { onClick: () => setConfirmProject(group.id), children: [_jsx(Trash2, { size: 14 }), t("Delete all conversations")] })))] }), _jsx(ConversationTable, { items: group.items, selected: selected, onSelect: canEdit ? toggle : undefined, canEdit: canEdit, confirm: confirm, setConfirm: setConfirm, onDelete: remove })] }, group.id || "none")))), results.more && (_jsx("button", { className: "load-more", disabled: results.loading, onClick: results.more, children: t("Load more") }))] }));
}
function Team({ members, role, refresh, }) {
    useLocale();
    const { workspace, notify, setWorkspace } = useApp();
    const [email, setEmail] = useState("");
    const [inviteRole, setRole] = useState("Editor");
    const [token, setToken] = useState("");
    const [accept, setAccept] = useState("");
    const [activity, setActivity] = useState([]);
    const manage = ["Owner", "Admin"].includes(role);
    useEffect(() => {
        act(async () => setActivity(await api("/w/" + workspace + "/activity")));
    }, [workspace, members]);
    return (_jsxs("section", { className: "page", children: [_jsx("span", { className: "eyebrow", children: t("BETTER TOGETHER") }), _jsx("h1", { children: t("Members & access") }), _jsx("p", { className: "subtitle", children: t("A shared understanding of how the work happened.") }), _jsxs("div", { className: "two-col", children: [_jsxs("div", { className: "panel", children: [_jsxs("div", { className: "section-title", children: [_jsx("h2", { children: t("People in this workspace") }), manage && (_jsx("button", { onClick: () => {
                                            const name = prompt(t("Workspace name"));
                                            if (name)
                                                act(async () => {
                                                    await api("/w/" + workspace, { name }, "PUT");
                                                    refresh();
                                                });
                                        }, children: t("Rename") })), role !== "Owner" && (_jsx("button", { onClick: () => act(async () => {
                                            await api("/w/" + workspace + "/leave", {});
                                            setWorkspace("");
                                            refresh();
                                        }), children: t("Leave workspace") }))] }), members.map((m) => (_jsxs("div", { className: "member", children: [_jsx("span", { className: "avatar", children: initials(m.name) }), _jsxs("div", { children: [_jsx("strong", { children: m.name }), _jsx("small", { children: m.email })] }), _jsx("select", { "aria-label": t("Role for {name}", { name: m.name }), disabled: !manage || m.role === "Owner", value: m.role, onChange: (e) => act(async () => {
                                            await api("/w/" + workspace + "/members/" + m.id, { role: e.target.value }, "PUT");
                                            refresh();
                                        }), children: [
                                            "Owner",
                                            "Admin",
                                            "Editor",
                                            "Commenter",
                                            "Viewer",
                                            "Remove",
                                        ].map((r) => (_jsx("option", { value: r, children: t(r) }, r))) })] }, m.id))), manage && (_jsxs("form", { onSubmit: (e) => {
                                    e.preventDefault();
                                    act(async () => {
                                        const v = await api("/w/" + workspace + "/invitations", {
                                            email,
                                            role: inviteRole,
                                        });
                                        setToken(v.token);
                                        notify(t("Invitation created. Share the token with the intended teammate."));
                                    });
                                }, children: [_jsx("h3", { children: t("Invite a teammate") }), _jsxs("label", { children: [t("Email"), _jsx("input", { type: "email", required: true, value: email, onChange: (e) => setEmail(e.target.value) })] }), _jsxs("div", { className: "row", children: [_jsx("select", { value: inviteRole, onChange: (e) => setRole(e.target.value), children: [
                                                    "Editor",
                                                    "Commenter",
                                                    "Viewer",
                                                    ...(role === "Owner" ? ["Admin"] : []),
                                                ].map((r) => (_jsx("option", { value: r, children: t(r) }, r))) }), _jsx("button", { className: "primary", children: t("Create invitation") })] }), token && (_jsxs("p", { className: "token", children: [t("Invitation token (valid for 7 days):"), " ", _jsx("code", { children: token })] })), _jsx("small", { children: t("Email delivery is deferred. Only the account with this email can accept.") })] }))] }), _jsxs("div", { children: [_jsxs("div", { className: "panel", children: [_jsx("h2", { children: t("Recent activity") }), activity.map((a) => (_jsxs("div", { className: "activity", children: [_jsx("span", { className: "dot" }), _jsxs("div", { children: [a.text, _jsx("small", { children: formatDate(a.createdAt) })] })] }, a.id)))] }), _jsxs("div", { className: "panel", children: [_jsx("h3", { children: t("Join a workspace") }), _jsxs("form", { onSubmit: (e) => {
                                            e.preventDefault();
                                            act(async () => {
                                                await api("/invitations/" + accept + "/accept", {});
                                                refresh();
                                                notify(t("Joined workspace"));
                                            });
                                        }, children: [_jsx("input", { "aria-label": t("Invitation token"), placeholder: t("Paste invitation token"), value: accept, onChange: (e) => setAccept(e.target.value), required: true }), _jsx("button", { children: t("Accept invitation") })] }), _jsx("a", { className: "button", href: "#workspaces", children: t("Create or manage workspaces →") })] })] })] })] }));
}
createRoot(document.getElementById("root")).render(_jsx(React.StrictMode, { children: _jsx(Suspense, { fallback: _jsx("div", { className: "loading", children: t("Opening journey…") }), children: _jsx(App, {}) }) }));
