import { useEffect, useState } from "react";
import {
  Plug,
  ShieldCheck,
  ArrowUpRight,
  Plus,
  Copy,
  KeyRound,
  Check,
  Clock3,
} from "lucide-react";
import { api, useApp, useJourneyOptions } from "../../shared/state";
import { t, useLocale, formatDate } from "../../shared/i18n/i18n";
import { Modal } from "../../shared/ui/ui";
import "./integrations.css";
import { GitHubIntegration } from "./GitHubIntegration";

type Connection = {
  id: string;
  name: string;
  journeyIds: string;
  allowProposals: boolean;
  mode: string;
  revoked: boolean;
  expiresAt: string;
  lastUsedAt: string | null;
};
type Proposal = {
  id: string;
  journeyId: string;
  connectionId: string;
  kind: string;
  payload: string;
  status: string;
  baseVersion: number;
  createdAt: string;
};
type Status = {
  enabled: boolean;
  localAvailable: boolean;
  remoteConfigured: boolean;
  endpoint: string;
};
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
export function Integrations({
  canEdit,
  refresh,
}: {
  canEdit: boolean;
  refresh: () => void;
}) {
  useLocale();
  const { workspace } = useApp();
  const journeys = useJourneyOptions(workspace);
  const [status, setStatus] = useState<Status>();
  const [connections, setConnections] = useState<Connection[]>([]);
  const [proposals, setProposals] = useState<Proposal[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [mode, setMode] = useState("local");
  const [allowProposals, setAllowProposals] = useState(false);
  const [credential, setCredential] = useState<{
    id: string;
    token?: string;
    expiresAt: string;
  }>();
  const [review, setReview] = useState<Proposal>();
  const [confirm, setConfirm] = useState(false);
  const [copied, setCopied] = useState(false);
  const load = async () => {
    const [s, data] = await Promise.all([
      api<Status>("/integrations/mcp/status"),
      api<{ connections: Connection[]; proposals: Proposal[] }>(
        `/integrations/mcp/w/${workspace}`,
      ),
    ]);
    setStatus(s);
    setConnections(data.connections);
    setProposals(data.proposals);
  };
  useEffect(() => {
    let alive = true;
    const reload = () =>
      load().catch((e) => {
        if (alive) setError(e.message);
      });
    void reload();
    window.addEventListener("focus", reload);
    return () => {
      window.removeEventListener("focus", reload);
      alive = false;
    };
  }, [workspace]);
  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    setError("");
    try {
      await fn();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const endpoint = new URL(status?.endpoint || "/mcp", location.origin).href;
  const pending = proposals.filter((p) => p.status === "pending");
  const target = journeys.find((j) => j.id === review?.journeyId);
  const stale = !!review && target?.version !== review.baseVersion;
  const config = JSON.stringify(
    {
      url: endpoint,
      transport: "Streamable HTTP",
      ...(credential?.token
        ? { headers: { Authorization: `Bearer ${credential.token}` } }
        : {}),
    },
    null,
    2,
  );
  return (
    <div className="mcp-page">
      <header className="mcp-heading">
        <div>
          <div className="mcp-eyebrow">
            {t("Integrations")} <span>{t("Experimental")}</span>
          </div>
          <h1>{t("Your journey, in context.")}</h1>
          <p>{t("Give your AI applications the context behind your work.")}</p>
        </div>
        <Plug size={32} />
      </header>
      <GitHubIntegration key={workspace} canEdit={canEdit} />
      {error && (
        <p className="mcp-error" role="alert">
          {error}
        </p>
      )}
      {!status ? (
        <p role="status">{t("Loading…")}</p>
      ) : (
        <>
          {!status.enabled && (
            <p className="mcp-notice">
              {t(
                "MCP is disabled. Ask your administrator to enable the experimental integration.",
              )}
            </p>
          )}
          <div className="mcp-layout">
            <div className="mcp-main">
              <section className="mcp-card">
                <div className="mcp-card-title">
                  <span className="mcp-icon">
                    <Plug size={24} />
                  </span>
                  <div>
                    <h2>Model Context Protocol</h2>
                    <p>{t("Connect compatible AI applications to openhow.")}</p>
                  </div>
                  <span className="mcp-pill">
                    {t(status.enabled ? "Enabled" : "Disabled")}
                  </span>
                </div>
                <div className="mcp-endpoint">
                  <span>{t("Server endpoint")}</span>
                  <code>{endpoint}</code>
                  <small>Streamable HTTP</small>
                </div>
                <div className="mcp-trust">
                  <ShieldCheck size={20} />
                  <p>
                    {t(
                      "You choose the journeys. AI suggestions wait for your approval.",
                    )}
                  </p>
                </div>
                <button
                  className="primary"
                  disabled={
                    !status.enabled ||
                    (!status.localAvailable && !status.remoteConfigured)
                  }
                  onClick={() => {
                    setCreating(true);
                    setName("");
                    setSelected([]);
                    setAllowProposals(false);
                    setMode(status.localAvailable ? "local" : "oauth");
                    setError("");
                  }}
                >
                  <Plus size={16} />
                  {t("Add connection")}
                </button>
              </section>
              <section className="mcp-card">
                <div className="mcp-section-title">
                  <h2>{t("Your connections")}</h2>
                  <span>
                    {
                      connections.filter(
                        (c) => !c.revoked && new Date(c.expiresAt) > new Date(),
                      ).length
                    }
                  </span>
                </div>
                {!connections.length && (
                  <div className="mcp-empty">
                    <KeyRound size={24} />
                    <h3>{t("No connections yet")}</h3>
                    <p>
                      {t(
                        "Create a connection with access to specific journeys in this workspace.",
                      )}
                    </p>
                  </div>
                )}
                {connections.map((c) => {
                  const inactive =
                    c.revoked || new Date(c.expiresAt) <= new Date();
                  return (
                    <div className="mcp-connection" key={c.id}>
                      <div>
                        <strong>{c.name}</strong>
                        <p>
                          {t(
                            c.mode === "local"
                              ? "Local development"
                              : "Remote OAuth",
                          )}{" "}
                          ·{" "}
                          {t(c.allowProposals ? "Read + propose" : "Read only")}
                        </p>
                        <small>
                          {JSON.parse(c.journeyIds)
                            .map(
                              (id: string) =>
                                journeys.find((j) => j.id === id)?.title ||
                                t("Unavailable journey"),
                            )
                            .join(", ")}
                        </small>
                        <small>
                          {t("Expires")}: {formatDate(c.expiresAt)} ·{" "}
                          {t("Last used")}:{" "}
                          {c.lastUsedAt ? formatDate(c.lastUsedAt) : t("Never")}
                        </small>
                      </div>
                      {inactive ? (
                        <span className="mcp-pill">
                          {t(c.revoked ? "Revoked" : "Expired")}
                        </span>
                      ) : (
                        <button
                          disabled={busy}
                          onClick={() =>
                            run(async () => {
                              await api(
                                `/integrations/mcp/connections/${c.id}`,
                                undefined,
                                "DELETE",
                              );
                              await load();
                            })
                          }
                        >
                          {t("Revoke")}
                        </button>
                      )}
                    </div>
                  );
                })}
              </section>
              <section className="mcp-card">
                <div className="mcp-section-title">
                  <h2>{t("Proposals to review")}</h2>
                  <span>{pending.length}</span>
                  <button disabled={busy} onClick={() => run(load)}>
                    {t("Refresh")}
                  </button>
                </div>
                <p className="mcp-muted">
                  {t(
                    "Nothing changes until an editor approves. Publishing stays separate.",
                  )}
                </p>
                {!pending.length && (
                  <div className="mcp-empty">
                    <Check size={24} />
                    <p>{t("No pending proposals")}</p>
                  </div>
                )}
                {pending.map((p) => (
                  <div className="mcp-connection" key={p.id}>
                    <Clock3 size={18} />
                    <div>
                      <strong>
                        {JSON.parse(p.payload).title || t(p.kind)}
                      </strong>
                      <p>
                        {journeys.find((j) => j.id === p.journeyId)?.title} ·{" "}
                        {t(p.kind)}
                      </p>
                      <small>{formatDate(p.createdAt)}</small>
                    </div>
                    <button
                      disabled={!canEdit || !status.enabled}
                      onClick={() => {
                        setReview(p);
                        setConfirm(false);
                        setError("");
                      }}
                    >
                      {t("Review")}
                    </button>
                  </div>
                ))}
                {proposals.some((p) => p.status !== "pending") && (
                  <details>
                    <summary>{t("Review history")}</summary>
                    {proposals
                      .filter((p) => p.status !== "pending")
                      .map((p) => (
                        <div className="mcp-history" key={p.id}>
                          <span>
                            {JSON.parse(p.payload).title || t(p.kind)}
                          </span>
                          <span>{t(p.status)}</span>
                        </div>
                      ))}
                  </details>
                )}
              </section>
            </div>
            <aside className="mcp-aside">
              <section className="mcp-card">
                <ShieldCheck size={24} />
                <h2>{t("Access you control")}</h2>
                <p>
                  {t(
                    "Permissions follow your current workspace role and selected journeys. Revoking a connection stops future calls.",
                  )}
                </p>
                <h3>{t("Read context")}</h3>
                {readTools.map((n) => (
                  <code className="mcp-tool" key={n}>
                    {n}
                  </code>
                ))}
                <h3>{t("Propose changes")}</h3>
                <p>{t("Requires approval in openhow")}</p>
                {proposalTools.map((n) => (
                  <code className="mcp-tool" key={n}>
                    {n}
                  </code>
                ))}
              </section>
              <section className="mcp-card">
                <h3>{t("Connect your client")}</h3>
                <p>
                  {t(
                    "Use a client that supports Streamable HTTP and the authentication method shown when you create a connection.",
                  )}
                </p>
                <a
                  href="https://modelcontextprotocol.io/docs/learn/architecture"
                  target="_blank"
                  rel="noreferrer"
                >
                  {t("About MCP")} <ArrowUpRight size={14} />
                </a>
              </section>
            </aside>
          </div>
        </>
      )}
      {creating && (
        <Modal
          title={t("Add connection")}
          onClose={() => {
            if (!busy) setCreating(false);
          }}
        >
          <form
            className="mcp-form"
            onSubmit={(e) => {
              e.preventDefault();
              void run(async () => {
                const result = await api(
                  `/integrations/mcp/w/${workspace}/connections`,
                  { name, journeyIds: selected, allowProposals, mode },
                );
                setCredential(result);
                setCopied(false);
                setCreating(false);
                await load();
              });
            }}
          >
            {error && (
              <p role="alert" className="mcp-error">
                {error}
              </p>
            )}
            <label>
              {t("Connection name")}
              <input
                required
                maxLength={80}
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t("My AI assistant")}
              />
            </label>
            <label>
              {t("Connection type")}
              <select value={mode} onChange={(e) => setMode(e.target.value)}>
                {status?.localAvailable && (
                  <option value="local">{t("Local development")}</option>
                )}
                {status?.remoteConfigured && (
                  <option value="oauth">{t("Remote OAuth")}</option>
                )}
              </select>
            </label>
            <fieldset>
              <legend>{t("Choose journeys")}</legend>
              {journeys.length === 0 && <p>{t("Create a journey first.")}</p>}
              {journeys.map((j) => (
                <label className="mcp-check" key={j.id}>
                  <input
                    type="checkbox"
                    checked={selected.includes(j.id)}
                    onChange={(e) =>
                      setSelected((ids) =>
                        e.target.checked
                          ? [...ids, j.id]
                          : ids.filter((id) => id !== j.id),
                      )
                    }
                  />
                  <span>{j.title}</span>
                </label>
              ))}
            </fieldset>
            <label className="mcp-check">
              <input
                type="checkbox"
                disabled={!canEdit}
                checked={allowProposals}
                onChange={(e) => setAllowProposals(e.target.checked)}
              />
              <span>{t("Allow proposals for review")}</span>
            </label>
            <p>
              {t(
                "Read access is always included. Connections expire after 30 days.",
              )}
            </p>
            <button
              className="primary"
              disabled={busy || !selected.length || selected.length > 100}
            >
              {t("Create connection")}
            </button>
          </form>
        </Modal>
      )}
      {credential && (
        <Modal
          title={t("Connection created")}
          onClose={() => setCredential(undefined)}
        >
          <div className="mcp-form">
            <p>
              {t(
                credential.token
                  ? "Save this credential now. It is shown only once. Keep it private."
                  : "Your administrator must bind this connection ID to your OAuth access token before use.",
              )}
            </p>
            {credential.token ? (
              <>
                <p>
                  {t(
                    "Enter these connection settings in your MCP client. Client configuration formats vary.",
                  )}
                </p>
                <pre className="mcp-secret">{config}</pre>
                <button
                  onClick={() =>
                    run(async () => {
                      await navigator.clipboard.writeText(config);
                      setCopied(true);
                    })
                  }
                >
                  <Copy size={16} />
                  {t(copied ? "Copied" : "Copy settings")}
                </button>
              </>
            ) : (
              <code className="mcp-secret">{credential.id}</code>
            )}
            <p>
              {t("Expires")}: {formatDate(credential.expiresAt)}
            </p>
            <button
              className="primary"
              onClick={() => setCredential(undefined)}
            >
              {t("Done")}
            </button>
          </div>
        </Modal>
      )}
      {review && (
        <Modal
          title={t("Review proposal")}
          onClose={() => {
            if (!busy) setReview(undefined);
          }}
        >
          <div className="mcp-form">
            <strong>{target?.title}</strong>
            <p>
              {t("Proposed content is untrusted. Review it before applying.")}
            </p>
            {error && (
              <p role="alert" className="mcp-error">
                {error}
              </p>
            )}
            <dl className="mcp-payload">
              {Object.entries(JSON.parse(review.payload))
                .filter(([, value]) => value !== null && value !== "")
                .map(([key, value]) => (
                  <div key={key}>
                    <dt>{t(key)}</dt>
                    <dd>{String(value)}</dd>
                  </div>
                ))}
            </dl>
            {stale && (
              <p className="mcp-notice">
                {t(
                  "The journey changed. Reject this proposal and request a fresh one.",
                )}
              </p>
            )}
            <label className="mcp-check">
              <input
                type="checkbox"
                checked={confirm}
                onChange={(e) => setConfirm(e.target.checked)}
              />
              <span>
                {t("I reviewed this proposal and confirm my decision.")}
              </span>
            </label>
            <div className="mcp-actions">
              <button
                disabled={!confirm || busy}
                onClick={() =>
                  run(async () => {
                    await api(
                      `/integrations/mcp/proposals/${review.id}/review`,
                      {
                        approve: false,
                        expectedVersion: review.baseVersion,
                        confirm: true,
                      },
                    );
                    setReview(undefined);
                    await load();
                  })
                }
              >
                {t("Reject")}
              </button>
              <button
                className="primary"
                disabled={!confirm || busy || stale}
                onClick={() =>
                  run(async () => {
                    await api(
                      `/integrations/mcp/proposals/${review.id}/review`,
                      {
                        approve: true,
                        expectedVersion: review.baseVersion,
                        confirm: true,
                      },
                    );
                    setReview(undefined);
                    await load();
                    refresh();
                  })
                }
              >
                {t("Approve and apply")}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
