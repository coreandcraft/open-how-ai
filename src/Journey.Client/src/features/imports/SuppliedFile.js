import { jsxs as _jsxs, jsx as _jsx } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { t } from "../../shared/i18n/i18n";
function leaf(path) {
    const parts = path.split(/[\\/]/).filter(Boolean);
    return parts[parts.length - 1] || path;
}
export function SuppliedFile({ file }) {
    const name = leaf(file.name || "");
    const view = file.view || "download";
    const url = `/api/imported-attachments/${file.id}/content`;
    if (file.availability !== "available")
        return (_jsxs("p", { className: "supplied-file-missing", children: [name, " \u00B7 ", file.availability] }));
    return (_jsxs("figure", { className: "supplied-file", children: [_jsx("figcaption", { children: name }), view === "image" && _jsx("img", { src: url, alt: name }), view === "pdf" && _jsx("iframe", { src: url, title: name }), view === "video" && _jsx("video", { src: url, controls: true, preload: "metadata" }), view === "audio" && _jsx("audio", { src: url, controls: true, preload: "metadata" }), (view === "text" || view === "document") && _jsx(DocumentText, { id: file.id }), _jsx("a", { href: `${url}?download=1`, children: t("Download supplied file") })] }));
}
function DocumentText({ id }) {
    const [text, setText] = useState(null);
    const [missing, setMissing] = useState(false);
    useEffect(() => {
        let live = true;
        fetch(`/api/imported-attachments/${id}/text`)
            .then((response) => {
            if (!response.ok)
                throw new Error();
            return response.text();
        })
            .then((body) => {
            if (live)
                setText(body);
        })
            .catch(() => {
            if (live)
                setMissing(true);
        });
        return () => {
            live = false;
        };
    }, [id]);
    if (missing)
        return (_jsx("p", { children: t("Preview is not available for this document. Download the original file.") }));
    if (text == null)
        return null;
    return _jsx("pre", { className: "supplied-document", children: text });
}
