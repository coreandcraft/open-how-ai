import { useState, useRef } from "react";
import { GitBranch } from "lucide-react";
import { Modal } from "../../shared/ui/ui";
import { api, go, useApp } from "../../shared/state";
import { t, useLocale } from "../../shared/i18n/i18n";

export function BuildJourneyDialog({
  selected,
  close,
  refresh,
}: {
  selected: { id: string; title: string }[];
  close: () => void;
  refresh: () => void;
}) {
  useLocale();
  const { workspace, notify } = useApp();
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const submitting = useRef(false);
  return (
    <Modal
      title={t("Build journey from selected conversations")}
      onClose={() => !submitting.current && close()}
    >
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          if (submitting.current) return;
          submitting.current = true;
          setBusy(true);
          setError("");
          try {
            const journey = await api("/w/" + workspace + "/journeys/build", {
              name: name.trim(),
              conversationIds: selected.map((c) => c.id),
            });
            if (useApp.getState().workspace !== workspace) return;
            refresh();
            close();
            notify(
              t(
                "Draft created. You can edit the timeline, nodes, and connections.",
              ),
            );
            go("canvas/" + journey.id);
          } catch (e) {
            setError(e instanceof Error ? e.message : t("Request failed"));
          } finally {
            submitting.current = false;
            setBusy(false);
          }
        }}
      >
        <label>
          {t("Journey title")}
          <input
            autoFocus
            required
            maxLength={150}
            value={name}
            disabled={busy}
            onChange={(e) => setName(e.target.value)}
          />
        </label>
        <p>
          {t(
            "Create a private, editable draft with chronological placement, separate thread and tool lanes, and recorded relationships.",
          )}
        </p>
        <p className="muted">
          {t(
            "Missing times stay unknown. Overlapping activity does not imply a dependency.",
          )}
        </p>
        <details>
          <summary>
            {t("{count} conversations selected", { count: selected.length })}
          </summary>
          <ul>
            {selected.map((c) => (
              <li key={c.id}>{c.title}</li>
            ))}
          </ul>
        </details>
        {error && (
          <p role="alert" className="danger">
            {error}
          </p>
        )}
        <button
          className="primary"
          disabled={
            busy || !name.trim() || !selected.length || selected.length > 100
          }
        >
          <GitBranch size={16} />
          {t(busy ? "Building draft…" : "Create editable draft")}
        </button>
      </form>
    </Modal>
  );
}
