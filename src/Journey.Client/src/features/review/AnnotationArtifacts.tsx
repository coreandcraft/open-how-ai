import { Paperclip, Mic, Square, Upload, Clipboard } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { api } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
export function ArtifactList({ artifacts = [] }: { artifacts?: any[] }) {
  return (
    <div className="annotation-artifacts">
      {artifacts.map((f) => {
        const url = "/api/artifacts/" + f.id;
        return (
          <div key={f.id} className="annotation-artifact">
            {f.contentType.startsWith("image/") && (
              <img src={url} alt={f.name} loading="lazy" />
            )}
            {f.contentType.startsWith("audio/") && (
              <audio src={url} controls preload="none" />
            )}
            <a href={url} target="_blank" rel="noreferrer">
              {f.name}
            </a>
            <small> · {(f.size / 1024 / 1024).toFixed(2)} MB</small>
          </div>
        );
      })}
    </div>
  );
}
export function AnnotationArtifacts({
  annotation,
  canUpload,
  onUploaded,
}: {
  annotation: any;
  canUpload: boolean;
  onUploaded: (file: any) => void;
}) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [recording, setRecording] = useState(false);
  const [requesting, setRequesting] = useState(false);
  const [seconds, setSeconds] = useState(0);
  const [draft, setDraft] = useState<File | null>(null);
  const [preview, setPreview] = useState("");
  const input = useRef<HTMLInputElement>(null);
  const recorder = useRef<MediaRecorder | null>(null);
  const stream = useRef<MediaStream | null>(null);
  const alive = useRef(true);
  const uploading = useRef(false);
  useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
      if (recorder.current?.state === "recording") recorder.current.stop();
      stream.current?.getTracks().forEach((track) => track.stop());
    };
  }, []);
  useEffect(() => {
    if (!draft) {
      setPreview("");
      return;
    }
    const url = URL.createObjectURL(draft);
    setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [draft]);
  useEffect(() => {
    if (!recording) return;
    const timer = setInterval(() => setSeconds((s) => s + 1), 1000);
    return () => clearInterval(timer);
  }, [recording]);
  const upload = async (files: File[]) => {
    if (uploading.current || recording || requesting) return;
    uploading.current = true;
    setBusy(true);
    setError("");
    try {
      for (const file of files) {
        if (!file.size || file.size > 50 * 1024 * 1024)
          throw new Error(
            t("Each artifact must be nonempty and at most 50 MB."),
          );
        const body = new FormData();
        body.append("file", file);
        onUploaded(
          await api("/annotations/" + annotation.id + "/artifacts", body),
        );
        if (file === draft) setDraft(null);
      }
    } catch (err) {
      setError((err as Error).message);
    } finally {
      uploading.current = false;
      setBusy(false);
    }
  };
  const startRecording = async () => {
    setError("");
    setRequesting(true);
    try {
      if (
        !navigator.mediaDevices?.getUserMedia ||
        typeof MediaRecorder === "undefined"
      )
        throw new Error(
          t(
            "Audio recording is unavailable in this browser. You can attach an audio file instead.",
          ),
        );
      const media = await navigator.mediaDevices.getUserMedia({ audio: true });
      if (!alive.current) {
        media.getTracks().forEach((track) => track.stop());
        return;
      }
      stream.current = media;
      const mime = [
        "audio/webm;codecs=opus",
        "audio/ogg;codecs=opus",
        "audio/mp4",
      ].find((type) => MediaRecorder.isTypeSupported(type));
      if (!mime)
        throw new Error(
          t("No supported recording format. Attach an audio file instead."),
        );
      const rec = new MediaRecorder(media, { mimeType: mime });
      recorder.current = rec;
      const chunks: Blob[] = [];
      let size = 0;
      let exceeded = false;
      rec.ondataavailable = (event) => {
        size += event.data.size;
        if (size > 50 * 1024 * 1024) {
          exceeded = true;
          if (rec.state === "recording") rec.stop();
        } else chunks.push(event.data);
      };
      rec.onstop = () => {
        media.getTracks().forEach((track) => track.stop());
        if (!alive.current) return;
        setRecording(false);
        if (exceeded) {
          setError(
            t("Recording exceeded 50 MB. Please record a shorter clip."),
          );
          return;
        }
        const ext = mime.includes("mp4")
          ? "m4a"
          : mime.includes("ogg")
            ? "ogg"
            : "webm";
        setDraft(
          new File(chunks, "recording-" + Date.now() + "." + ext, {
            type: mime,
          }),
        );
      };
      rec.onerror = () => {
        setError(t("Recording failed. Please try again."));
        media.getTracks().forEach((track) => track.stop());
        setRecording(false);
      };
      setSeconds(0);
      setRecording(true);
      rec.start(1000);
    } catch (err) {
      stream.current?.getTracks().forEach((track) => track.stop());
      setError((err as Error).message);
    } finally {
      if (alive.current) setRequesting(false);
    }
  };
  return (
    <div>
      <ArtifactList artifacts={annotation.artifacts} />
      {canUpload && (
        <button
          type="button"
          className="attachment-toggle"
          title={t("Attach artifact")}
          aria-label={t("Attach artifact")}
          aria-expanded={open}
          disabled={recording || requesting || busy}
          onClick={() => setOpen((v) => !v)}
        >
          <Paperclip size={17} />
        </button>
      )}
      {canUpload && open && (
        <div
          className="attachment-composer"
          onPaste={(e) => {
            const files = Array.from(e.clipboardData.files);
            if (files.length) {
              e.preventDefault();
              void upload(files);
            }
          }}
        >
          <div
            className="artifact-dropzone"
            tabIndex={0}
            onDragOver={(e) => e.preventDefault()}
            onDrop={(e) => {
              e.preventDefault();
              void upload(Array.from(e.dataTransfer.files));
            }}
          >
            {t("Drop files here or paste an image (Ctrl/Cmd+V)")}
            <small>{t("Images, audio or ZIP · 50 MB per file")}</small>
          </div>
          <input
            ref={input}
            hidden
            type="file"
            multiple
            aria-label={t("Choose artifact files")}
            accept=".png,.jpg,.jpeg,.gif,.webp,.mp3,.wav,.ogg,.flac,.m4a,.webm,.zip"
            onChange={(e) => {
              void upload(Array.from(e.target.files || []));
              e.target.value = "";
            }}
          />
          <div className="attachment-actions">
            <button
              type="button"
              disabled={busy || recording || requesting}
              onClick={() => input.current?.click()}
            >
              <Upload size={14} />
              {t("Choose files")}
            </button>
            <button
              type="button"
              disabled={busy || recording || requesting}
              onClick={async () => {
                setError("");
                try {
                  if (!navigator.clipboard?.read)
                    throw new Error(
                      t("Use Ctrl/Cmd+V in the drop area to paste an image."),
                    );
                  const items = await navigator.clipboard.read();
                  const files: File[] = [];
                  for (const item of items) {
                    const type = item.types.find((type) =>
                      [
                        "image/png",
                        "image/jpeg",
                        "image/webp",
                        "image/gif",
                      ].includes(type),
                    );
                    if (type)
                      files.push(
                        new File(
                          [await item.getType(type)],
                          "clipboard-" + Date.now() + "." + type.split("/")[1],
                          { type },
                        ),
                      );
                  }
                  if (!files.length)
                    throw new Error(t("No image found on the clipboard."));
                  await upload(files);
                } catch (err) {
                  setError((err as Error).message);
                }
              }}
            >
              <Clipboard size={14} />
              {t("Paste image")}
            </button>
            {!recording ? (
              <button
                type="button"
                disabled={busy || requesting || !!draft}
                onClick={startRecording}
              >
                <Mic size={14} />
                {requesting ? t("Waiting for microphone…") : t("Record audio")}
              </button>
            ) : (
              <button type="button" onClick={() => recorder.current?.stop()}>
                <Square size={14} />
                {t("Stop recording")} ({seconds}s)
              </button>
            )}
          </div>
          {recording && (
            <p role="status">
              {t("Recording audio…")} {seconds}s
            </p>
          )}
          {draft && (
            <div className="recording-preview">
              <audio controls src={preview} />
              <button
                type="button"
                disabled={busy}
                onClick={() => void upload([draft])}
              >
                {t("Attach recording")}
              </button>
              <button
                type="button"
                disabled={busy}
                onClick={() => setDraft(null)}
              >
                {t("Discard recording")}
              </button>
            </div>
          )}
          {busy && <p role="status">{t("Uploading artifact…")}</p>}
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
        </div>
      )}
    </div>
  );
}
