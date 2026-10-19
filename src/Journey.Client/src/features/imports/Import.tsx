import { LocalDiscovery } from "./LocalDiscovery";
import {
  ImportActivity,
  ImportCommitActivity,
  ImportSizeMeter,
  FileProgressCount,
  ImportTimings,
  type FileProgress,
} from "./ImportActivity";
import { ReplacementRules, type ReplacementRule } from "./SanitizationSettings";
import { t, useLocale, count } from "../../shared/i18n/i18n";
import { useState, useRef, useEffect } from "react";
import { Upload, FileText, Check, AlertCircle, ArrowRight } from "lucide-react";
import { api, act, useApp } from "../../shared/state";
import { Modal, Badge } from "../../shared/ui/ui";
export function ImportDialog({
  close,
  done,
  workspaces,
}: {
  close: () => void;
  done: () => void;
  workspaces: any[];
}) {
  useLocale();
  const { workspace, notify } = useApp();
  const [limits, setLimits] = useState<{
    maxBytes: number;
    maxFiles: number;
  } | null>(null);
  const [capabilities, setCapabilities] = useState<any[]>([]);
  const [projects, setProjects] = useState<any[]>([]);
  const [projectId, setProjectId] = useState("");
  const [newProjectName, setNewProjectName] = useState("");
  const [identity, setIdentity] = useState("default");
  const [adapterId, setAdapterId] = useState("");
  const [mappingEnabled, setMappingEnabled] = useState(false);
  const [mapping, setMapping] = useState<Record<string, string>>({
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
  const [preview, setPreview] = useState<any>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [importError, setImportError] = useState("");
  const [progress, setProgress] = useState<any>(null);
  const [inspect, setInspect] = useState<any>(null);
  const [sanitize, setSanitize] = useState(false);
  const [scanPatterns, setScanPatterns] = useState(false);
  const [rules, setRules] = useState<ReplacementRule[]>([]);
  const [rulesLoaded, setRulesLoaded] = useState(false);
  const [pendingOriginals, setPendingOriginals] = useState<File[] | null>(null);
  const [progressId, setProgressId] = useState<string | null>(null);
  const [fileProgress, setFileProgress] = useState<FileProgress | null>(null);
  const [uploadBytes, setUploadBytes] = useState(0);
  const [retryingPreview, setRetryingPreview] = useState(false);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  useEffect(() => {
    if (!progressId) return;
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        const next = await api<FileProgress>(
          `/w/${target}/imports/progress/${progressId}`,
        );
        if (active) setFileProgress(next);
      } catch {
        /* Keep the last count when a progress request fails. */
      }
      if (active) timer = setTimeout(poll, 700);
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
  const input = useRef<HTMLInputElement>(null);
  const folder = useRef<HTMLInputElement>(null);
  const dropFiles = async (transfer: DataTransfer) => {
    const entries = Array.from(transfer.items)
      .map((item: any) => item.webkitGetAsEntry?.())
      .filter(Boolean);
    if (!entries.length) {
      await upload(Array.from(transfer.files));
      return;
    }
    const files: File[] = [];
    async function walk(entry: any, path = "") {
      if (entry.isFile) {
        if (files.length >= 500)
          throw new Error(t("Select at most {count} files.", { count: 500 }));
        const file: File = await new Promise((resolve, reject) =>
          entry.file(resolve, reject),
        );
        files.push(new File([file], path + file.name, { type: file.type }));
      } else if (entry.isDirectory) {
        const reader = entry.createReader();
        let children: any[];
        do {
          children = await new Promise((resolve, reject) =>
            reader.readEntries(resolve, reject),
          );
          for (const child of children)
            await walk(child, path + entry.name + "/");
        } while (children.length);
      }
    }
    setImportError("");
    try {
      for (const entry of entries) await walk(entry);
      await upload(files);
    } catch (e) {
      setImportError((e as Error).message);
      notify((e as Error).message);
    }
  };
  const upload = async (
    files: FileList | File[],
    allowOriginalFiles = false,
  ) => {
    if (busy) return;
    // The browser protects the drag data store after the drop handler returns.
    // Keep stable File references before any asynchronous request.
    const selectedFiles = Array.from(files);
    if (!selectedFiles.length) {
      setImportError(
        t("No files were found. Choose files or a folder and try again."),
      );
      return;
    }
    setImportError("");
    setPendingOriginals(null);
    setFileProgress(null);
    setRetryingPreview(false);
    setBusy(true);
    try {
      const currentLimits = await api<{ maxBytes: number; maxFiles: number }>(
        "/imports/limits",
      );
      setLimits(currentLimits);
      if (selectedFiles.length > currentLimits.maxFiles)
        throw new Error(
          t("Select at most {count} files.", { count: currentLimits.maxFiles }),
        );
      const bytes = selectedFiles.reduce((sum, file) => sum + file.size, 0);
      if (bytes > currentLimits.maxBytes)
        throw new Error(
          t("Conversation import exceeds your {limit} GB limit.", {
            limit: currentLimits.maxBytes / 1_000_000_000,
          }),
        );
      setUploadBytes(bytes);
      const form = new FormData();
      selectedFiles.forEach((f) =>
        form.append("files", f, (f as any).webkitRelativePath || f.name),
      );
      form.append("sourceIdentity", identity);
      form.append(
        "sanitization",
        JSON.stringify({
          enabled: sanitize,
          scanPatterns,
          rules: sanitize ? rules : [],
          allowOriginalFiles,
        }),
      );
      form.append("adapterId", mappingEnabled ? "manual-mapping" : adapterId);
      if (mappingEnabled) form.append("mapping", JSON.stringify(mapping));
      let result;
      // Keep the File objects and the exact submitted options until preview finishes. A
      // restart can lose temporary raw staging; resubmit once without asking for files again.
      for (let attempt = 0; ; attempt++) {
        const id = crypto.randomUUID();
        setProgressId(id);
        result = await api(
          "/w/" + target + "/imports/preview?background=true&progressId=" + id,
          form,
        );
        try {
          if (result?.previewId) {
            const previewId = result.previewId;
            setProgressId(previewId);
            let failures = 0;
            while (result?.status === "processing") {
              if (result.fileProgress) setFileProgress(result.fileProgress);
              await new Promise((resolve) =>
                setTimeout(resolve, failures ? Math.min(5000, 1000 * 2 ** failures) : 800),
              );
              if (!mounted.current) return;
              try {
                result = await api(`/w/${target}/imports/previews/${previewId}`);
                failures = 0;
              } catch (error) {
                const status = (error as Error & { status?: number }).status;
                // Brief gateway/network failures during a restart do not invalidate a job.
                if ((!status || [502, 503, 504].includes(status)) && ++failures <= 8)
                  continue;
                throw error;
              }
            }
          }
          break;
        } catch (error) {
          if ((error as Error & { status?: number }).status !== 410 || attempt > 0)
            throw error;
          setRetryingPreview(true);
          setFileProgress(null);
        }
      }
      if (!result || !Array.isArray(result.conversations))
        throw new Error(
          t("The import preview could not be read. Please try again."),
        );
      if (result.fileProgress) setFileProgress(result.fileProgress);
      setPreview(result);
      setPendingOriginals(null);
      setSelected([
        ...result.conversations
          .filter((c: any) => !c.duplicate)
          .map((c: any) => c.key),
        ...(result.projects || []).map((p: any) => p.key),
      ]);
      setInspect(result.conversations[0]);
    } catch (e) {
      if (
        (e as Error & { code?: string }).code ===
        "original_file_consent_required"
      )
        setPendingOriginals(selectedFiles);
      else {
        setImportError((e as Error).message);
        notify((e as Error).message);
      }
    } finally {
      setProgressId(null);
      setRetryingPreview(false);
      setBusy(false);
    }
  };
  const commit = () =>
    act(async () => {
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
        } catch (e) {
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
      } finally {
        setBusy(false);
      }
    });
  const selectKeys = (keys: string[], checked: boolean) =>
    setSelected((current) =>
      checked
        ? [...new Set([...current, ...keys])]
        : current.filter((key) => !keys.includes(key)),
    );
  const selectableKeys: string[] = preview
    ? [
        ...preview.conversations.map((c: any) => c.key),
        ...(preview.projects || []).map((p: any) => p.key),
      ]
    : [];
  return (
    <Modal title={t("Bring your conversations together")} onClose={close} wide>
      <div className="wizard-steps">
        <span className={!preview ? "active" : ""}>
          {t("1 · Choose sources")}
        </span>
        <span className={preview && !progress ? "active" : ""}>
          {t("2 · Review & select")}
        </span>
        <span className={progress ? "active" : ""}>{t("3 · Import")}</span>
      </div>
      <ImportTimings progress={progress?.fileProgress || fileProgress} />
      {retryingPreview && busy && (
        <p role="status">{t("The preview was interrupted. Retrying your upload…")}</p>
      )}
      <label>
        {t("Import destination")}
        <select
          disabled={!!preview || busy}
          value={target}
          onChange={(e) => setTarget(e.target.value)}
        >
          {workspaces
            .filter((w) => ["Owner", "Admin", "Editor"].includes(w.role))
            .map((w) => (
              <option value={w.id} key={w.id}>
                {w.name}
              </option>
            ))}
        </select>
      </label>
      {!preview && (
        <div className="panel">
          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={sanitize}
              disabled={busy || !rulesLoaded}
              onChange={(e) => setSanitize(e.target.checked)}
            />
            {t("Sanitize sensitive information before import")}
          </label>
          {sanitize && (
            <>
              <p>
                {t(
                  "Sanitizes text source files before storage, including filenames and metadata. Archives retain extracted files. Files that cannot be sanitized require your permission to retain originals. Existing imports are not changed.",
                )}
              </p>
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={scanPatterns}
                  disabled={busy}
                  onChange={(e) => setScanPatterns(e.target.checked)}
                />
                {t(
                  "Also redact common credential, email and local path patterns",
                )}
              </label>
              <ReplacementRules
                rules={rules}
                onChange={setRules}
                disabled={busy}
              />
              <small>
                {t(
                  "Changes here apply only to this import. Review the sanitized preview before importing. Pattern scanning is not exhaustive.",
                )}
              </small>
            </>
          )}
        </div>
      )}
      {!preview && (
        <details className="import-options">
          <summary>{t("Format detection and field mapping")}</summary>
          <label>
            {t("Source account or installation label")}
            <input
              value={identity}
              onChange={(e) => setIdentity(e.target.value)}
            />
          </label>
          <small>
            {t(
              "Use the same label when importing updates; use different labels for different accounts.",
            )}
          </small>
          <label>
            {t("Import adapter")}
            <select
              value={adapterId}
              onChange={(e) => setAdapterId(e.target.value)}
            >
              <option value="">{t("Automatic detection")}</option>
              {capabilities
                .filter(
                  (c) => c.id !== "manual-mapping" && c.id !== "private-asset",
                )
                .map((c) => (
                  <option
                    key={c.id}
                    value={c.id}
                    disabled={c.status === "pending sample validation"}
                  >
                    {c.provider} · {c.status}
                  </option>
                ))}
            </select>
          </label>
          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={mappingEnabled}
              onChange={(e) => setMappingEnabled(e.target.checked)}
            />
            {t("Map an unknown structured format")}
          </label>
          {mappingEnabled && (
            <div className="mapping-fields">
              {Object.entries(mapping).map(([key, value]) => (
                <label key={key}>
                  {key}
                  <input
                    value={value}
                    onChange={(e) =>
                      setMapping((old) => ({ ...old, [key]: e.target.value }))
                    }
                  />
                </label>
              ))}
            </div>
          )}
        </details>
      )}
      {importError && (
        <p className="error" role="alert">
          <AlertCircle size={15} />
          {importError}
        </p>
      )}
      {pendingOriginals && !preview && (
        <div className="panel" role="alert">
          <h3>{t("Some files need your permission")}</h3>
          <p>
            {t(
              "Some files cannot be sanitized. Keeping them will store their original contents, which may include sensitive information. This permission applies only to the selected upload.",
            )}
          </p>
          <button
            disabled={busy}
            onClick={() => void upload(pendingOriginals, true)}
          >
            {t("Keep original files and continue")}
          </button>
          <button disabled={busy} onClick={() => setPendingOriginals(null)}>
            {t("Cancel")}
          </button>
        </div>
      )}
      {preview?.retainedOriginals?.length > 0 && (
        <details className="panel retained-originals">
          <summary>
            <span>{t("Original files retained with your permission")}</span>
            <span className="muted">
              {count("files", preview.retainedOriginals.length)}
            </span>
          </summary>
          <p>
            {t(
              "These files were not sanitized and may contain sensitive information.",
            )}
          </p>
          <ul>
            {preview.retainedOriginals.map((name: string, i: number) => (
              <li key={i}>{name}</li>
            ))}
          </ul>
        </details>
      )}
      {!preview ? (
        <>
          <div
            className="dropzone"
            onDragOver={(e) => e.preventDefault()}
            onDrop={(e) => {
              e.preventDefault();
              if (!busy) void dropFiles(e.dataTransfer);
            }}
          >
            {busy ? (
              <ImportActivity
                progress={fileProgress}
                compressedBytes={uploadBytes}
                limitBytes={limits?.maxBytes ?? 0}
              />
            ) : (
              <>
                <Upload size={32} />
                <h3>{t("Drop your conversation exports here")}</h3>
              </>
            )}
            <p>{t("JSON, JSONL, SQLite, CSV, text, ZIP or TAR.GZ")}</p>
            <p>
              {limits
                ? t(
                    "Up to {limit} GB per import, including extracted archive contents.",
                    { limit: limits.maxBytes / 1_000_000_000 },
                  )
                : t("Checking your import limit…")}
            </p>
            <LocalDiscovery onFiles={upload} busy={busy} />
            <div className="row">
              <button
                disabled={busy || !limits}
                className="primary"
                onClick={() => input.current?.click()}
              >
                {t("Choose files")}
              </button>
              <button
                disabled={busy || !limits}
                onClick={() => folder.current?.click()}
              >
                {t("Choose folder")}
              </button>
            </div>
          </div>
          <input
            ref={input}
            type="file"
            multiple
            hidden
            onChange={(e) => e.target.files && upload(e.target.files)}
          />
          <input
            ref={folder}
            type="file"
            multiple
            hidden
            {...({ webkitdirectory: "" } as any)}
            onChange={(e) => e.target.files && upload(e.target.files)}
          />
          <div className="import-formats">
            <Badge provider="ChatGPT" />
            <Badge provider="Claude" />
            <span>{t("Generic JSON / Markdown")}</span>
          </div>
          <p className="muted">
            {t(
              sanitize
                ? "Review sanitized copies and any originals you explicitly allowed. Pattern scanning cannot identify every sensitive value."
                : "Original files are preserved unchanged. Detection uses source content. Cursor SQLite support is experimental; unsupported schemas are reported, never guessed.",
            )}
          </p>
        </>
      ) : progress ? (
        <div className="import-result">
          {busy ? (
            <>
              <ImportCommitActivity
                batch={progress}
                sizes={fileProgress}
                compressedBytes={uploadBytes}
                limitBytes={limits?.maxBytes ?? 0}
              />
              <button
                onClick={() =>
                  act(async () => {
                    await api("/imports/" + preview.batchId + "/cancel", {});
                  })
                }
              >
                {t("Cancel import")}
              </button>
            </>
          ) : (
            <>
              <Check size={36} />
              <h2>{t("Import complete")}</h2>
              {progress.fileProgress?.total > 0 && (
                <FileProgressCount progress={progress.fileProgress} />
              )}
              <ImportSizeMeter
                progress={fileProgress}
                compressedBytes={uploadBytes}
                limitBytes={limits?.maxBytes ?? 0}
              />
              <progress
                aria-label={t("Conversations imported")}
                max={Math.max(progress.total, 1)}
                value={
                  progress.imported +
                  progress.duplicates +
                  (progress.updated || 0)
                }
              />
              <p>
                {t("Import summary", {
                  imported: progress.imported,
                  duplicates: progress.duplicates,
                })}
              </p>
              <p>
                {progress.updated || 0} {t("updated conversations")} ·{" "}
                {progress.conflicts || 0} {t("changes needing review")}
              </p>
              {progress.error && <pre className="error">{progress.error}</pre>}
              <button className="primary" onClick={close}>
                {t("Back to workspace")}
              </button>
            </>
          )}
        </div>
      ) : (
        <>
          <ImportSizeMeter
            progress={fileProgress}
            compressedBytes={uploadBytes}
            limitBytes={limits?.maxBytes ?? 0}
          />
          {preview.skippedFiles?.length > 0 && (
            <details open>
              <summary>
                {t("{count} files skipped", {
                  count: preview.skippedFiles.length,
                })}
              </summary>
              <p>{t("Supported files will still be imported.")}</p>
              <ul>
                {preview.skippedFiles.map(
                  (file: { name: string; reason: string }, i: number) => (
                    <li key={i}>
                      {file.name}: {t(file.reason)}
                    </li>
                  ),
                )}
              </ul>
            </details>
          )}
          <div className="import-project-options">
            <label>
              {t("Application project")}
              <select
                value={projectId}
                disabled={!!newProjectName}
                onChange={(e) => setProjectId(e.target.value)}
              >
                <option value="">{t("No application project")}</option>
                {projects.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              {t("Or create a project")}
              <input
                value={newProjectName}
                maxLength={150}
                onChange={(e) => {
                  setNewProjectName(e.target.value);
                  setProjectId("");
                }}
              />
            </label>
            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={updateExisting}
                onChange={(e) => setUpdateExisting(e.target.checked)}
              />
              {t(
                "Append updates to existing conversations; preserve changed messages for review",
              )}
            </label>
          </div>
          {(preview.attachments?.length ?? 0) > 0 && (
            <details open>
              <summary>
                {t("Supplied attachments")} · {preview.attachments.length}
              </summary>
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={includeAttachments}
                  onChange={(e) => setIncludeAttachments(e.target.checked)}
                />
                {t("Link supplied files to exact source references (private)")}
              </label>
              {preview.attachments.map((a: any, i: number) => (
                <p key={i}>
                  {a.name} · {a.size.toLocaleString()} bytes
                </p>
              ))}
              <p>
                {t(
                  "Unmatched files remain in the original import. No remote files are downloaded.",
                )}
              </p>
            </details>
          )}
          <label className="checkbox-row">
            <ImportSelectionCheckbox
              label={t("Select all")}
              keys={selectableKeys}
              selected={selected}
              disabled={busy}
              onChange={selectKeys}
            />
            {t("Select all")}
          </label>
          <div className="import-review">
            <div>
              {(preview.projects || []).map((p: any) => (
                <label key={p.key} className="checkbox-row">
                  <ImportSelectionCheckbox
                    label={p.name}
                    keys={[p.key]}
                    selected={selected}
                    disabled={busy}
                    onChange={selectKeys}
                  />
                  {p.provider} · {p.name} · {t("Source project metadata")}
                </label>
              ))}
              {Object.entries(
                preview.conversations.reduce(
                  (groups: Record<string, any[]>, c: any) => {
                    const key =
                      c.provider +
                      " / " +
                      (c.sourceProjectName ||
                        c.externalProjectId ||
                        c.sourcePath ||
                        "Ungrouped source");
                    (groups[key] ||= []).push(c);
                    return groups;
                  },
                  {},
                ),
              ).map(([group, items]) => (
                <details key={group} open>
                  <summary>
                    <ImportSelectionCheckbox
                      label={t("Select group") + " " + group}
                      keys={(items as any[]).map((c) => c.key)}
                      selected={selected}
                      disabled={busy}
                      onChange={selectKeys}
                    />
                    {group}
                  </summary>
                  {(items as any[]).map((c: any) => (
                    <div
                      className={
                        "import-item" +
                        ((items as any[]).some(
                          (parent) =>
                            parent.providerId ===
                            c.parentConversationExternalId,
                        )
                          ? " import-child-session"
                          : "")
                      }
                      key={c.key}
                    >
                      <ImportSelectionCheckbox
                        label={c.title}
                        keys={[c.key]}
                        selected={selected}
                        disabled={busy}
                        onChange={selectKeys}
                      />
                      <button onClick={() => setInspect(c)}>
                        <strong>{c.title}</strong>
                        <small>
                          {c.provider} · {count("messages", c.messageCount)}
                          {c.duplicate
                            ? t(" · Already imported")
                            : c.update
                              ? t(" · Update available")
                              : ""}
                          {c.parentConversationExternalId && (
                            <span>
                              {" "}
                              · {t("Child session of")}{" "}
                              {c.parentConversationExternalId}
                            </span>
                          )}
                        </small>
                      </button>
                    </div>
                  ))}
                </details>
              ))}
              {preview.errors.map((e: string, i: number) => (
                <p className="error" key={i}>
                  <AlertCircle size={15} />
                  {e}
                </p>
              ))}
            </div>
            <div className="preview-reader">
              <span className="eyebrow">
                {t(
                  sanitize && !inspect?.originalRetained
                    ? "SANITIZED PREVIEW"
                    : "SOURCE PREVIEW",
                )}
              </span>
              <h3>{inspect?.title}</h3>
              <p>
                {inspect?.adapter?.status} · {inspect?.adapter?.version}
              </p>
              <small>{inspect?.adapter?.limitations}</small>
              {inspect?.diagnostics &&
                JSON.parse(inspect.diagnostics).map(
                  (warning: string, i: number) => (
                    <p className="error" key={i}>
                      {warning}
                    </p>
                  ),
                )}
              {inspect?.messages.map((m: any) => (
                <div key={m.id}>
                  <small>{t(m.role)}</small>
                  <pre>
                    {m.blocks
                      .map(
                        (b: any) =>
                          b.text || (b.kind !== "text" ? b.metadata : ""),
                      )
                      .join("\n")}
                  </pre>
                </div>
              ))}
            </div>
          </div>
          <footer className="modal-footer">
            <span>
              {t("Selected conversations", {
                count: preview.conversations.filter((c: any) =>
                  selected.includes(c.key),
                ).length,
              })}
            </span>
            <button
              disabled={busy}
              onClick={() => {
                setPreview(null);
                setInspect(null);
              }}
            >
              {t("Back")}
            </button>
            <button
              className="primary"
              disabled={!selected.length || busy}
              onClick={commit}
            >
              {busy ? t("Importing…") : t("Import selected")}{" "}
              <ArrowRight size={16} />
            </button>
          </footer>
        </>
      )}
    </Modal>
  );
}

function ImportSelectionCheckbox({
  label,
  keys,
  selected,
  disabled,
  onChange,
}: {
  label: string;
  keys: string[];
  selected: string[];
  disabled: boolean;
  onChange: (keys: string[], checked: boolean) => void;
}) {
  const input = useRef<HTMLInputElement>(null);
  const selectedCount = keys.filter((key) => selected.includes(key)).length;
  const checked = keys.length > 0 && selectedCount === keys.length;
  const mixed = selectedCount > 0 && !checked;
  useEffect(() => {
    if (input.current) input.current.indeterminate = mixed;
  }, [mixed]);
  return (
    <input
      ref={input}
      type="checkbox"
      aria-label={label}
      checked={checked}
      disabled={disabled || keys.length === 0}
      onClick={(e) => e.stopPropagation()}
      onChange={(e) => onChange(keys, e.currentTarget.checked)}
    />
  );
}
