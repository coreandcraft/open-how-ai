import { useEffect, useState } from "react";
import { t } from "../../shared/i18n/i18n";

export type SuppliedFileRecord = {
  id: string;
  name: string;
  availability: string;
  blockId?: string;
  view?: string;
};

function leaf(path: string) {
  const parts = path.split(/[\\/]/).filter(Boolean);
  return parts[parts.length - 1] || path;
}

export function SuppliedFile({ file }: { file: SuppliedFileRecord }) {
  const name = leaf(file.name || "");
  const view = file.view || "download";
  const url = `/api/imported-attachments/${file.id}/content`;
  if (file.availability !== "available")
    return (
      <p className="supplied-file-missing">
        {name} · {file.availability}
      </p>
    );
  return (
    <figure className="supplied-file">
      <figcaption>{name}</figcaption>
      {view === "image" && <img src={url} alt={name} />}
      {view === "pdf" && <iframe src={url} title={name} />}
      {view === "video" && <video src={url} controls preload="metadata" />}
      {view === "audio" && <audio src={url} controls preload="metadata" />}
      {(view === "text" || view === "document") && <DocumentText id={file.id} />}
      <a href={`${url}?download=1`}>{t("Download supplied file")}</a>
    </figure>
  );
}

function DocumentText({ id }: { id: string }) {
  const [text, setText] = useState<string | null>(null);
  const [missing, setMissing] = useState(false);
  useEffect(() => {
    let live = true;
    fetch(`/api/imported-attachments/${id}/text`)
      .then((response) => {
        if (!response.ok) throw new Error();
        return response.text();
      })
      .then((body) => {
        if (live) setText(body);
      })
      .catch(() => {
        if (live) setMissing(true);
      });
    return () => {
      live = false;
    };
  }, [id]);
  if (missing)
    return (
      <p>
        {t(
          "Preview is not available for this document. Download the original file.",
        )}
      </p>
    );
  if (text == null) return null;
  return <pre className="supplied-document">{text}</pre>;
}
