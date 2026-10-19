import { AccountScreen, AccountSecurity } from "../features/accounts/Accounts";
import { SanitizationSettings } from "../features/imports/SanitizationSettings";
import { BrandMark } from "../shared/ui/BrandMark";
import { LandingPage } from "../features/landing/LandingPage";
import {
  t,
  useLocale,
  formatNumber,
  formatDate,
  count,
} from "../shared/i18n/i18n";
import { LanguagePicker } from "../shared/i18n/LanguagePicker";
import {
  Workspaces,
  CreateWorkspaceDialog,
} from "../features/workspaces/Workspaces";
import React, { useEffect, useState, useRef, lazy, Suspense } from "react";
import { createRoot } from "react-dom/client";
import { HubConnectionBuilder } from "@microsoft/signalr";
import {
  Plug,
  Menu,
  ArrowUpRight,
  BookOpen,
  GitBranch,
  LayoutDashboard,
  Layers3,
  Library,
  Users,
  Bell,
  Settings,
  Plus,
  Search,
  ChevronDown,
  Sun,
  Moon,
  LogOut,
  ArrowRight,
  Upload,
  Command,
  Check,
  X,
  Trash2,
} from "lucide-react";
import {
  api,
  useApp,
  go,
  act,
  initials,
  providerClass,
  useLibrary,
  type Page,
} from "../shared/state";
const Integrations = lazy(() =>
  import("../features/integrations/Integrations").then((m) => ({
    default: m.Integrations,
  })),
);
const JourneyCanvas = lazy(() =>
  import("../features/journeys/Canvas").then((m) => ({
    default: m.JourneyCanvas,
  })),
);
const Explorer = lazy(() =>
  import("../features/conversations/Explorer").then((m) => ({
    default: m.Explorer,
  })),
);
const Comparison = lazy(() =>
  import("../features/comparisons/Comparison").then((m) => ({
    default: m.Comparison,
  })),
);
const ImportDialog = lazy(() =>
  import("../features/imports/Import").then((m) => ({
    default: m.ImportDialog,
  })),
);
const PublishDialog = lazy(() =>
  import("../features/publications/Publish").then((m) => ({
    default: m.PublishDialog,
  })),
);
const PublicJourney = lazy(() =>
  import("../features/publications/Publish").then((m) => ({
    default: m.PublicJourney,
  })),
);
import { BuildJourneyDialog } from "../features/journeys/BuildJourneyDialog";
import { Modal, Badge } from "../shared/ui/ui";
import "./styles.css";
import "./blueprint.css";
import {
  JourneyPreview,
  BlueprintIllustration,
} from "../features/journeys/JourneyPreview";
function App() {
  const mobileMenu = useRef<HTMLDialogElement>(null);
  useLocale();
  const [route, setRoute] = useState(location.hash.slice(1) || "home");
  const authRoute = route.split("?")[0];
  useEffect(() => {
    const publicLanding = ["home", "landing", "landing-content"].includes(
      route,
    );
    let robots = document.querySelector<HTMLMetaElement>('meta[name="robots"]');
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
  const [user, setUser] = useState<any>(null);
  const [ready, setReady] = useState(false);
  const [workspaces, setWorkspaces] = useState<any[]>([]);
  const [summary, setSummary] = useState<{
    conversations: number;
    journeys: number;
    providers: { provider: string; count: number }[];
  }>({ conversations: 0, journeys: 0, providers: [] });
  const [recent, setRecent] = useState<any[]>([]);
  const [journeys, setJourneys] = useState<any[]>([]);
  const [journeysCursor, setJourneysCursor] = useState<string | null>(null);
  const [members, setMembers] = useState<any[]>([]);
  const [notifications, setNotifications] = useState<any[]>([]);
  const [importing, setImporting] = useState(false);
  const [create, setCreate] = useState(false);
  const [createWorkspace, setCreateWorkspace] = useState(false);
  const [name, setName] = useState("");
  const [publish, setPublish] = useState<any>(null);
  const [revision, setRevision] = useState(0);
  const [online, setOnline] = useState<string[]>([]);
  const { workspace, setWorkspace, theme, toggleTheme, toast, notify } =
    useApp();
  const signOut = () =>
    act(async () => {
      await api("/logout", {});
      mobileMenu.current?.close();
      window.history.replaceState(
        null,
        "",
        location.pathname + location.search,
      );
      setRoute("home");
      setUser(null);
    });
  const refresh = () => setRevision((v) => v + 1);
  const session = () =>
    api("/me")
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
    if (!user) return;
    act(async () => {
      const ws = await api("/workspaces");
      setWorkspaces(ws);
      if (!ws.some((w: any) => w.id === workspace)) {
        setWorkspace(ws[0]?.id || "");
        return;
      }
      const [s, l, j, m, n] = await Promise.all([
        api("/w/" + workspace + "/summary"),
        api<Page>("/w/" + workspace + "/library?limit=5"),
        api<Page>("/w/" + workspace + "/journeys"),
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
    if (!user || !workspace) return;
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
      .catch(() => {});
    return () => {
      void connection.stop();
    };
  }, [user, workspace]);
  if (route === "landing") return <LandingPage />;
  if (route.startsWith("public/"))
    return <PublicJourney key={route} slug={route.split("/")[1]} />;
  if (!ready)
    return <div className="loading">{t("Opening your workspace…")}</div>;
  if (!user && (route === "home" || route === "landing-content"))
    return <LandingPage />;
  if (!user || ["confirm-email", "reset-password"].includes(authRoute))
    return (
      <>
        <AccountScreen
          key={route}
          route={route}
          done={() => {
            if (
              [
                "register",
                "login",
                "two-factor",
                "reset-password",
                "confirm-email",
              ].includes(authRoute)
            ) {
              const returnTo = new URLSearchParams(
                route.split("?")[1] || "",
              ).get("returnTo");
              go(
                returnTo && /^public\/[a-zA-Z0-9_-]+$/.test(returnTo)
                  ? returnTo
                  : "dashboard",
              );
            }
            session();
          }}
        />
        {toast && <div className="toast">{toast}</div>}
      </>
    );
  const current = workspaces.find((w) => w.id === workspace);
  const canEdit = ["Owner", "Admin", "Editor"].includes(current?.role);
  const canComment = current?.role !== "Viewer";
  const section = ["home", "login", "register"].includes(route)
    ? "dashboard"
    : route.split("?")[0].split("/")[0];
  const id = route.split("/")[1]?.split("?")[0];
  const moreJourneys = () =>
    journeysCursor &&
    act(async () => {
      const page = await api<Page>(
        "/w/" +
          workspace +
          "/journeys?cursor=" +
          encodeURIComponent(journeysCursor),
      );
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
  ] as const;
  return (
    <div className="app">
      <dialog
        ref={mobileMenu}
        className="mobile-navigation"
        aria-label={t("Navigation menu")}
        onClick={(e) => {
          if (e.target === e.currentTarget) mobileMenu.current?.close();
        }}
      >
        <div className="mobile-navigation-content">
          <header>
            <strong>openhow</strong>
            <button
              type="button"
              aria-label={t("Close menu")}
              onClick={() => mobileMenu.current?.close()}
            >
              <X size={21} />
            </button>
          </header>
          <label>
            {t("Workspace")}
            <select
              aria-label={t("Mobile workspace")}
              value={workspace}
              onChange={(e) => {
                setWorkspace(e.target.value);
                go("dashboard");
                mobileMenu.current?.close();
              }}
            >
              {workspaces.map((w) => (
                <option value={w.id} key={w.id}>
                  {w.name}
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            onClick={() => {
              mobileMenu.current?.close();
              setCreateWorkspace(true);
            }}
          >
            <Plus size={17} />
            {t("Create workspace")}
          </button>
          <nav>
            {nav.map(([key, label, Icon]) => (
              <a
                href={"#" + key}
                key={key}
                aria-current={section === key ? "page" : undefined}
                onClick={() => mobileMenu.current?.close()}
              >
                <Icon size={18} />
                {t(label)}
              </a>
            ))}
            <a
              href="#notifications"
              onClick={() => mobileMenu.current?.close()}
            >
              <Bell size={18} />
              {t("Notifications")}
            </a>
            <a href="#settings" onClick={() => mobileMenu.current?.close()}>
              <Settings size={18} />
              {t("Settings")}
            </a>
          </nav>
          <footer>
            <LanguagePicker />
            <button
              type="button"
              onClick={toggleTheme}
              aria-label={t("Toggle theme")}
            >
              {theme === "light" ? <Moon size={18} /> : <Sun size={18} />}
            </button>
            <button type="button" onClick={signOut}>
              {t("Sign out")}
            </button>
          </footer>
        </div>
      </dialog>
      <aside className="sidebar">
        <a className="brand" href="#dashboard">
          <span className="brand-icon">
            <BrandMark size={21} />
          </span>
          openhow<span className="beta">{t("BETA")}</span>
        </a>
        <div className="workspace-switcher">
          <label className="workspace-picker">
            <span className="workspace-avatar">
              {current?.name?.[0]?.toUpperCase() || "W"}
            </span>
            <select
              aria-label={t("Workspace")}
              value={workspace}
              onChange={(e) => {
                setWorkspace(e.target.value);
                go("dashboard");
              }}
            >
              {workspaces.map((w) => (
                <option value={w.id} key={w.id}>
                  {w.name}
                </option>
              ))}
            </select>
            <ChevronDown size={14} aria-hidden="true" />
          </label>
          <button
            className="workspace-create"
            aria-label={t("Create workspace")}
            title={t("Create workspace")}
            onClick={() => setCreateWorkspace(true)}
          >
            <Plus size={17} />
          </button>
        </div>
        <div className="nav-label">{t("WORKSPACE")}</div>
        <nav>
          {nav.map(([key, label, Icon]) => (
            <a
              key={key}
              className={
                section === key ||
                (key === "journeys" && section === "canvas") ||
                (key === "library" && section === "explorer")
                  ? "active"
                  : ""
              }
              href={"#" + key}
            >
              <Icon size={18} />
              {t(label)}
              {key === "library" && (
                <span className="count">
                  {formatNumber(summary.conversations)}
                </span>
              )}
            </a>
          ))}
        </nav>
        <div className="nav-label journeys-label">
          {t("YOUR JOURNEYS")}
          <button
            title={t("Create journey")}
            disabled={!canEdit}
            onClick={() => setCreate(true)}
          >
            <Plus size={15} />
          </button>
        </div>
        {journeys.slice(0, 5).map((j) => (
          <a className="journey-nav" key={j.id} href={"#canvas/" + j.id}>
            <span className="dot" />
            {j.title}
          </a>
        ))}
        <div className="sidebar-bottom">
          <div className="source-card">
            <GitBranch size={20} />
            <strong>{t("Conversations become stories.")}</strong>
            <p>
              {t(
                "Your original sources stay intact. Your perspective connects them.",
              )}
            </p>
          </div>
          <a href="#notifications">
            <Bell size={17} /> {t("Notifications")}{" "}
            <span className="count">
              {notifications.filter((n) => !n.read).length}
            </span>
          </a>
          <a href="#settings">
            <Settings size={17} /> {t("Settings")}
          </a>
          <div className="user">
            <span className="avatar">{initials(user.name)}</span>
            <div>
              <strong>{user.name}</strong>
              <small>
                {t(current?.role || "")} {t("· Local account")}
              </small>
            </div>
            <button title={t("Sign out")} onClick={signOut}>
              <LogOut size={16} />
            </button>
          </div>
        </div>
      </aside>
      <div className="main">
        <header className="topbar">
          <button
            className="mobile-menu-toggle"
            type="button"
            aria-label={t("Open menu")}
            aria-haspopup="dialog"
            onClick={() => mobileMenu.current?.showModal()}
          >
            <Menu size={22} />
            <span>openhow</span>
          </button>
          <div>
            <span className="muted">{t("Workspace")}</span>
            <span className="slash">/</span>
            <strong>
              {section === "canvas"
                ? t("Journey canvas")
                : section === "explorer"
                  ? t("Conversation explorer")
                  : t(
                      nav.find((x) => x[0] === section)?.[1] ||
                        (section === "settings"
                          ? "Settings"
                          : section === "notifications"
                            ? "Notifications"
                            : section),
                    )}
            </strong>
          </div>
          <div className="row">
            <LanguagePicker />
            <span className="local-status">
              <i /> {t("Local workspace")}
            </span>
            <button title={t("Toggle theme")} onClick={toggleTheme}>
              {theme === "light" ? <Moon size={17} /> : <Sun size={17} />}
            </button>
            <button
              title={t("Notifications")}
              onClick={() => go("notifications")}
            >
              <Bell size={17} />
            </button>
            <span className="avatar small">{initials(user.name)}</span>
          </div>
        </header>
        {section === "canvas" && id ? (
          <CanvasRoute
            key={id}
            id={id}
            revision={revision}
            canEdit={canEdit}
            canPublish={["Owner", "Admin"].includes(current?.role)}
            online={online}
            onPublish={setPublish}
            refresh={refresh}
            user={user}
            members={members}
          />
        ) : section === "explorer" && id ? (
          <Explorer
            key={id + workspace}
            id={id}
            user={user}
            members={members}
            canComment={canComment}
            revision={revision}
          />
        ) : section === "compare" ? (
          <Comparison
            key={workspace}
            conversations={summary.conversations}
            canEdit={canEdit}
          />
        ) : section === "library" ? (
          <LibraryView
            providers={summary.providers.map((p) => p.provider)}
            revision={revision}
            importing={() => setImporting(true)}
            canEdit={canEdit}
            refresh={refresh}
          />
        ) : section === "integrations" ? (
          <Integrations key={workspace} canEdit={canEdit} refresh={refresh} />
        ) : section === "workspaces" ? (
          <Workspaces
            workspaces={workspaces}
            onCreate={() => setCreateWorkspace(true)}
            refresh={refresh}
          />
        ) : section === "team" ? (
          current?.teamId ? (
            <Team members={members} role={current?.role} refresh={refresh} />
          ) : (
            <section className="page">
              <span className="eyebrow">{t("PERSONAL WORKSPACE")}</span>
              <h1>{t("Members & access")}</h1>
              <p>
                {t(
                  "This workspace is private to you. Create a team workspace when you want to collaborate.",
                )}
              </p>
              <button className="primary" onClick={() => go("workspaces")}>
                {t("Manage workspaces")}
              </button>
            </section>
          )
        ) : section === "notifications" ? (
          <div className="page">
            <h1>{t("Your inbox")}</h1>
            <p className="subtitle">
              {t("Conversations that need your attention.")}
            </p>
            {notifications.length === 0 && (
              <div className="empty">{t("You’re all caught up.")}</div>
            )}
            {notifications.map((n) => (
              <div className="notification" key={n.id}>
                <span className={"dot " + (n.read ? "muted-dot" : "")} />
                <div>
                  <a href={"#" + n.link}>{n.text}</a>
                  <small>{formatDate(n.createdAt)}</small>
                </div>
                <button
                  onClick={() =>
                    act(async () => {
                      await api("/notifications/" + n.id, {}, "PATCH");
                      refresh();
                    })
                  }
                >
                  {n.read ? t("Mark unread") : t("Mark read")}
                </button>
              </div>
            ))}
          </div>
        ) : section === "settings" ? (
          <div className="page">
            <h1>{t("Workspace settings")}</h1>
            <AccountSecurity />
            <SanitizationSettings />
            <div className="panel settings">
              <h3>{t("Language preference")}</h3>
              <LanguagePicker />
              <p>{t("Language help")}</p>
              <h3>{t("Appearance")}</h3>
              <p>{t("Make room for focused reading.")}</p>
              <button onClick={toggleTheme}>
                {t(theme === "light" ? "Use dark theme" : "Use light theme")}
              </button>
              <h3>{t("Privacy defaults")}</h3>
              <p>
                {t(
                  "New journeys are private. Imports stay in the workspace you explicitly choose. Publishing creates a separate, read-only snapshot.",
                )}
              </p>
              <h3>{t("Local data")}</h3>
              <p>
                {t(
                  "Conversations, original files, discussions, and layouts are stored in SQLite on this computer.",
                )}
              </p>
              <a href="/openapi/v1.json" target="_blank">
                {t("OpenAPI specification")} <ArrowUpRight size={14} />
              </a>
            </div>
          </div>
        ) : (
          <section className="page">
            <div className="page-heading">
              <div>
                <span className="eyebrow">
                  {section === "journeys"
                    ? t("THE BIGGER PICTURE")
                    : t("YOUR IDEAS, CONNECTED")}
                </span>
                <h1>
                  {section === "journeys"
                    ? t("Your journeys")
                    : t("Good to see you, {name}.", {
                        name: user.name.split(" ")[0],
                      })}
                </h1>
                <p className="subtitle">
                  {section === "journeys"
                    ? t("Curate the path from first question to final result.")
                    : t(
                        "Pick up a thread. Follow an idea. See how it all connects.",
                      )}
                </p>
              </div>
              <button
                className="primary"
                disabled={!canEdit}
                onClick={() => setCreate(true)}
              >
                <Plus size={17} />
                {t("New journey")}
              </button>
            </div>
            {section !== "journeys" && (
              <>
                <div className="stats">
                  <div>
                    <Library />
                    <strong>{formatNumber(summary.conversations)}</strong>
                    <span>{t("Conversations")}</span>
                  </div>
                  <div>
                    <GitBranch />
                    <strong>{formatNumber(summary.journeys)}</strong>
                    <span>{t("Connected journeys")}</span>
                  </div>
                  <div>
                    <Command />
                    <strong>{formatNumber(summary.providers.length)}</strong>
                    <span>{t("AI perspectives")}</span>
                  </div>
                  <div>
                    <Users />
                    <strong>
                      {formatNumber(
                        members.filter((member) => member.id !== user.id)
                          .length,
                      )}
                    </strong>
                    <span>{t("Collaborators")}</span>
                  </div>
                </div>
                <div className="section-title">
                  <h2>{t("Continue your journey")}</h2>
                  <a href="#journeys">
                    {t("View all")} <ArrowRight size={14} />
                  </a>
                </div>
              </>
            )}
            <div
              className={
                "journey-grid " +
                (section === "journeys" ? "" : "overview-journeys")
              }
            >
              {journeys.map((j, i) => (
                <a
                  className={"journey-card " + (i === 0 ? "featured" : "")}
                  href={"#canvas/" + j.id}
                  key={j.id}
                >
                  <div className="mini-graph">
                    <JourneyPreview journey={j} />
                    <span className="graph-badge">
                      {j.visibility === "Private"
                        ? t("Private draft")
                        : t("Team journey")}
                    </span>
                  </div>
                  <div className="journey-card-body">
                    <div className="row">
                      <span className="eyebrow">
                        {i === 0 ? t("FEATURED JOURNEY") : t("IN PROGRESS")}
                      </span>
                      <ArrowUpRight size={18} />
                    </div>
                    <h2>{j.title}</h2>
                    <p>
                      {j.description ||
                        t("A new story waiting to be connected.")}
                    </p>
                    <footer>
                      <div className="provider-dots">
                        <span>◉</span>
                        <span>✳</span>
                        <span>⌘</span>
                      </div>
                      <span>{count("nodes", j.nodeCount)}</span>
                      <span className="spacer" />
                      <span className="avatar tiny">{initials(user.name)}</span>
                    </footer>
                  </div>
                </a>
              ))}
            </div>
            {section === "journeys" && journeysCursor && (
              <button className="load-more" onClick={moreJourneys}>
                {t("Load more")}
              </button>
            )}
            {section !== "journeys" && (
              <>
                <div className="section-title">
                  <h2>{t("Recently added conversations")}</h2>
                  <a href="#library">
                    {t("Open library")} <ArrowRight size={14} />
                  </a>
                </div>
                <ConversationTable items={recent} />
                <div className="import-banner">
                  <div className="upload-icon">
                    <Upload />
                  </div>
                  <div>
                    <h3>
                      {t("Your next journey starts with a conversation.")}
                    </h3>
                    <p>
                      {t(
                        "Bring your ChatGPT, Claude, or other AI conversations into one place.",
                      )}
                    </p>
                  </div>
                  <button
                    disabled={!canEdit}
                    onClick={() => setImporting(true)}
                  >
                    {t("Import conversations")} <ArrowRight size={16} />
                  </button>
                </div>
              </>
            )}
            {journeys.length === 0 && (
              <div className="empty">
                {t(
                  "Create a journey, then add conversations from your library.",
                )}
              </div>
            )}
          </section>
        )}
      </div>
      {createWorkspace && (
        <CreateWorkspaceDialog
          close={() => setCreateWorkspace(false)}
          created={(w) => {
            setWorkspaces((ws) => [...ws, w]);
            setWorkspace(w.id);
            setSummary({ conversations: 0, journeys: 0, providers: [] });
            setRecent([]);
            setJourneys([]);
            setMembers([]);
            setCreateWorkspace(false);
            refresh();
            go("workspaces");
            notify(
              t(
                "Workspace created. You can now import conversations or create journeys.",
              ),
            );
          }}
        />
      )}
      {importing && (
        <ImportDialog
          close={() => setImporting(false)}
          done={refresh}
          workspaces={workspaces}
        />
      )}{" "}
      {create && (
        <Modal
          title={t("Start a new journey")}
          onClose={() => setCreate(false)}
        >
          <form
            onSubmit={(e) => {
              e.preventDefault();
              act(async () => {
                const j = await api("/w/" + workspace + "/journeys", { name });
                setCreate(false);
                setName("");
                refresh();
                go("canvas/" + j.id);
              });
            }}
          >
            <label>
              {t("Journey title")}
              <input
                autoFocus
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t("What are you building or exploring?")}
                required
              />
            </label>
            <p className="muted">
              {t(
                "Your journey starts private. Add conversations without changing the originals.",
              )}
            </p>
            <button className="primary">
              {t("Create journey")} <ArrowRight size={16} />
            </button>
          </form>
        </Modal>
      )}
      {publish && (
        <PublishDialog journey={publish} close={() => setPublish(null)} />
      )}{" "}
      {toast && (
        <div className="toast" role="status">
          {toast}
          <button aria-label={t("Dismiss")} onClick={() => notify("")}>
            <X size={14} />
          </button>
        </div>
      )}
    </div>
  );
}
function ConversationTable({
  items,
  canEdit = false,
  confirm = "",
  setConfirm = () => {},
  onDelete = () => {},
  selected,
  onSelect,
}: {
  items: any[];
  canEdit?: boolean;
  confirm?: string;
  setConfirm?: (id: string) => void;
  onDelete?: (id: string) => void;
  selected?: Record<string, { id: string; title: string }>;
  onSelect?: (conversation: any) => void;
}) {
  useLocale();
  return (
    <div className={"conversation-table" + (canEdit ? " can-edit" : "")}>
      <div className="table-head">
        <span>{t("CONVERSATION")}</span>
        <span>{t("PROVIDER")}</span>
        <span>{t("MESSAGES")}</span>
        <span>{t("SOURCE")}</span>
        {canEdit && <span className="sr-only">{t("Delete")}</span>}
      </div>
      {items.map((c) => (
        <div className="table-row" key={c.id}>
          <div className="conversation-title-cell">
            {onSelect && (
              <input
                type="checkbox"
                checked={!!selected?.[c.id]}
                aria-label={t("Select {title}", { title: c.title })}
                onChange={() => onSelect(c)}
              />
            )}
            <a href={"#explorer/" + c.id}>
              <span
                className={"conversation-icon " + providerClass(c.provider)}
              >
                <BookOpen size={17} />
              </span>
              <div>
                <strong>{c.title}</strong>
                <small>
                  {c.model || t("Model not provided")}
                  {c.demo ? t(" · Demonstration") : ""}
                </small>
              </div>
            </a>
          </div>
          <Badge provider={c.provider} />
          <span>{count("messages", c.messageCount)}</span>
          <span className="muted">{c.sourceFormat}</span>
          {canEdit && (
            <span className="row-actions">
              {confirm === c.id ? (
                <>
                  <button className="danger" onClick={() => onDelete(c.id)}>
                    {t("Delete")}
                  </button>
                  <button
                    aria-label={t("Dismiss")}
                    onClick={() => setConfirm("")}
                  >
                    <X size={14} />
                  </button>
                </>
              ) : (
                <button
                  aria-label={t("Delete conversation")}
                  title={t("Delete conversation")}
                  onClick={() => setConfirm(c.id)}
                >
                  <Trash2 size={15} />
                </button>
              )}
            </span>
          )}
        </div>
      ))}
      {items.length === 0 && (
        <div className="empty">
          {t("No conversations found. Import a source to get started.")}
        </div>
      )}
    </div>
  );
}
function CanvasRoute({
  id,
  revision,
  onPublish,
  ...props
}: {
  id: string;
  revision: number;
  onPublish: (journey: any) => void;
  canEdit: boolean;
  canPublish: boolean;
  online: string[];
  refresh: () => void;
  user: any;
  members: any[];
}) {
  useLocale();
  const [journey, setJourney] = useState<any>(null);
  useEffect(() => {
    act(async () => setJourney(await api("/journeys/" + id)));
  }, [id, revision]);
  if (!journey) return <div className="loading">{t("Opening journey…")}</div>;
  return (
    <JourneyCanvas
      journey={journey}
      onPublish={() => onPublish(journey)}
      {...props}
    />
  );
}
function groupConversations(items: any[]) {
  const groups = new Map<string, { id: string; name: string; items: any[] }>();
  for (const item of items) {
    const id = item.applicationProjectId || "";
    const name = id ? item.projectName || t("No project") : t("No project");
    const group = groups.get(id) ?? { id, name, items: [] as any[] };
    group.items.push(item);
    groups.set(id, group);
  }
  return [...groups.values()].sort((a, b) => {
    if (!a.id) return 1;
    if (!b.id) return -1;
    return a.name.localeCompare(b.name);
  });
}
function sourceGroups(items: any[]) {
  const groups = new Map<
    string,
    { key: string; name: string; identity: string; items: any[] }
  >();
  for (const item of items) {
    const name =
      item.sourceProjectName ||
      item.externalProjectId ||
      item.sourcePath ||
      t("Ungrouped source");
    const key =
      item.sourceGroupKey ||
      JSON.stringify([item.provider, item.sourceIdentity, name]);
    const group = groups.get(key) ?? {
      key,
      name: item.provider + " / " + name,
      identity: item.sourceIdentity || "default",
      items: [] as any[],
    };
    group.items.push(item);
    groups.set(key, group);
  }
  return [...groups.values()].sort(
    (a, b) =>
      a.name.localeCompare(b.name) || a.identity.localeCompare(b.identity),
  );
}
function LibraryView({
  providers,
  revision,
  importing,
  canEdit,
  refresh,
}: {
  providers: string[];
  revision: number;
  importing: () => void;
  canEdit: boolean;
  refresh: () => void;
}) {
  useLocale();
  const [q, setQ] = useState("");
  const [project, setProject] = useState("");
  const [sourceGroup, setSourceGroup] = useState("");
  const [availableGroups, setAvailableGroups] = useState<any[]>([]);
  const [projects, setProjects] = useState<any[]>([]);
  const [provider, setProvider] = useState("");
  const [sort, setSort] = useState("");
  const [confirm, setConfirm] = useState("");
  const [confirmProject, setConfirmProject] = useState("");
  const [selected, setSelected] = useState<
    Record<string, { id: string; title: string }>
  >({});
  const [building, setBuilding] = useState(false);
  const toggle = (c: any) =>
    setSelected((current) => {
      const next = { ...current };
      if (next[c.id]) delete next[c.id];
      else if (Object.keys(next).length < 100)
        next[c.id] = { id: c.id, title: c.title };
      return next;
    });
  const { workspace, notify } = useApp();
  useEffect(() => {
    setSelected({});
    setBuilding(false);
    setConfirmProject("");
    setSourceGroup("");
    setAvailableGroups([]);
  }, [workspace]);
  const results = useLibrary(
    workspace,
    { q, provider, project, sourceGroup, sort },
    revision,
  );
  const groups = groupConversations(results.items);
  const remove = (id: string) =>
    act(async () => {
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
  const removeProject = (group: { id: string; items: any[] }) =>
    act(async () => {
      setConfirmProject("");
      const result = await api<{ deleted: number }>(
        "/w/" + workspace + "/projects/" + group.id + "/conversations",
        undefined,
        "DELETE",
      );
      const removed = new Set(group.items.map((item) => item.id));
      setSelected((current) => {
        const next = { ...current };
        for (const id of removed) delete next[id];
        return next;
      });
      if (removed.has(confirm)) setConfirm("");
      notify(t("Deleted {count} conversations", { count: result.deleted }));
      refresh();
    });
  useEffect(() => {
    let live = true;
    act(async () => {
      const [projects, groups] = await Promise.all([
        api("/w/" + workspace + "/projects"),
        api("/w/" + workspace + "/library-groups"),
      ]);
      if (!live) return;
      setProjects(projects);
      setAvailableGroups(groups);
      setSourceGroup((current) =>
        groups.some((g: any) => g.key === current) ? current : "",
      );
    });
    return () => {
      live = false;
    };
  }, [workspace, revision]);
  return (
    <section className="page">
      <div className="page-heading">
        <div>
          <span className="eyebrow">{t("THE SOURCE MATERIAL")}</span>
          <h1>{t("Conversation library")}</h1>
          <p className="subtitle">
            {t("Original conversations. Every perspective, in one place.")}
          </p>
        </div>
        <button className="primary" disabled={!canEdit} onClick={importing}>
          <Upload size={17} />
          {t("Import conversations")}
        </button>
      </div>
      <div className="filterbar">
        <label className="search">
          <Search size={17} />
          <input
            placeholder={t("Search conversations, messages, code, comments…")}
            value={q}
            onChange={(e) => setQ(e.target.value)}
          />
        </label>
        <select
          aria-label={t("Provider filter")}
          value={provider}
          onChange={(e) => setProvider(e.target.value)}
        >
          <option value="">{t("All providers")}</option>
          {providers.map((p) => (
            <option key={p}>{p}</option>
          ))}
        </select>
        <select
          aria-label={t("Application project filter")}
          value={project}
          onChange={(e) => setProject(e.target.value)}
        >
          <option value="">{t("All projects")}</option>
          {projects.map((p) => (
            <option value={p.id} key={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        <select
          aria-label={t("Source group filter")}
          value={sourceGroup}
          onChange={(e) => setSourceGroup(e.target.value)}
        >
          <option value="">{t("All source groups")}</option>
          {availableGroups.map((g) => (
            <option key={g.key} value={g.key}>
              {g.provider} / {g.name || t("Ungrouped source")}
              {g.sourceIdentity !== "default" ? " · " + g.sourceIdentity : ""}
              {" (" + g.count + ")"}
            </option>
          ))}
        </select>
        <select
          aria-label={t("Sort")}
          value={sort}
          onChange={(e) => setSort(e.target.value)}
        >
          <option value="">{t("Newest")}</option>
          <option value="messages">{t("Message count")}</option>
        </select>
        <span className="muted">
          {count("conversations", results.items.length)}
        </span>
      </div>
      {canEdit && (
        <div className="journey-selection-bar">
          <button
            disabled={!results.items.length || results.loading}
            onClick={() =>
              setSelected((current) => {
                const next = { ...current };
                for (const c of results.items) {
                  if (Object.keys(next).length >= 100) break;
                  next[c.id] = { id: c.id, title: c.title };
                }
                return next;
              })
            }
          >
            {t("Select loaded conversations")}
          </button>
          <span role="status">
            {t("{count} conversations selected", {
              count: Object.keys(selected).length,
            })}
          </span>
          {!!Object.keys(selected).length && (
            <button onClick={() => setSelected({})}>
              {t("Clear selection")}
            </button>
          )}
          <span className="muted">
            {t("Select up to 100. Selection is kept across filters.")}
          </span>
          <button
            className="primary"
            disabled={!Object.keys(selected).length}
            onClick={() => setBuilding(true)}
          >
            <GitBranch size={16} />
            {t("Build journey")}
          </button>
        </div>
      )}
      {building && canEdit && (
        <BuildJourneyDialog
          selected={Object.values(selected)}
          close={() => setBuilding(false)}
          refresh={refresh}
        />
      )}
      {results.items.length === 0 ? (
        <ConversationTable
          items={[]}
          canEdit={canEdit}
          confirm={confirm}
          setConfirm={setConfirm}
          onDelete={remove}
        />
      ) : (
        groups.map((group) => (
          <section className="library-group" key={group.id || "none"}>
            <div className="library-group-heading">
              <h2>
                {group.name}
                <span>{count("conversations", group.items.length)}</span>
              </h2>
              {canEdit &&
                group.id &&
                (confirmProject === group.id ? (
                  <div className="confirm-delete" role="alert">
                    <span>
                      {t(
                        "Delete every conversation in {name}? This cannot be undone.",
                        { name: group.name },
                      )}
                    </span>
                    <button
                      className="danger"
                      onClick={() => removeProject(group)}
                    >
                      {t("Delete all")}
                    </button>
                    <button
                      aria-label={t("Dismiss")}
                      onClick={() => setConfirmProject("")}
                    >
                      <X size={14} />
                    </button>
                  </div>
                ) : (
                  <button onClick={() => setConfirmProject(group.id)}>
                    <Trash2 size={14} />
                    {t("Delete all conversations")}
                  </button>
                ))}
            </div>
            {sourceGroups(group.items).map((source) => (
              <details className="library-source-group" key={source.key} open>
                <summary>
                  <span>{source.name}</span>
                  {source.identity !== "default" && (
                    <small>{source.identity}</small>
                  )}
                  <span className="muted">
                    {count("conversations", source.items.length)}
                  </span>
                </summary>
                <ConversationTable
                  items={source.items}
                  selected={selected}
                  onSelect={canEdit ? toggle : undefined}
                  canEdit={canEdit}
                  confirm={confirm}
                  setConfirm={setConfirm}
                  onDelete={remove}
                />
              </details>
            ))}
          </section>
        ))
      )}
      {results.more && (
        <button
          className="load-more"
          disabled={results.loading}
          onClick={results.more}
        >
          {t("Load more")}
        </button>
      )}
    </section>
  );
}
function Team({
  members,
  role,
  refresh,
}: {
  members: any[];
  role: string;
  refresh: () => void;
}) {
  useLocale();
  const { workspace, notify, setWorkspace } = useApp();
  const [email, setEmail] = useState("");
  const [inviteRole, setRole] = useState("Editor");
  const [token, setToken] = useState("");
  const [accept, setAccept] = useState("");
  const [activity, setActivity] = useState<any[]>([]);
  const manage = ["Owner", "Admin"].includes(role);
  useEffect(() => {
    act(async () => setActivity(await api("/w/" + workspace + "/activity")));
  }, [workspace, members]);
  return (
    <section className="page">
      <span className="eyebrow">{t("BETTER TOGETHER")}</span>
      <h1>{t("Members & access")}</h1>
      <p className="subtitle">
        {t("A shared understanding of how the work happened.")}
      </p>
      <div className="two-col">
        <div className="panel">
          <div className="section-title">
            <h2>{t("People in this workspace")}</h2>
            {manage && (
              <button
                onClick={() => {
                  const name = prompt(t("Workspace name"));
                  if (name)
                    act(async () => {
                      await api("/w/" + workspace, { name }, "PUT");
                      refresh();
                    });
                }}
              >
                {t("Rename")}
              </button>
            )}
            {role !== "Owner" && (
              <button
                onClick={() =>
                  act(async () => {
                    await api("/w/" + workspace + "/leave", {});
                    setWorkspace("");
                    refresh();
                  })
                }
              >
                {t("Leave workspace")}
              </button>
            )}
          </div>
          {members.map((m) => (
            <div className="member" key={m.id}>
              <span className="avatar">{initials(m.name)}</span>
              <div>
                <strong>{m.name}</strong>
                <small>{m.email}</small>
              </div>
              <select
                aria-label={t("Role for {name}", { name: m.name })}
                disabled={!manage || m.role === "Owner"}
                value={m.role}
                onChange={(e) =>
                  act(async () => {
                    await api(
                      "/w/" + workspace + "/members/" + m.id,
                      { role: e.target.value },
                      "PUT",
                    );
                    refresh();
                  })
                }
              >
                {[
                  "Owner",
                  "Admin",
                  "Editor",
                  "Commenter",
                  "Viewer",
                  "Remove",
                ].map((r) => (
                  <option key={r} value={r}>
                    {t(r)}
                  </option>
                ))}
              </select>
            </div>
          ))}
          {manage && (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                act(async () => {
                  const v = await api("/w/" + workspace + "/invitations", {
                    email,
                    role: inviteRole,
                  });
                  setToken(v.token);
                  notify(
                    t(
                      "Invitation created. Share the token with the intended teammate.",
                    ),
                  );
                });
              }}
            >
              <h3>{t("Invite a teammate")}</h3>
              <label>
                {t("Email")}
                <input
                  type="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                />
              </label>
              <div className="row">
                <select
                  value={inviteRole}
                  onChange={(e) => setRole(e.target.value)}
                >
                  {[
                    "Editor",
                    "Commenter",
                    "Viewer",
                    ...(role === "Owner" ? ["Admin"] : []),
                  ].map((r) => (
                    <option key={r} value={r}>
                      {t(r)}
                    </option>
                  ))}
                </select>
                <button className="primary">{t("Create invitation")}</button>
              </div>
              {token && (
                <p className="token">
                  {t("Invitation token (valid for 7 days):")}{" "}
                  <code>{token}</code>
                </p>
              )}
              <small>
                {t(
                  "Email delivery is deferred. Only the account with this email can accept.",
                )}
              </small>
            </form>
          )}
        </div>
        <div>
          <div className="panel">
            <h2>{t("Recent activity")}</h2>
            {activity.map((a) => (
              <div className="activity" key={a.id}>
                <span className="dot" />
                <div>
                  {a.text}
                  <small>{formatDate(a.createdAt)}</small>
                </div>
              </div>
            ))}
          </div>
          <div className="panel">
            <h3>{t("Join a workspace")}</h3>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                act(async () => {
                  await api("/invitations/" + accept + "/accept", {});
                  refresh();
                  notify(t("Joined workspace"));
                });
              }}
            >
              <input
                aria-label={t("Invitation token")}
                placeholder={t("Paste invitation token")}
                value={accept}
                onChange={(e) => setAccept(e.target.value)}
                required
              />
              <button>{t("Accept invitation")}</button>
            </form>
            <a className="button" href="#workspaces">
              {t("Create or manage workspaces →")}
            </a>
          </div>
        </div>
      </div>
    </section>
  );
}
createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <Suspense fallback={<div className="loading">{t("Opening journey…")}</div>}>
      <App />
    </Suspense>
  </React.StrictMode>,
);
