import { useEffect, useState } from "react";
import { LoaderCircle } from "lucide-react";
import { t, useLocale, formatNumber } from "../../shared/i18n/i18n";
import "./import-activity.css";

export type FileProgress = {
  stage: string;
  processed: number;
  total: number;
  skipped: number;
  compressedBytes?: number;
  uncompressedBytes?: number;
  usedBytes?: number;
  limitBytes?: number;
  elapsedMilliseconds?: number;
  timings?: Record<
    string,
    { milliseconds: number; operations: number; active: number }
  >;
  metrics?: Record<string, number>;
};

const timingLabels: Record<string, string> = {
  uploadReceipt: "Receiving upload",
  uploadStaging: "Staging upload",
  queueWait: "Waiting for worker",
  archiveScan: "Inspecting archive",
  extraction: "Extracting files",
  sanitization: "Sanitizing content",
  sourceStorage: "Saving source files",
  localRead: "Reading local files",
  sourceRead: "Downloading source files",
  detection: "Detecting formats",
  parsing: "Parsing conversations",
  duplicateLookup: "Checking duplicates",
  previewPersistence: "Saving preview",
  sourceMetadata: "Loading source metadata",
  persistence: "Saving conversations",
  recovery: "Linking files and tools",
};

export function ImportTimings({
  progress,
}: {
  progress?: FileProgress | null;
}) {
  useLocale();
  const timings = Object.entries(progress?.timings || {});
  if (!timings.length) return null;
  return (
    <details className="import-timings panel">
      <summary>{t("Processing timings")}</summary>
      <p>
        {t("Server elapsed: {seconds} seconds", {
          seconds: formatNumber(
            Math.round((progress?.elapsedMilliseconds || 0) / 100) / 10,
          ),
        })}
      </p>
      <dl>
        {timings
          .sort((a, b) => b[1].milliseconds - a[1].milliseconds)
          .map(([stage, timing]) => (
            <div className="row" key={stage}>
              <dt>{t(timingLabels[stage] || stage)}</dt>
              <dd>
                {t("{seconds} seconds · {count} operations", {
                  seconds: formatNumber(
                    Math.round(timing.milliseconds / 100) / 10,
                  ),
                  count: timing.operations + (timing.active || 0),
                })}
              </dd>
            </div>
          ))}
      </dl>
      {progress?.metrics?.duplicateQueries !== undefined && (
        <p>
          {t("Duplicate lookup queries: {count}", {
            count: progress.metrics.duplicateQueries,
          })}
        </p>
      )}
    </details>
  );
}

export function formatByteSize(bytes: number) {
  const units: [number, string][] = [
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

export function ImportSizeMeter({
  progress,
  compressedBytes = 0,
  limitBytes = 0,
}: {
  progress?: FileProgress | null;
  compressedBytes?: number;
  limitBytes?: number;
}) {
  const limit = progress?.limitBytes || limitBytes;
  const compressed = progress?.compressedBytes || compressedBytes;
  const serverSized = (progress?.limitBytes ?? 0) > 0;
  if (!limit || (compressed <= 0 && !serverSized)) return null;
  const used = serverSized ? (progress?.usedBytes ?? 0) : compressed;
  return (
    <div className="import-sizes">
      <p>{t("Compressed: {size}", { size: formatByteSize(compressed) })}</p>
      {serverSized ? (
        <p>
          {t("Uncompressed: {size}", {
            size: formatByteSize(progress?.uncompressedBytes ?? 0),
          })}
        </p>
      ) : (
        <p>{t("Calculating uncompressed size…")}</p>
      )}
      <p>
        {t("{used} of {limit} toward your import limit", {
          used: formatByteSize(used),
          limit: formatByteSize(limit),
        })}
      </p>
      <progress
        aria-label={t("Import size")}
        max={limit}
        value={Math.min(Math.max(used, 0), limit)}
      />
    </div>
  );
}

export function FileProgressCount({ progress }: { progress: FileProgress }) {
  return (
    <div>
      <p>{t("{processed} of {total} files processed", {
        processed: progress.processed, total: progress.total,
      })}</p>
      <progress
        aria-label={t("Files processed")}
        max={Math.max(progress.total, 1)}
        value={progress.processed}
      />
      {progress.skipped > 0 && (
        <p>{t("{count} files skipped", { count: progress.skipped })}</p>
      )}
    </div>
  );
}

function useElapsed() {
  const [seconds, setSeconds] = useState(0);
  useEffect(() => {
    const started = Date.now();
    const timer = window.setInterval(
      () => setSeconds(Math.floor((Date.now() - started) / 1000)),
      1000,
    );
    return () => window.clearInterval(timer);
  }, []);
  return seconds;
}

export function ImportActivity({
  progress,
  compressedBytes = 0,
  limitBytes = 0,
}: {
  progress?: FileProgress | null;
  compressedBytes?: number;
  limitBytes?: number;
}) {
  useLocale();
  const seconds = useElapsed();
  return (
    <div className="import-activity" role="status">
      <LoaderCircle
        className="import-activity-spinner"
        size={36}
        aria-hidden="true"
      />
      <h3>{t("Reading your sources…")}</h3>
      {progress?.stage === "counting" ? (
        <p>{t("Counting files in your archives…")}</p>
      ) : progress?.stage === "queued" ? (
        <p>{t("Waiting for an available import worker…")}</p>
      ) : progress && progress.total > 0 ? (
        <FileProgressCount progress={progress} />
      ) : (
        <p>{t("Uploading files…")}</p>
      )}
      <ImportSizeMeter
        progress={progress}
        compressedBytes={compressedBytes}
        limitBytes={limitBytes}
      />
      <p>
        {t(
          "Uploading, extracting and reading your files. Large archives can take several minutes. Keep this window open.",
        )}
      </p>
      <p className="muted" aria-live="off">
        {t("Elapsed: {seconds} seconds", { seconds })}
      </p>
    </div>
  );
}

export function ImportCommitActivity({
  batch,
  sizes,
  compressedBytes = 0,
  limitBytes = 0,
}: {
  batch: {
    total?: number;
    imported?: number;
    duplicates?: number;
    updated?: number;
    fileProgress?: FileProgress | null;
  };
  sizes?: FileProgress | null;
  compressedBytes?: number;
  limitBytes?: number;
}) {
  useLocale();
  const seconds = useElapsed();
  const done =
    (batch.imported || 0) + (batch.duplicates || 0) + (batch.updated || 0);
  const total = batch.total || 0;
  const recovering = batch.fileProgress?.stage === "recovering";
  return (
    <div className="import-activity" role="status">
      <LoaderCircle
        className="import-activity-spinner"
        size={36}
        aria-hidden="true"
      />
      <h3>
        {t(recovering ? "Finishing import…" : "Importing conversations…")}
      </h3>
      <ImportSizeMeter
        progress={sizes}
        compressedBytes={compressedBytes}
        limitBytes={limitBytes}
      />
      <p>{t("{done} of {total} conversations", { done, total })}</p>
      <progress
        aria-label={t("Conversations imported")}
        max={Math.max(total, 1)}
        value={done}
      />
      {batch.fileProgress && batch.fileProgress.total > 0 && (
        <FileProgressCount progress={batch.fileProgress} />
      )}
      <p>
        {t(
          recovering
            ? "Linking supplied files and recorded tool results."
            : "Importing your selected conversations. Keep this window open.",
        )}
      </p>
      <p className="muted" aria-live="off">
        {t("Elapsed: {seconds} seconds", { seconds })}
      </p>
    </div>
  );
}
