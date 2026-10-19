import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { useEffect, useState } from "react";
import { LoaderCircle } from "lucide-react";
import { t, useLocale, formatNumber } from "../../shared/i18n/i18n";
import "./import-activity.css";
export function formatByteSize(bytes) {
    const units = [
        [1_000_000_000, "GB"],
        [1_000_000, "MB"],
        [1_000, "KB"],
    ];
    for (const [size, label] of units) {
        if (bytes >= size) {
            const value = Math.round((bytes / size) * 100) / 100;
            return `${formatNumber(value)} ${label}`;
        }
    }
    return `${formatNumber(bytes)} B`;
}
export function ImportSizeMeter({ progress, compressedBytes = 0, limitBytes = 0, }) {
    const limit = progress?.limitBytes || limitBytes;
    const compressed = progress?.compressedBytes || compressedBytes;
    const serverSized = (progress?.limitBytes ?? 0) > 0;
    if (!limit || (compressed <= 0 && !serverSized))
        return null;
    const used = serverSized ? (progress?.usedBytes ?? 0) : compressed;
    return (_jsxs("div", { className: "import-sizes", children: [_jsx("p", { children: t("Compressed: {size}", { size: formatByteSize(compressed) }) }), serverSized ? (_jsx("p", { children: t("Uncompressed: {size}", {
                    size: formatByteSize(progress?.uncompressedBytes ?? 0),
                }) })) : (_jsx("p", { children: t("Calculating uncompressed size…") })), _jsx("p", { children: t("{used} of {limit} toward your import limit", {
                    used: formatByteSize(used),
                    limit: formatByteSize(limit),
                }) }), _jsx("progress", { "aria-label": t("Import size"), max: limit, value: Math.min(Math.max(used, 0), limit) })] }));
}
export function FileProgressCount({ progress }) {
    return (_jsxs("div", { children: [_jsx("p", { children: t("{processed} of {total} files processed", progress) }), _jsx("progress", { "aria-label": t("Files processed"), max: Math.max(progress.total, 1), value: progress.processed }), progress.skipped > 0 && (_jsx("p", { children: t("{count} files skipped", { count: progress.skipped }) }))] }));
}
function useElapsed() {
    const [seconds, setSeconds] = useState(0);
    useEffect(() => {
        const started = Date.now();
        const timer = window.setInterval(() => setSeconds(Math.floor((Date.now() - started) / 1000)), 1000);
        return () => window.clearInterval(timer);
    }, []);
    return seconds;
}
export function ImportActivity({ progress, compressedBytes = 0, limitBytes = 0, }) {
    useLocale();
    const seconds = useElapsed();
    return (_jsxs("div", { className: "import-activity", role: "status", children: [_jsx(LoaderCircle, { className: "import-activity-spinner", size: 36, "aria-hidden": "true" }), _jsx("h3", { children: t("Reading your sources…") }), progress?.stage === "counting" ? (_jsx("p", { children: t("Counting files in your archives…") })) : progress && progress.total > 0 ? (_jsx(FileProgressCount, { progress: progress })) : (_jsx("p", { children: t("Uploading files…") })), _jsx(ImportSizeMeter, { progress: progress, compressedBytes: compressedBytes, limitBytes: limitBytes }), _jsx("p", { children: t("Uploading, extracting and reading your files. Large archives can take several minutes. Keep this window open.") }), _jsx("p", { className: "muted", "aria-live": "off", children: t("Elapsed: {seconds} seconds", { seconds }) })] }));
}
export function ImportCommitActivity({ batch, sizes, compressedBytes = 0, limitBytes = 0, }) {
    useLocale();
    const seconds = useElapsed();
    const done = (batch.imported || 0) + (batch.duplicates || 0) + (batch.updated || 0);
    const total = batch.total || 0;
    return (_jsxs("div", { className: "import-activity", role: "status", children: [_jsx(LoaderCircle, { className: "import-activity-spinner", size: 36, "aria-hidden": "true" }), _jsx("h3", { children: t("Importing conversations…") }), _jsx(ImportSizeMeter, { progress: sizes, compressedBytes: compressedBytes, limitBytes: limitBytes }), _jsx("p", { children: t("{done} of {total} conversations", { done, total }) }), _jsx("progress", { "aria-label": t("Conversations imported"), max: Math.max(total, 1), value: done }), batch.fileProgress && batch.fileProgress.total > 0 && (_jsx(FileProgressCount, { progress: batch.fileProgress })), _jsx("p", { children: t("Importing your selected conversations. Keep this window open.") }), _jsx("p", { className: "muted", "aria-live": "off", children: t("Elapsed: {seconds} seconds", { seconds }) })] }));
}
