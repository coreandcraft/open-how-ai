import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { Code2, MessageSquare, Settings2, Sparkles, UserRound, Wrench, } from "lucide-react";
import { t } from "../../shared/i18n/i18n";
export function MessageRole({ role }) {
    const Icon = role === "user"
        ? UserRound
        : role === "assistant"
            ? Sparkles
            : role === "system"
                ? Settings2
                : role === "developer"
                    ? Code2
                    : role === "tool" || role === "function"
                        ? Wrench
                        : MessageSquare;
    return (_jsxs("span", { className: "message-role", children: [_jsx(Icon, { size: 16, "aria-hidden": "true" }), _jsx("span", { children: t(role) })] }));
}
