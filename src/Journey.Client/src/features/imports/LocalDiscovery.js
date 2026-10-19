import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { api, act } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
export function LocalDiscovery({ onFiles, busy, }) {
    const [settings, setSettings] = useState(null), [roots, setRoots] = useState([]), [exclude, setExclude] = useState("");
    const [consent, setConsent] = useState(false), [files, setFiles] = useState([]), [chosen, setChosen] = useState([]), [loading, setLoading] = useState(false);
    useEffect(() => {
        act(async () => setSettings(await api("/imports/discovery")));
    }, []);
    return (_jsxs("details", { children: [_jsx("summary", { children: t("Find local tool histories (optional)") }), _jsx("p", { children: t(settings?.explanation || "Checking local discovery availability…") }), settings?.enabled && (_jsxs(_Fragment, { children: [_jsx("p", { children: t("Choose directories to inspect. Scanning lists transcript filenames and sizes; selected files are read only when you import them.") }), settings.roots.map((r) => (_jsxs("label", { children: [_jsx("input", { type: "checkbox", checked: roots.includes(r.id), onChange: (e) => setRoots(e.target.checked
                                    ? [...roots, r.id]
                                    : roots.filter((id) => id !== r.id)) }), r.provider, " \u00B7 ", r.path] }, r.id))), _jsxs("label", { children: [t("Exclude relative directories (one per line)"), _jsx("textarea", { value: exclude, onChange: (e) => setExclude(e.target.value) })] }), _jsxs("label", { children: [_jsx("input", { type: "checkbox", checked: consent, onChange: (e) => setConsent(e.target.checked) }), t("Allow read-only inspection of the selected transcript directories on this computer.")] }), _jsx("button", { disabled: !consent || !roots.length || loading || busy, onClick: () => act(async () => {
                            setLoading(true);
                            try {
                                setFiles(await api("/imports/discovery/scan", {
                                    roots,
                                    excludeDirectories: exclude
                                        .split("\n")
                                        .map((s) => s.trim())
                                        .filter(Boolean),
                                    consent,
                                }));
                                setChosen([]);
                            }
                            finally {
                                setLoading(false);
                            }
                        }), children: t("Scan selected directories") }), files.map((f) => (_jsxs("label", { children: [_jsx("input", { type: "checkbox", checked: chosen.includes(f.id), onChange: (e) => setChosen(e.target.checked
                                    ? [...chosen, f.id]
                                    : chosen.filter((id) => id !== f.id)) }), f.relativePath, " \u00B7 ", f.size.toLocaleString(), " bytes"] }, f.id))), files.length > 0 && (_jsx("button", { disabled: !chosen.length || loading || busy, onClick: () => act(async () => {
                            setLoading(true);
                            try {
                                const selected = [];
                                for (const f of files.filter((f) => chosen.includes(f.id))) {
                                    const response = await fetch(`/api/imports/discovery/files/${f.id}`);
                                    if (!response.ok)
                                        throw new Error("Source changed or discovery expired; scan again.");
                                    selected.push(new File([await response.blob()], f.relativePath));
                                }
                                await onFiles(selected);
                            }
                            finally {
                                setLoading(false);
                            }
                        }), children: t("Preview selected histories") }))] }))] }));
}
