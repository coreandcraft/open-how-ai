import { t, useLocale } from "../../shared/i18n/i18n";
import { useState } from "react";
import {
  ArrowRight,
  Check,
  Layers3,
  LockKeyhole,
  Plus,
  Users,
} from "lucide-react";
import { api, go, useApp } from "../../shared/state";
import { Modal } from "../../shared/ui/ui";
export type WorkspaceSummary = {
  id: string;
  name: string;
  teamId: string | null;
  role: string;
};
export function CreateWorkspaceDialog({
  close,
  created,
}: {
  close: () => void;
  created: (w: WorkspaceSummary) => void;
}) {
  useLocale();
  const [name, setName] = useState("");
  const [type, setType] = useState("Personal");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  return (
    <Modal title={t("Create workspace")} onClose={close}>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setError("");
          setBusy(true);
          try {
            created(
              await api<WorkspaceSummary>("/workspaces", {
                name: name.trim(),
                type,
              }),
            );
          } catch (e) {
            setError((e as Error).message);
          } finally {
            setBusy(false);
          }
        }}
      >
        <p>
          {t(
            "A separate home for conversations, journeys, and comparisons. Create as many workspaces as you need.",
          )}
        </p>
        <label>
          {t("Workspace name")}
          <input
            autoFocus
            required
            maxLength={100}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("e.g. Product research, Side projects, Design team")}
          />
        </label>
        <fieldset className="workspace-type-options">
          <legend>{t("Workspace type")}</legend>
          <label className={type === "Personal" ? "chosen" : ""}>
            <input
              type="radio"
              name="workspaceType"
              value="Personal"
              checked={type === "Personal"}
              onChange={() => setType("Personal")}
            />
            <LockKeyhole size={19} />
            <span>
              <strong>{t("Personal")}</strong>
              <small>{t("Private to you. Organize your own projects.")}</small>
            </span>
          </label>
          <label className={type === "Team" ? "chosen" : ""}>
            <input
              type="radio"
              name="workspaceType"
              value="Team"
              checked={type === "Team"}
              onChange={() => setType("Team")}
            />
            <Users size={19} />
            <span>
              <strong>{t("Team")}</strong>
              <small>{t("Invite people and assign access roles.")}</small>
            </span>
          </label>
        </fieldset>
        <p className="workspace-privacy">
          {type === "Personal"
            ? t("Only you can access this workspace.")
            : t(
                "Starts with you as owner. Invite teammates when you’re ready.",
              )}{" "}
          {t("Existing conversations stay in their current workspace.")}
        </p>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <div className="modal-footer">
          <button type="button" disabled={busy} onClick={close}>
            {t("Cancel")}
          </button>
          <button className="primary" disabled={busy || !name.trim()}>
            {busy ? t("Creating…") : t("Create workspace")}
            <Plus size={15} />
          </button>
        </div>
      </form>
    </Modal>
  );
}
export function Workspaces({
  workspaces,
  onCreate,
  refresh,
}: {
  workspaces: WorkspaceSummary[];
  onCreate: () => void;
  refresh: () => void;
}) {
  useLocale();
  const { workspace, setWorkspace, notify } = useApp();
  const [renaming, setRenaming] = useState<WorkspaceSummary | null>(null);
  const [name, setName] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [token, setToken] = useState("");
  const [joining, setJoining] = useState(false);
  const open = (w: WorkspaceSummary, path: string) => {
    setWorkspace(w.id);
    go(path);
  };
  return (
    <section className="page workspace-management">
      <div className="page-heading">
        <div>
          <span className="eyebrow">{t("A HOME FOR EVERY PROJECT")}</span>
          <h1>{t("Workspaces")}</h1>
          <p className="subtitle">
            {t(
              "Organize separate areas for your work. Keep them personal or collaborate with a team.",
            )}
          </p>
        </div>
        <button className="primary" onClick={onCreate}>
          <Plus size={17} />
          {t("Create workspace")}
        </button>
      </div>
      <div className="workspace-explainer">
        <Layers3 size={22} />
        <p>
          {t(
            "Each workspace has its own conversation library, journeys, comparisons, and access settings. Personal library and Cloudplay Studio are starting examples—not a fixed limit.",
          )}
        </p>
      </div>
      <div className="workspace-grid">
        {workspaces.map((w) => (
          <article
            className={
              "workspace-card " + (w.id === workspace ? "current" : "")
            }
            key={w.id}
          >
            <div className="row">
              <span
                className={
                  "workspace-card-icon " + (w.teamId ? "team" : "personal")
                }
              >
                {w.teamId ? <Users size={22} /> : <LockKeyhole size={21} />}
              </span>
              <span className="spacer" />
              {w.id === workspace && (
                <span className="workspace-current">
                  <Check size={12} />
                  {t("Current workspace")}
                </span>
              )}
            </div>
            <h2>{w.name}</h2>
            <div className="row">
              <span className="type-tag">
                {w.teamId ? t("Team workspace") : t("Personal workspace")}
              </span>
              <span className="muted">{t(w.role)}</span>
            </div>
            <p>
              {w.teamId
                ? t(
                    "A shared library for your team. Membership and permissions are managed separately.",
                  )
                : t(
                    "A private library for your own conversations and projects. Only you have access.",
                  )}
            </p>
            <footer>
              <button
                className="primary subtle"
                onClick={() => open(w, "dashboard")}
              >
                {t("Open workspace")}
                <ArrowRight size={14} />
              </button>
              {w.teamId && (
                <button onClick={() => open(w, "team")}>
                  {t("Members & access")}
                </button>
              )}
              {["Owner", "Admin"].includes(w.role) && (
                <button
                  className="workspace-rename"
                  onClick={() => {
                    setRenaming(w);
                    setName(w.name);
                    setError("");
                  }}
                >
                  {t("Rename")}
                </button>
              )}
            </footer>
          </article>
        ))}
        <button className="workspace-add-card" onClick={onCreate}>
          <Plus size={26} />
          <strong>{t("Create another workspace")}</strong>
          <span>{t("For a project, a team, or a new area of work")}</span>
        </button>
      </div>
      <div className="workspace-join panel">
        <div>
          <h2>{t("Invited to a team workspace?")}</h2>
          <p>
            {t(
              "Accept an invitation using the account with the invited email address.",
            )}
          </p>
        </div>
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            setJoining(true);
            setError("");
            try {
              await api(
                "/invitations/" + encodeURIComponent(token.trim()) + "/accept",
                {},
              );
              setToken("");
              refresh();
              notify(t("Joined workspace. Select it above to get started."));
            } catch (e) {
              setError((e as Error).message);
            } finally {
              setJoining(false);
            }
          }}
        >
          <input
            aria-label={t("Workspace invitation token")}
            placeholder={t("Paste invitation token")}
            value={token}
            onChange={(e) => setToken(e.target.value)}
            required
          />
          <button disabled={joining || !token.trim()}>
            {joining ? t("Joining…") : t("Join workspace")}
          </button>
        </form>
        {error && !renaming && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
      </div>
      {renaming && (
        <Modal title={t("Rename workspace")} onClose={() => setRenaming(null)}>
          <form
            onSubmit={async (e) => {
              e.preventDefault();
              setBusy(true);
              try {
                await api("/w/" + renaming.id, { name: name.trim() }, "PUT");
                setRenaming(null);
                refresh();
                notify(t("Workspace renamed"));
              } catch (e) {
                setError((e as Error).message);
              } finally {
                setBusy(false);
              }
            }}
          >
            <label>
              {t("Workspace name")}
              <input
                autoFocus
                required
                maxLength={100}
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </label>
            {error && (
              <p className="error" role="alert">
                {error}
              </p>
            )}
            <div className="modal-footer">
              <button type="button" onClick={() => setRenaming(null)}>
                {t("Cancel")}
              </button>
              <button className="primary" disabled={busy || !name.trim()}>
                {busy ? t("Saving…") : t("Save name")}
              </button>
            </div>
          </form>
        </Modal>
      )}
    </section>
  );
}
