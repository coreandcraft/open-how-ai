import { RecoveredSource } from "./RecoveredSource";
import { useEffect, useState } from "react";
import { api, act, useLibrary } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
export function SourceHistory({
  conversation,
  canEdit,
  onUpdated,
}: {
  conversation: any;
  canEdit: boolean;
  onUpdated?: () => void;
}) {
  const [projects, setProjects] = useState<any[]>([]);
  const [links, setLinks] = useState<any[]>([]);
  const [conflicts, setConflicts] = useState<any[]>([]);
  const picker = useLibrary(conversation.workspaceId, { limit: 40 });
  const [project, setProject] = useState(
    conversation.applicationProjectId || "",
  );
  const [confirm, setConfirm] = useState<string | null>(null);
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
    setLinks(
      l.filter(
        (r: any) =>
          r.conversationId === conversation.id ||
          r.targetConversationId === conversation.id,
      ),
    );
    setConflicts(c.filter((r: any) => r.conversationId === conversation.id));
  };
  useEffect(() => {
    setProject(conversation.applicationProjectId || "");
    act(load);
  }, [conversation.id]);
  return (
    <details className="source-history">
      <summary>{t("Source grouping & import history")}</summary>
      <p>
        {conversation.provider} · {conversation.sourceFormat}
      </p>
      {conversation.externalProjectId && (
        <p>
          {t("Original project")}:{" "}
          {conversation.sourceProjectName || conversation.externalProjectId}
        </p>
      )}
      {conversation.sourcePath && (
        <p>
          {t("Original workspace path")}: {conversation.sourcePath}
        </p>
      )}
      <label>
        {t("Application project")}
        <select
          disabled={!canEdit}
          value={project}
          onChange={(e) => {
            const value = e.target.value;
            act(async () => {
              await api(
                "/conversations/" + conversation.id + "/project",
                { projectId: value || null },
                "PUT",
              );
              setProject(value);
              onUpdated?.();
            });
          }}
        >
          <option value="">{t("Unassigned")}</option>
          {projects.map((p) => (
            <option value={p.id} key={p.id}>
              {p.name}
            </option>
          ))}
        </select>
      </label>
      {canEdit && (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            act(async () => {
              const p = await api(
                "/w/" + conversation.workspaceId + "/projects",
                { name: newName },
              );
              await api(
                "/conversations/" + conversation.id + "/project",
                { projectId: p.id },
                "PUT",
              );
              setProject(p.id);
              setNewName("");
              await load();
              onUpdated?.();
            });
          }}
        >
          <input
            aria-label={t("New application project")}
            placeholder={t("New application project")}
            value={newName}
            maxLength={150}
            onChange={(e) => setNewName(e.target.value)}
          />
          <button disabled={!newName.trim()}>{t("Create & associate")}</button>
        </form>
      )}
      {links.map((r) => (
        <div key={r.id}>
          <p>
            {r.kind} · {r.status}
          </p>
          {r.targetConversationId ? (
            <a
              href={
                "#explorer/" +
                (r.conversationId === conversation.id
                  ? r.targetConversationId
                  : r.conversationId)
              }
            >
              {t("Open linked session")}
            </a>
          ) : (
            <>
              <small>
                {t("Unresolved source ID")}: {r.externalTargetId}
              </small>
              {canEdit && (
                <select
                  aria-label={t("Resolve session relationship")}
                  value=""
                  onChange={(e) =>
                    act(async () => {
                      await api(
                        "/source-relationships/" + r.id,
                        { targetId: e.target.value },
                        "PUT",
                      );
                      await load();
                    })
                  }
                >
                  <option value="">{t("Associate a session…")}</option>
                  {picker.items
                    .filter((c) => c.id !== conversation.id)
                    .map((c) => (
                      <option value={c.id} key={c.id}>
                        {c.title}
                      </option>
                    ))}
                </select>
              )}
            </>
          )}
        </div>
      ))}
      {reviewResult && <p role="status">{reviewResult}</p>}
      {conflicts.map((conflict) => (
        <details key={conflict.id}>
          <summary>
            {t("Changed source message")} · {conflict.status}
          </summary>
          <p>
            {t(
              "The original message and its annotations were preserved. Incoming version:",
            )}
          </p>
          <pre>
            {JSON.parse(conflict.incoming)
              .blocks.map((b: any) => b.text)
              .join("\n")}
          </pre>
          {canEdit && conflict.status === "unresolved" && (
            <div>
              {confirm === conflict.id ? (
                <div>
                  <p>
                    {t(
                      "Accept this source revision? The previous version is retained. Comments whose quotes cannot be recovered will be marked for review.",
                    )}
                  </p>
                  <button
                    onClick={() =>
                      act(async () => {
                        const result = await api(
                          "/import-conflicts/" + conflict.id + "/accept",
                          {},
                        );
                        setReviewResult(
                          `${result.recovered} annotations recovered · ${result.orphaned} need review`,
                        );
                        setConfirm(null);
                        await load();
                        onUpdated?.();
                      })
                    }
                  >
                    {t("Accept revision")}
                  </button>
                  <button onClick={() => setConfirm(null)}>
                    {t("Cancel")}
                  </button>
                </div>
              ) : (
                <button onClick={() => setConfirm(conflict.id)}>
                  {t("Review & accept incoming")}
                </button>
              )}
              <button
                onClick={() =>
                  act(async () => {
                    await api(
                      "/import-conflicts/" + conflict.id + "/keep-original",
                      {},
                    );
                    await load();
                  })
                }
              >
                {t("Keep original · mark reviewed")}
              </button>
            </div>
          )}
        </details>
      ))}
      <RecoveredSource
        key={conflicts.map((c) => c.status).join()}
        id={conversation.id}
      />
      <details>
        <summary>{t("Provenance")}</summary>
        <pre>
          {JSON.stringify(JSON.parse(conversation.provenance || "{}"), null, 2)}
        </pre>
      </details>
    </details>
  );
}
