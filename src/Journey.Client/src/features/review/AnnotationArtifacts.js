import { jsx as _jsx, jsxs as _jsxs } from "react/jsx-runtime";
import { Paperclip, Mic, Square, Upload, Clipboard } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { api } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
export function ArtifactList({ artifacts = [] }) {
    return (_jsx("div", { className: "annotation-artifacts", children: artifacts.map((f) => {
            const url = "/api/artifacts/" + f.id;
            return (_jsxs("div", { className: "annotation-artifact", children: [f.contentType.startsWith("image/") && (_jsx("img", { src: url, alt: f.name, loading: "lazy" })), f.contentType.startsWith("audio/") && (_jsx("audio", { src: url, controls: true, preload: "none" })), _jsx("a", { href: url, target: "_blank", rel: "noreferrer", children: f.name }), _jsxs("small", { children: [" \u00B7 ", (f.size / 1024 / 1024).toFixed(2), " MB"] })] }, f.id));
        }) }));
}
export function AnnotationArtifacts({ annotation, canUpload, onUploaded, }) {
    const [open, setOpen] = useState(false);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState("");
    const [recording, setRecording] = useState(false);
    const [requesting, setRequesting] = useState(false);
    const [seconds, setSeconds] = useState(0);
    const [draft, setDraft] = useState(null);
    const [preview, setPreview] = useState("");
    const input = useRef(null);
    const recorder = useRef(null);
    const stream = useRef(null);
    const alive = useRef(true);
    const uploading = useRef(false);
    useEffect(() => {
        alive.current = true;
        return () => {
            alive.current = false;
            if (recorder.current?.state === "recording")
                recorder.current.stop();
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
        if (!recording)
            return;
        const timer = setInterval(() => setSeconds((s) => s + 1), 1000);
        return () => clearInterval(timer);
    }, [recording]);
    const upload = async (files) => {
        if (uploading.current || recording || requesting)
            return;
        uploading.current = true;
        setBusy(true);
        setError("");
        try {
            for (const file of files) {
                if (!file.size || file.size > 50 * 1024 * 1024)
                    throw new Error(t("Each artifact must be nonempty and at most 50 MB."));
                const body = new FormData();
                body.append("file", file);
                onUploaded(await api("/annotations/" + annotation.id + "/artifacts", body));
                if (file === draft)
                    setDraft(null);
            }
        }
        catch (err) {
            setError(err.message);
        }
        finally {
            uploading.current = false;
            setBusy(false);
        }
    };
    const startRecording = async () => {
        setError("");
        setRequesting(true);
        try {
            if (!navigator.mediaDevices?.getUserMedia ||
                typeof MediaRecorder === "undefined")
                throw new Error(t("Audio recording is unavailable in this browser. You can attach an audio file instead."));
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
                throw new Error(t("No supported recording format. Attach an audio file instead."));
            const rec = new MediaRecorder(media, { mimeType: mime });
            recorder.current = rec;
            const chunks = [];
            let size = 0;
            let exceeded = false;
            rec.ondataavailable = (event) => {
                size += event.data.size;
                if (size > 50 * 1024 * 1024) {
                    exceeded = true;
                    if (rec.state === "recording")
                        rec.stop();
                }
                else
                    chunks.push(event.data);
            };
            rec.onstop = () => {
                media.getTracks().forEach((track) => track.stop());
                if (!alive.current)
                    return;
                setRecording(false);
                if (exceeded) {
                    setError(t("Recording exceeded 50 MB. Please record a shorter clip."));
                    return;
                }
                const ext = mime.includes("mp4")
                    ? "m4a"
                    : mime.includes("ogg")
                        ? "ogg"
                        : "webm";
                setDraft(new File(chunks, "recording-" + Date.now() + "." + ext, {
                    type: mime,
                }));
            };
            rec.onerror = () => {
                setError(t("Recording failed. Please try again."));
                media.getTracks().forEach((track) => track.stop());
                setRecording(false);
            };
            setSeconds(0);
            setRecording(true);
            rec.start(1000);
        }
        catch (err) {
            stream.current?.getTracks().forEach((track) => track.stop());
            setError(err.message);
        }
        finally {
            if (alive.current)
                setRequesting(false);
        }
    };
    return (_jsxs("div", { children: [_jsx(ArtifactList, { artifacts: annotation.artifacts }), canUpload && (_jsx("button", { type: "button", className: "attachment-toggle", title: t("Attach artifact"), "aria-label": t("Attach artifact"), "aria-expanded": open, disabled: recording || requesting || busy, onClick: () => setOpen((v) => !v), children: _jsx(Paperclip, { size: 17 }) })), canUpload && open && (_jsxs("div", { className: "attachment-composer", onPaste: (e) => {
                    const files = Array.from(e.clipboardData.files);
                    if (files.length) {
                        e.preventDefault();
                        void upload(files);
                    }
                }, children: [_jsxs("div", { className: "artifact-dropzone", tabIndex: 0, onDragOver: (e) => e.preventDefault(), onDrop: (e) => {
                            e.preventDefault();
                            void upload(Array.from(e.dataTransfer.files));
                        }, children: [t("Drop files here or paste an image (Ctrl/Cmd+V)"), _jsx("small", { children: t("Images, audio or ZIP · 50 MB per file") })] }), _jsx("input", { ref: input, hidden: true, type: "file", multiple: true, "aria-label": t("Choose artifact files"), accept: ".png,.jpg,.jpeg,.gif,.webp,.mp3,.wav,.ogg,.flac,.m4a,.webm,.zip", onChange: (e) => {
                            void upload(Array.from(e.target.files || []));
                            e.target.value = "";
                        } }), _jsxs("div", { className: "attachment-actions", children: [_jsxs("button", { type: "button", disabled: busy || recording || requesting, onClick: () => input.current?.click(), children: [_jsx(Upload, { size: 14 }), t("Choose files")] }), _jsxs("button", { type: "button", disabled: busy || recording || requesting, onClick: async () => {
                                    setError("");
                                    try {
                                        if (!navigator.clipboard?.read)
                                            throw new Error(t("Use Ctrl/Cmd+V in the drop area to paste an image."));
                                        const items = await navigator.clipboard.read();
                                        const files = [];
                                        for (const item of items) {
                                            const type = item.types.find((type) => [
                                                "image/png",
                                                "image/jpeg",
                                                "image/webp",
                                                "image/gif",
                                            ].includes(type));
                                            if (type)
                                                files.push(new File([await item.getType(type)], "clipboard-" + Date.now() + "." + type.split("/")[1], { type }));
                                        }
                                        if (!files.length)
                                            throw new Error(t("No image found on the clipboard."));
                                        await upload(files);
                                    }
                                    catch (err) {
                                        setError(err.message);
                                    }
                                }, children: [_jsx(Clipboard, { size: 14 }), t("Paste image")] }), !recording ? (_jsxs("button", { type: "button", disabled: busy || requesting || !!draft, onClick: startRecording, children: [_jsx(Mic, { size: 14 }), requesting ? t("Waiting for microphone…") : t("Record audio")] })) : (_jsxs("button", { type: "button", onClick: () => recorder.current?.stop(), children: [_jsx(Square, { size: 14 }), t("Stop recording"), " (", seconds, "s)"] }))] }), recording && (_jsxs("p", { role: "status", children: [t("Recording audio…"), " ", seconds, "s"] })), draft && (_jsxs("div", { className: "recording-preview", children: [_jsx("audio", { controls: true, src: preview }), _jsx("button", { type: "button", disabled: busy, onClick: () => void upload([draft]), children: t("Attach recording") }), _jsx("button", { type: "button", disabled: busy, onClick: () => setDraft(null), children: t("Discard recording") })] })), busy && _jsx("p", { role: "status", children: t("Uploading artifact…") }), error && (_jsx("p", { role: "alert", className: "error", children: error }))] }))] }));
}
