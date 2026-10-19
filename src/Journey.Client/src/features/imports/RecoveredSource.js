import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { api, act } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
import { SuppliedFile } from "./SuppliedFile";
export function RecoveredSource({ id }) {
    const [data, setData] = useState(null);
    const [versions, setVersions] = useState([]);
    useEffect(() => {
        act(async () => {
            const [source, history] = await Promise.all([
                api(`/conversations/${id}/recovered-source`),
                api(`/conversations/${id}/versions`),
            ]);
            setData(source);
            setVersions(history);
        });
    }, [id]);
    if (!data)
        return null;
    return (_jsxs("details", { className: "source-history", children: [_jsx("summary", { children: t("Recovered files, tools & revisions") }), _jsx("p", { children: t("Source evidence · private workspace content") }), data.attachments.length === 0 && (_jsx("p", { children: t("No attachment references recorded.") })), data.attachments.map((a) => (_jsx(SuppliedFile, { file: a }, a.id))), data.executions.map((e) => (_jsxs("details", { children: [_jsxs("summary", { children: [e.toolName || e.externalCallId, " \u00B7 ", e.status] }), _jsxs("p", { children: [t("Explicit source call ID"), ": ", e.externalCallId] }), _jsx("pre", { children: e.arguments }), _jsx("pre", { children: e.result })] }, e.id))), versions.map((v) => (_jsxs("details", { children: [_jsxs("summary", { children: [t("Previous source version"), " \u00B7", " ", new Date(v.createdAt).toLocaleString()] }), _jsx("pre", { children: JSON.parse(v.payload)
                            .blocks.map((b) => b.text)
                            .join("\n") })] }, v.id)))] }));
}
