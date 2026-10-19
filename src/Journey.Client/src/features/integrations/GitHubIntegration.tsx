import { useEffect, useState } from "react";
import {
  Check,
  ExternalLink,
  FileCode2,
  GitBranch,
  Link2,
  ShieldCheck,
} from "lucide-react";
import { api, useApp } from "../../shared/state";
import { t, useLocale } from "../../shared/i18n/i18n";
import "./github.css";
import { GitHubSetup } from "./GitHubSetup";

type Project = {
  id: string;
  name: string;
  gitHubRepositoryUrl?: string | null;
  gitHubBranch?: string | null;
  gitHubLocalRoot?: string | null;
};

export function GitHubIntegration({ canEdit }: { canEdit: boolean }) {
  useLocale();
  const { workspace } = useApp();
  const [projects, setProjects] = useState<Project[]>([]);
  const [selected, setSelected] = useState("");
  const [enabled, setEnabled] = useState(false);
  const [mode, setMode] = useState("Public");
  const [configured, setConfigured] = useState(true);
  const [installationUrl, setInstallationUrl] = useState<string | null>(null);
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
      api<{
        enabled: boolean;
        mode?: string;
        configured?: boolean;
        installationUrl?: string;
        configurationError?: string;
      }>("/integrations/github/status"),
      api<Project[]>(`/w/${workspace}/projects`),
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
  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await action();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <section
      className="github-integration"
      aria-label={t("GitHub integration")}
    >
      <div className="github-section-heading">
        <div>
          <h2>{t("GitHub integration")}</h2>
          <p>{t("Link a public repository to a project in your workspace.")}</p>
        </div>
        <span className="github-badge">{t("Experimental")}</span>
      </div>
      {loading ? (
        <p role="status">{t("Loading…")}</p>
      ) : (
        <>
          {!enabled && (
            <p className="github-notice">
              {t("The experimental GitHub integration is disabled.")}
            </p>
          )}
          <GitHubSetup />
          {configurationError && (
            <p className="github-notice" role="alert">
              {t(configurationError)}
            </p>
          )}
          {mode === "App" && configured && (
            <div className="github-app-controls">
              <p>
                {t(
                  "Install the GitHub App on your repository, then return here to link it. Organization access may need owner approval.",
                )}
              </p>
              {installationUrl && (
                <a
                  href={installationUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  {t("Install or manage GitHub App")}
                  <ExternalLink size={14} />
                </a>
              )}
              {canEdit && (
                <button
                  type="button"
                  disabled={busy || !enabled}
                  onClick={() =>
                    void run(async () => {
                      await api(
                        `/w/${workspace}/integrations/github/check`,
                        {},
                      );
                      setNotice(
                        t(
                          "GitHub App credentials verified. Install it on the repository before linking.",
                        ),
                      );
                    })
                  }
                >
                  {t("Check App credentials")}
                </button>
              )}
            </div>
          )}
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          <div className="github-layout">
            <form
              className="github-card"
              onSubmit={(event) => {
                event.preventDefault();
                if (!project || busy || !enabled || !configured || !canEdit)
                  return;
                void run(async () => {
                  const saved = await api<Project>(
                    `/w/${workspace}/projects/${project.id}/github`,
                    { repositoryUrl, branch, localRoot },
                    "PUT",
                  );
                  setProjects((rows) =>
                    rows.map((p) => (p.id === saved.id ? saved : p)),
                  );
                  setNotice(
                    t(
                      "Repository linked. Open a conversation assigned to this project to see source links.",
                    ),
                  );
                });
              }}
            >
              <div className="github-card-heading">
                <span className="github-icon">
                  <GitBranch size={22} />
                </span>
                <h3>{t("Public repository")}</h3>
                {project?.gitHubRepositoryUrl && (
                  <span className="github-connected">
                    <Check size={14} />
                    {t("Connected")}
                  </span>
                )}
              </div>
              <label>
                {t("Project")}
                <select
                  disabled={busy}
                  value={selected}
                  onChange={(e) => {
                    setSelected(e.target.value);
                    setNotice("");
                  }}
                >
                  <option value="" disabled>
                    {t("Choose a project")}
                  </option>
                  {projects.map((p) => (
                    <option value={p.id} key={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </label>
              <fieldset disabled={busy || !canEdit || !enabled || !configured}>
                <details>
                  <summary>{t("Create a project")}</summary>
                  <div className="github-create">
                    <input
                      aria-label={t("Project name")}
                      value={newName}
                      maxLength={150}
                      onChange={(e) => setNewName(e.target.value)}
                    />
                    <button
                      type="button"
                      disabled={!newName.trim()}
                      onClick={() =>
                        void run(async () => {
                          const added = await api<Project>(
                            `/w/${workspace}/projects`,
                            { name: newName },
                          );
                          setProjects((rows) => [...rows, added]);
                          setSelected(added.id);
                          setNewName("");
                        })
                      }
                    >
                      {t("Create")}
                    </button>
                  </div>
                </details>
                <label>
                  {t("Repository URL")}
                  <input
                    type="url"
                    required
                    value={repositoryUrl}
                    maxLength={250}
                    placeholder="https://github.com/owner/repository"
                    onChange={(e) => setRepositoryUrl(e.target.value)}
                  />
                </label>
                <p className="github-help">
                  {t(
                    mode === "App"
                      ? "Public repositories only. The GitHub App needs access to the selected repository."
                      : "Public repositories only. No GitHub sign-in or token required.",
                  )}
                </p>
                <label>
                  {t("Branch")}
                  <input
                    value={branch}
                    maxLength={250}
                    placeholder={t("Repository default branch")}
                    onChange={(e) => setBranch(e.target.value)}
                  />
                </label>
                <label>
                  {t("Project folder in tool calls")}
                  <input
                    value={localRoot}
                    maxLength={1024}
                    placeholder="C:\projects\my-app or /workspace/my-app"
                    onChange={(e) => setLocalRoot(e.target.value)}
                  />
                </label>
                <p className="github-help">
                  {t(
                    "Required for absolute file paths. Use the repository root from the imported conversation, not this server.",
                  )}
                </p>
              </fieldset>
              <div className="github-actions">
                <button
                  className="primary"
                  disabled={
                    busy || !canEdit || !enabled || !configured || !project
                  }
                  type="submit"
                >
                  <Link2 size={15} />
                  {t(
                    project?.gitHubRepositoryUrl
                      ? "Save connection"
                      : "Link repository",
                  )}
                </button>
                {project?.gitHubRepositoryUrl && (
                  <button
                    disabled={busy || !canEdit}
                    type="button"
                    onClick={() =>
                      void run(async () => {
                        await api(
                          `/w/${workspace}/projects/${project.id}/github`,
                          undefined,
                          "DELETE",
                        );
                        setProjects((rows) =>
                          rows.map((p) =>
                            p.id === project.id
                              ? {
                                  ...p,
                                  gitHubRepositoryUrl: null,
                                  gitHubBranch: null,
                                  gitHubLocalRoot: null,
                                }
                              : p,
                          ),
                        );
                      })
                    }
                  >
                    {t("Unlink")}
                  </button>
                )}
              </div>
              {notice && (
                <p className="github-success" role="status">
                  {notice}
                </p>
              )}
              {!canEdit && (
                <p className="github-help">
                  {t("An editor can manage repository connections.")}
                </p>
              )}
            </form>
            <aside className="github-card github-explanation">
              <h3>{t("In your conversations")}</h3>
              <p>
                {t(
                  "Read-file tool calls can link to the matching file and line range on GitHub.",
                )}
              </p>
              <div className="github-example">
                <FileCode2 size={17} />
                <code>src/auth/session.ts</code>
                <ExternalLink size={14} />
              </div>
              <p>
                {t(
                  "Recorded commits stay pinned. Without a recorded commit, links are labeled Latest on GitHub.",
                )}
              </p>
              <p>
                {t(
                  "Local, uncommitted changes may differ. Original tool payloads stay preserved.",
                )}
              </p>
              <p>
                {t(
                  "Assign conversations using Source grouping & import history → Application project.",
                )}
              </p>
              {project?.gitHubRepositoryUrl && (
                <a
                  href={project.gitHubRepositoryUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  {project.gitHubRepositoryUrl.replace(
                    "https://github.com/",
                    "",
                  )}
                  <ExternalLink size={14} />
                </a>
              )}
              <div className="github-trust">
                <ShieldCheck size={17} />
                {t("Conversation sharing stays unchanged.")}
              </div>
            </aside>
          </div>
        </>
      )}
    </section>
  );
}
