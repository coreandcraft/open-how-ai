import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { ChevronRight, CircleCheck, FileJson, Sparkles, } from "lucide-react";
import { t, useLocale } from "../../shared/i18n/i18n";
import { describeActivity, sourceText, } from "./messagePresentation";
import { SourcePayload } from "./JsonPayload";
export function SourceDisclosure({ icon: Icon, title, detail, detailTitle, detailFallback, path, sourceLabel, payload, sourceRef, plain = false, activity, mark, summaryExtra, footer, }) {
    return (_jsxs("div", { className: "tool-call-entry", children: [_jsxs("details", { className: "tool-call", "data-activity": activity, children: [_jsxs("summary", { children: [_jsx(Icon, { className: "tool-call-icon", size: 16, "aria-hidden": "true" }), _jsxs("span", { className: "tool-call-label", children: [_jsx("strong", { children: title }), mark && _jsx("span", { className: "activity-mark", children: mark }), detail ? (_jsx("span", { className: "tool-call-target", title: detailTitle || detail, children: detail })) : (_jsx("span", { className: "tool-call-hint", children: detailFallback }))] }), summaryExtra, _jsx(ChevronRight, { className: "tool-call-chevron", size: 15, "aria-hidden": "true" })] }), _jsxs("div", { className: "tool-call-source", children: [path && _jsx("div", { className: "tool-call-path", children: path }), _jsx("span", { className: "tool-call-source-label", children: sourceLabel }), _jsx(SourcePayload, { text: payload, sourceRef: sourceRef, plain: plain })] })] }), footer] }));
}
export function SourceActivity({ block, sourceRef, plain = false, }) {
    useLocale();
    const activity = describeActivity(block);
    const Icon = activity.tone === "progress"
        ? Sparkles
        : activity.tone === "result"
            ? CircleCheck
            : FileJson;
    return (_jsx(SourceDisclosure, { icon: Icon, title: t(activity.label), detail: activity.preview, detailFallback: t("Preserved source block"), sourceLabel: t("Original source payload"), payload: sourceText(block), sourceRef: sourceRef, plain: plain, activity: activity.tone, mark: activity.tone === "progress"
            ? t("Progress")
            : activity.tone === "result"
                ? t("Result")
                : undefined }));
}
