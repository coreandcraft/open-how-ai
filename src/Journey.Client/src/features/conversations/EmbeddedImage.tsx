import { FileJson } from "lucide-react";
import { useState } from "react";
import { t, useLocale } from "../../shared/i18n/i18n";
import { SourceDisclosure } from "./SourceActivity";

export function EmbeddedImage({ src, alt }: { src: string; alt?: string }) {
  useLocale();
  const [failed, setFailed] = useState(false);
  return failed ? (
    <span className="attachment">{t("Image preview unavailable.")}</span>
  ) : (
    <img
      className="embedded-image-preview"
      src={src}
      alt={alt || t("Image")}
      loading="lazy"
      decoding="async"
      onError={() => setFailed(true)}
    />
  );
}

export function ImageSource({
  payload,
  sourceRef = () => {},
}: {
  payload: string;
  sourceRef?: (element: HTMLElement | null) => void;
}) {
  useLocale();
  return (
    <div className="embedded-image-source">
      <SourceDisclosure
        icon={FileJson}
        title={t("View source block")}
        sourceLabel={t("Original source payload")}
        payload={payload}
        sourceRef={sourceRef}
        plain
      />
    </div>
  );
}
