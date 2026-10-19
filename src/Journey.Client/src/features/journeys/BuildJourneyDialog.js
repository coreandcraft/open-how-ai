import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { useState, useRef } from "react";
import { GitBranch } from "lucide-react";
import { Modal } from "../../shared/ui/ui";
import { api, go, useApp } from "../../shared/state";
import { t, useLocale } from "../../shared/i18n/i18n";
export function BuildJourneyDialog({ selected, close, refresh, }) {
    useLocale();
    const { workspace, notify } = useApp();
    const [name, setName] = useState("");
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState("");
    const submitting = useRef(false);
    return (_jsx(Modal, { title: t("Build journey from selected conversations"), onClose: () => !submitting.current && close(), children: _jsxs("form", { onSubmit: async (event) => {
                event.preventDefault();
                if (submitting.current)
                    return;
                submitting.current = true;
                setBusy(true);
                setError("");
                try {
                    const journey = await api("/w/" + workspace + "/journeys/build", {
                        name: name.trim(),
                        conversationIds: selected.map((c) => c.id),
                    });
                    if (useApp.getState().workspace !== workspace)
                        return;
                    refresh();
                    close();
                    notify(t("Draft created. You can edit the timeline, nodes, and connections."));
                    go("canvas/" + journey.id);
                }
                catch (e) {
                    setError(e instanceof Error ? e.message : t("Request failed"));
                }
                finally {
                    submitting.current = false;
                    setBusy(false);
                }
            }, children: [_jsxs("label", { children: [t("Journey title"), _jsx("input", { autoFocus: true, required: true, maxLength: 150, value: name, disabled: busy, onChange: (e) => setName(e.target.value) })] }), _jsx("p", { children: t("Create a private, editable draft with chronological placement, separate thread and tool lanes, and recorded relationships.") }), _jsx("p", { className: "muted", children: t("Missing times stay unknown. Overlapping activity does not imply a dependency.") }), _jsxs("details", { children: [_jsx("summary", { children: t("{count} conversations selected", { count: selected.length }) }), _jsx("ul", { children: selected.map((c) => (_jsx("li", { children: c.title }, c.id))) })] }), error && (_jsx("p", { role: "alert", className: "danger", children: error })), _jsxs("button", { className: "primary", disabled: busy || !name.trim() || !selected.length || selected.length > 100, children: [_jsx(GitBranch, { size: 16 }), t(busy ? "Building draft…" : "Create editable draft")] })] }) }));
}
