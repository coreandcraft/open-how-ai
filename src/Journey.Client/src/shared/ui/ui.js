import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { t, useLocale } from "../i18n/i18n";
import { X } from "lucide-react";
import { providerClass } from "../state";
export function Modal({ title, onClose, children, wide = false, }) {
    useLocale();
    return (_jsx("div", { className: "overlay", onMouseDown: (e) => {
            if (e.target === e.currentTarget)
                onClose();
        }, children: _jsxs("section", { className: "modal " + (wide ? "wide" : ""), role: "dialog", "aria-modal": "true", "aria-label": title, children: [_jsxs("header", { children: [_jsx("h2", { children: title }), _jsx("button", { "aria-label": t("Close dialog"), onClick: onClose, children: _jsx(X, { size: 18 }) })] }), children] }) }));
}
export function Badge({ provider }) {
    useLocale();
    return (_jsxs("span", { className: "provider " + providerClass(provider), children: [provider === "ChatGPT" ? "◉" : provider === "Claude" ? "✳" : "⌘", " ", _jsx("span", { children: provider })] }));
}
