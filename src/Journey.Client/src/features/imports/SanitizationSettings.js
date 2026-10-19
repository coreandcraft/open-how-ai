import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { api, act, useApp } from "../../shared/state";
import { t, useLocale } from "../../shared/i18n/i18n";
export function ReplacementRules({ rules, onChange, disabled = false, }) {
    return (_jsxs("fieldset", { disabled: disabled, children: [_jsx("legend", { children: t("Automatic replacements") }), _jsx("p", { children: t("Exact, case-sensitive text. Longer phrases match first; replacements are applied once.") }), rules.map((rule, index) => (_jsxs("div", { className: "row", children: [_jsxs("label", { children: [t("Find text"), _jsx("input", { value: rule.find, maxLength: 1000, onChange: (e) => onChange(rules.map((r, i) => i === index ? { ...r, find: e.target.value } : r)) })] }), _jsxs("label", { children: [t("Replace with"), _jsx("input", { value: rule.replace, maxLength: 1000, onChange: (e) => onChange(rules.map((r, i) => i === index ? { ...r, replace: e.target.value } : r)) })] }), _jsx("button", { type: "button", "aria-label": t("Remove replacement") + " " + (index + 1), onClick: () => onChange(rules.filter((_, i) => i !== index)), children: t("Remove") })] }, index))), _jsx("button", { type: "button", disabled: rules.length >= 100, onClick: () => onChange([...rules, { find: "", replace: t("[REDACTED]") }]), children: t("Add replacement") })] }));
}
export function SanitizationSettings() {
    useLocale();
    const [rules, setRules] = useState([]);
    const [loaded, setLoaded] = useState(false);
    const [saving, setSaving] = useState(false);
    useEffect(() => {
        act(async () => {
            setRules(await api("/me/sanitization"));
            setLoaded(true);
        });
    }, []);
    return (_jsxs("div", { className: "panel settings", children: [_jsx("h3", { children: t("Sanitize sensitive information") }), _jsx("p", { children: t("Your account's saved rules are loaded for publishing and optional import sanitization.") }), _jsx(ReplacementRules, { rules: rules, onChange: setRules, disabled: !loaded || saving }), _jsx("button", { disabled: !loaded || saving || rules.some((r) => !r.find.trim()), onClick: () => act(async () => {
                    setSaving(true);
                    try {
                        await api("/me/sanitization", rules, "PUT");
                        useApp.getState().notify(t("Replacement rules saved"));
                    }
                    finally {
                        setSaving(false);
                    }
                }), children: saving ? t("Saving…") : t("Save replacement rules") })] }));
}
