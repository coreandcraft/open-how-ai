import { jsx as _jsx, jsxs as _jsxs, Fragment as _Fragment } from "react/jsx-runtime";
import { LocalDiscovery } from "./LocalDiscovery";
import { ImportActivity, ImportCommitActivity, ImportSizeMeter, FileProgressCount, } from "./ImportActivity";
import { ReplacementRules } from "./SanitizationSettings";
import { t, useLocale, count } from "../../shared/i18n/i18n";
import { useState, useRef, useEffect } from "react";
import { Upload, Check, AlertCircle, ArrowRight } from "lucide-react";
import { api, act, useApp } from "../../shared/state";
import { Modal, Badge } from "../../shared/ui/ui";
export function ImportDialog({ close, done, workspaces, }) {
    useLocale();
    const { workspace, notify } = useApp();
    const [limits, setLimits] = useState(null);
    const [capabilities, setCapabilities] = useState([]);
    const [projects, setProjects] = useState([]);
    const [projectId, setProjectId] = useState("");
    const [newProjectName, setNewProjectName] = useState("");
    const [identity, setIdentity] = useState("default");
    const [adapterId, setAdapterId] = useState("");
    const [mappingEnabled, setMappingEnabled] = useState(false);
    const [mapping, setMapping] = useState({
        role: "role",
        content: "content",
        id: "id",
        parent: "parentId",
        conversation: "conversationId",
        project: "projectId",
        timestamp: "timestamp",
        provider: "Mapped",
        arrayPath: "",
        table: "",
    });
    const [includeAttachments, setIncludeAttachments] = useState(true);
    const [updateExisting, setUpdateExisting] = useState(true);
    const [target, setTarget] = useState(workspace);
    const [preview, setPreview] = useState(null);
    const [selected, setSelected] = useState([]);
    const [busy, setBusy] = useState(false);
    const [progress, setProgress] = useState(null);
    const [inspect, setInspect] = useState(null);
    const [sanitize, setSanitize] = useState(false);
    const [scanPatterns, setScanPatterns] = useState(false);
    const [rules, setRules] = useState([]);
    const [rulesLoaded, setRulesLoaded] = useState(false);
    const [pendingOriginals, setPendingOriginals] = useState(null);
    const [progressId, setProgressId] = useState(null);
    const [fileProgress, setFileProgress] = useState(null);
    const [uploadBytes, setUploadBytes] = useState(0);
    useEffect(() => {
        if (!progressId)
            return;
        let active = true;
        let timer;
        const poll = async () => {
            try {
                const next = await api(`/w/${target}/imports/progress/${progressId}`);
                if (active)
                    setFileProgress(next);
            }
            catch {
                /* Keep the last count when a progress request fails. */
            }
            if (active)
                timer = setTimeout(poll, 700);
        };
        void poll();
        return () => {
            active = false;
            clearTimeout(timer);
        };
    }, [progressId, target]);
    useEffect(() => {
        act(async () => {
            setRules(await api("/me/sanitization"));
            setRulesLoaded(true);
        });
    }, []);
    useEffect(() => {
        act(async () => {
            const [adapters, uploadLimits] = await Promise.all([
                api("/imports/capabilities"),
                api("/imports/limits"),
            ]);
            setCapabilities(adapters);
            setLimits(uploadLimits);
        });
    }, []);
    useEffect(() => {
        setProjectId("");
        act(async () => setProjects(await api("/w/" + target + "/projects")));
    }, [target]);
    const input = useRef(null);
    const folder = useRef(null);
    const dropFiles = async (transfer) => {
        const entries = Array.from(transfer.items)
            .map((item) => item.webkitGetAsEntry?.())
            .filter(Boolean);
        if (!entries.length) {
            await upload(transfer.files);
            return;
        }
        const files = [];
        async function walk(entry, path = "") {
            if (files.length >= 500)
                throw new Error(t("Select at most {count} files.", { count: 500 }));
            if (entry.isFile) {
                const file = await new Promise((resolve, reject) => entry.file(resolve, reject));
                files.push(new File([file], path + file.name, { type: file.type }));
            }
            else if (entry.isDirectory) {
                const reader = entry.createReader();
                let children;
                do {
                    children = await new Promise((resolve, reject) => reader.readEntries(resolve, reject));
                    for (const child of children)
                        await walk(child, path + entry.name + "/");
                } while (children.length);
            }
        }
        try {
            for (const entry of entries)
                await walk(entry);
            await upload(files);
        }
        catch (e) {
            notify(e.message);
        }
    };
    const upload = async (files, allowOriginalFiles = false) => {
        if (busy || !files.length)
            return;
        setPendingOriginals(null);
        setFileProgress(null);
        setBusy(true);
        try {
            const currentLimits = await api("/imports/limits");
            setLimits(currentLimits);
            if (files.length > currentLimits.maxFiles)
                throw new Error(t("Select at most {count} files.", { count: currentLimits.maxFiles }));
            const bytes = Array.from(files).reduce((sum, file) => sum + file.size, 0);
            if (bytes > currentLimits.maxBytes)
                throw new Error(t("Conversation import exceeds your {limit} GB limit.", {
                    limit: currentLimits.maxBytes / 1_000_000_000,
                }));
            setUploadBytes(bytes);
            const form = new FormData();
            Array.from(files).forEach((f) => form.append("files", f, f.webkitRelativePath || f.name));
            form.append("sourceIdentity", identity);
            form.append("sanitization", JSON.stringify({
                enabled: sanitize,
                scanPatterns,
                rules: sanitize ? rules : [],
                allowOriginalFiles,
            }));
            form.append("adapterId", mappingEnabled ? "manual-mapping" : adapterId);
            if (mappingEnabled)
                form.append("mapping", JSON.stringify(mapping));
            const id = crypto.randomUUID();
            setProgressId(id);
            const result = await api("/w/" + target + "/imports/preview?progressId=" + id, form);
            if (result.fileProgress)
                setFileProgress(result.fileProgress);
            setPreview(result);
            setPendingOriginals(null);
            setSelected([
                ...result.conversations
                    .filter((c) => !c.duplicate)
                    .map((c) => c.key),
                ...(result.projects || []).map((p) => p.key),
            ]);
            setInspect(result.conversations[0]);
        }
        catch (e) {
            if (e.code ===
                "original_file_consent_required")
                setPendingOriginals(Array.from(files));
            else
                notify(e.message);
        }
        finally {
            setProgressId(null);
            setBusy(false);
        }
    };
    const commit = () => act(async () => {
        setBusy(true);
        setProgress({
            status: "queued",
            total: selected.length,
            imported: 0,
            duplicates: 0,
            updated: 0,
            conflicts: 0,
            error: "",
        });
        try {
            try {
                await api("/imports/" + preview.batchId + "/commit", {
                    keys: selected,
                    projectId: projectId || null,
                    newProjectName: newProjectName || null,
                    updateExisting,
                    includeAttachments,
                });
            }
            catch (e) {
                setProgress(null);
                throw e;
            }
            let b;
            do {
                await new Promise((r) => setTimeout(r, 800));
                b = await api("/imports/" + preview.batchId);
                setProgress(b);
            } while (["queued", "processing"].includes(b.status));
            done();
        }
        finally {
            setBusy(false);
        }
    });
    return (_jsxs(Modal, { title: t("Bring your conversations together"), onClose: close, wide: true, children: [_jsxs("div", { className: "wizard-steps", children: [_jsx("span", { className: !preview ? "active" : "", children: t("1 · Choose sources") }), _jsx("span", { className: preview && !progress ? "active" : "", children: t("2 · Review & select") }), _jsx("span", { className: progress ? "active" : "", children: t("3 · Import") })] }), _jsxs("label", { children: [t("Import destination"), _jsx("select", { disabled: !!preview || busy, value: target, onChange: (e) => setTarget(e.target.value), children: workspaces
                            .filter((w) => ["Owner", "Admin", "Editor"].includes(w.role))
                            .map((w) => (_jsx("option", { value: w.id, children: w.name }, w.id))) })] }), !preview && (_jsxs("div", { className: "panel", children: [_jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: sanitize, disabled: busy || !rulesLoaded, onChange: (e) => setSanitize(e.target.checked) }), t("Sanitize sensitive information before import")] }), sanitize && (_jsxs(_Fragment, { children: [_jsx("p", { children: t("Sanitizes text source files before storage, including filenames and metadata. Archives retain extracted files. Files that cannot be sanitized require your permission to retain originals. Existing imports are not changed.") }), _jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: scanPatterns, disabled: busy, onChange: (e) => setScanPatterns(e.target.checked) }), t("Also redact common credential, email and local path patterns")] }), _jsx(ReplacementRules, { rules: rules, onChange: setRules, disabled: busy }), _jsx("small", { children: t("Changes here apply only to this import. Review the sanitized preview before importing. Pattern scanning is not exhaustive.") })] }))] })), !preview && (_jsxs("details", { className: "import-options", children: [_jsx("summary", { children: t("Format detection and field mapping") }), _jsxs("label", { children: [t("Source account or installation label"), _jsx("input", { value: identity, onChange: (e) => setIdentity(e.target.value) })] }), _jsx("small", { children: t("Use the same label when importing updates; use different labels for different accounts.") }), _jsxs("label", { children: [t("Import adapter"), _jsxs("select", { value: adapterId, onChange: (e) => setAdapterId(e.target.value), children: [_jsx("option", { value: "", children: t("Automatic detection") }), capabilities
                                        .filter((c) => c.id !== "manual-mapping" && c.id !== "private-asset")
                                        .map((c) => (_jsxs("option", { value: c.id, disabled: c.status === "pending sample validation", children: [c.provider, " \u00B7 ", c.status] }, c.id)))] })] }), _jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: mappingEnabled, onChange: (e) => setMappingEnabled(e.target.checked) }), t("Map an unknown structured format")] }), mappingEnabled && (_jsx("div", { className: "mapping-fields", children: Object.entries(mapping).map(([key, value]) => (_jsxs("label", { children: [key, _jsx("input", { value: value, onChange: (e) => setMapping((old) => ({ ...old, [key]: e.target.value })) })] }, key))) }))] })), pendingOriginals && !preview && (_jsxs("div", { className: "panel", role: "alert", children: [_jsx("h3", { children: t("Some files need your permission") }), _jsx("p", { children: t("Some files cannot be sanitized. Keeping them will store their original contents, which may include sensitive information. This permission applies only to the selected upload.") }), _jsx("button", { disabled: busy, onClick: () => void upload(pendingOriginals, true), children: t("Keep original files and continue") }), _jsx("button", { disabled: busy, onClick: () => setPendingOriginals(null), children: t("Cancel") })] })), preview?.retainedOriginals?.length > 0 && (_jsxs("details", { className: "panel retained-originals", children: [_jsxs("summary", { children: [_jsx("span", { children: t("Original files retained with your permission") }), _jsx("span", { className: "muted", children: count("files", preview.retainedOriginals.length) })] }), _jsx("p", { children: t("These files were not sanitized and may contain sensitive information.") }), _jsx("ul", { children: preview.retainedOriginals.map((name, i) => (_jsx("li", { children: name }, i))) })] })), !preview ? (_jsxs(_Fragment, { children: [_jsxs("div", { className: "dropzone", onDragOver: (e) => e.preventDefault(), onDrop: (e) => {
                            e.preventDefault();
                            if (!busy)
                                void dropFiles(e.dataTransfer);
                        }, children: [busy ? (_jsx(ImportActivity, { progress: fileProgress, compressedBytes: uploadBytes, limitBytes: limits?.maxBytes ?? 0 })) : (_jsxs(_Fragment, { children: [_jsx(Upload, { size: 32 }), _jsx("h3", { children: t("Drop your conversation exports here") })] })), _jsx("p", { children: t("JSON, JSONL, SQLite, CSV, text, ZIP or TAR.GZ") }), _jsx("p", { children: limits
                                    ? t("Up to {limit} GB per import, including extracted archive contents.", { limit: limits.maxBytes / 1_000_000_000 })
                                    : t("Checking your import limit…") }), _jsx(LocalDiscovery, { onFiles: upload, busy: busy }), _jsxs("div", { className: "row", children: [_jsx("button", { disabled: busy || !limits, className: "primary", onClick: () => input.current?.click(), children: t("Choose files") }), _jsx("button", { disabled: busy || !limits, onClick: () => folder.current?.click(), children: t("Choose folder") })] })] }), _jsx("input", { ref: input, type: "file", multiple: true, hidden: true, onChange: (e) => e.target.files && upload(e.target.files) }), _jsx("input", { ref: folder, type: "file", multiple: true, hidden: true, ...{ webkitdirectory: "" }, onChange: (e) => e.target.files && upload(e.target.files) }), _jsxs("div", { className: "import-formats", children: [_jsx(Badge, { provider: "ChatGPT" }), _jsx(Badge, { provider: "Claude" }), _jsx("span", { children: t("Generic JSON / Markdown") })] }), _jsx("p", { className: "muted", children: t(sanitize
                            ? "Review sanitized copies and any originals you explicitly allowed. Pattern scanning cannot identify every sensitive value."
                            : "Original files are preserved unchanged. Detection uses source content. Cursor SQLite support is experimental; unsupported schemas are reported, never guessed.") })] })) : progress ? (_jsx("div", { className: "import-result", children: busy ? (_jsxs(_Fragment, { children: [_jsx(ImportCommitActivity, { batch: progress, sizes: fileProgress, compressedBytes: uploadBytes, limitBytes: limits?.maxBytes ?? 0 }), _jsx("button", { onClick: () => act(async () => {
                                await api("/imports/" + preview.batchId + "/cancel", {});
                            }), children: t("Cancel import") })] })) : (_jsxs(_Fragment, { children: [_jsx(Check, { size: 36 }), _jsx("h2", { children: t("Import complete") }), progress.fileProgress?.total > 0 && (_jsx(FileProgressCount, { progress: progress.fileProgress })), _jsx(ImportSizeMeter, { progress: fileProgress, compressedBytes: uploadBytes, limitBytes: limits?.maxBytes ?? 0 }), _jsx("progress", { "aria-label": t("Conversations imported"), max: Math.max(progress.total, 1), value: progress.imported +
                                progress.duplicates +
                                (progress.updated || 0) }), _jsx("p", { children: t("Import summary", {
                                imported: progress.imported,
                                duplicates: progress.duplicates,
                            }) }), _jsxs("p", { children: [progress.updated || 0, " ", t("updated conversations"), " \u00B7", " ", progress.conflicts || 0, " ", t("changes needing review")] }), progress.error && _jsx("pre", { className: "error", children: progress.error }), _jsx("button", { className: "primary", onClick: close, children: t("Back to workspace") })] })) })) : (_jsxs(_Fragment, { children: [_jsx(ImportSizeMeter, { progress: fileProgress, compressedBytes: uploadBytes, limitBytes: limits?.maxBytes ?? 0 }), preview.skippedFiles?.length > 0 && (_jsxs("details", { open: true, children: [_jsx("summary", { children: t("{count} files skipped", {
                                    count: preview.skippedFiles.length,
                                }) }), _jsx("p", { children: t("Supported files will still be imported.") }), _jsx("ul", { children: preview.skippedFiles.map((file, i) => (_jsxs("li", { children: [file.name, ": ", t(file.reason)] }, i))) })] })), _jsxs("div", { className: "import-project-options", children: [_jsxs("label", { children: [t("Application project"), _jsxs("select", { value: projectId, disabled: !!newProjectName, onChange: (e) => setProjectId(e.target.value), children: [_jsx("option", { value: "", children: t("No application project") }), projects.map((p) => (_jsx("option", { value: p.id, children: p.name }, p.id)))] })] }), _jsxs("label", { children: [t("Or create a project"), _jsx("input", { value: newProjectName, maxLength: 150, onChange: (e) => {
                                            setNewProjectName(e.target.value);
                                            setProjectId("");
                                        } })] }), _jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: updateExisting, onChange: (e) => setUpdateExisting(e.target.checked) }), t("Append updates to existing conversations; preserve changed messages for review")] })] }), (preview.attachments?.length ?? 0) > 0 && (_jsxs("details", { open: true, children: [_jsxs("summary", { children: [t("Supplied attachments"), " \u00B7 ", preview.attachments.length] }), _jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: includeAttachments, onChange: (e) => setIncludeAttachments(e.target.checked) }), t("Link supplied files to exact source references (private)")] }), preview.attachments.map((a, i) => (_jsxs("p", { children: [a.name, " \u00B7 ", a.size.toLocaleString(), " bytes"] }, i))), _jsx("p", { children: t("Unmatched files remain in the original import. No remote files are downloaded.") })] })), _jsxs("div", { className: "import-review", children: [_jsxs("div", { children: [(preview.projects || []).map((p) => (_jsxs("label", { className: "checkbox-row", children: [_jsx("input", { type: "checkbox", checked: selected.includes(p.key), onChange: (e) => setSelected((old) => e.target.checked
                                                    ? [...old, p.key]
                                                    : old.filter((key) => key !== p.key)) }), p.provider, " \u00B7 ", p.name, " \u00B7 ", t("Source project metadata")] }, p.key))), Object.entries(preview.conversations.reduce((groups, c) => {
                                        const key = c.provider +
                                            " / " +
                                            (c.sourceProjectName ||
                                                c.externalProjectId ||
                                                c.sourcePath ||
                                                "Ungrouped source");
                                        (groups[key] ||= []).push(c);
                                        return groups;
                                    }, {})).map(([group, items]) => (_jsxs("details", { open: true, children: [_jsxs("summary", { children: [_jsx("input", { type: "checkbox", "aria-label": t("Select group") + " " + group, checked: items
                                                            .filter((c) => !c.duplicate)
                                                            .every((c) => selected.includes(c.key)), onChange: (e) => {
                                                            const keys = items
                                                                .filter((c) => !c.duplicate)
                                                                .map((c) => c.key);
                                                            setSelected((old) => e.target.checked
                                                                ? [...new Set([...old, ...keys])]
                                                                : old.filter((key) => !keys.includes(key)));
                                                        } }), group] }), items.map((c) => (_jsxs("div", { className: "import-item" +
                                                    (items.some((parent) => parent.providerId ===
                                                        c.parentConversationExternalId)
                                                        ? " import-child-session"
                                                        : ""), children: [_jsx("input", { type: "checkbox", checked: selected.includes(c.key), disabled: c.duplicate || busy, onChange: (e) => setSelected((s) => e.target.checked
                                                            ? [...s, c.key]
                                                            : s.filter((k) => k !== c.key)) }), _jsxs("button", { onClick: () => setInspect(c), children: [_jsx("strong", { children: c.title }), _jsxs("small", { children: [c.provider, " \u00B7 ", count("messages", c.messageCount), c.duplicate
                                                                        ? t(" · Already imported")
                                                                        : c.update
                                                                            ? t(" · Update available")
                                                                            : "", c.parentConversationExternalId && (_jsxs("span", { children: [" ", "\u00B7 ", t("Child session of"), " ", c.parentConversationExternalId] }))] })] })] }, c.key)))] }, group))), preview.errors.map((e, i) => (_jsxs("p", { className: "error", children: [_jsx(AlertCircle, { size: 15 }), e] }, i)))] }), _jsxs("div", { className: "preview-reader", children: [_jsx("span", { className: "eyebrow", children: t(sanitize && !inspect?.originalRetained
                                            ? "SANITIZED PREVIEW"
                                            : "SOURCE PREVIEW") }), _jsx("h3", { children: inspect?.title }), _jsxs("p", { children: [inspect?.adapter?.status, " \u00B7 ", inspect?.adapter?.version] }), _jsx("small", { children: inspect?.adapter?.limitations }), inspect?.diagnostics &&
                                        JSON.parse(inspect.diagnostics).map((warning, i) => (_jsx("p", { className: "error", children: warning }, i))), inspect?.messages.map((m) => (_jsxs("div", { children: [_jsx("small", { children: t(m.role) }), _jsx("pre", { children: m.blocks
                                                    .map((b) => b.text || (b.kind !== "text" ? b.metadata : ""))
                                                    .join("\n") })] }, m.id)))] })] }), _jsxs("footer", { className: "modal-footer", children: [_jsx("span", { children: t("Selected conversations", { count: selected.length }) }), _jsx("button", { disabled: busy, onClick: () => {
                                    setPreview(null);
                                    setInspect(null);
                                }, children: t("Back") }), _jsxs("button", { className: "primary", disabled: !selected.length || busy, onClick: commit, children: [busy ? t("Importing…") : t("Import selected"), " ", _jsx(ArrowRight, { size: 16 })] })] })] }))] }));
}
