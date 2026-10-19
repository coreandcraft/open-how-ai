import { t, useLocale } from "../i18n/i18n";
import React from "react";
import { X } from "lucide-react";
import { providerClass } from "../state";
export function Modal({
  title,
  onClose,
  children,
  wide = false,
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
  wide?: boolean;
}) {
  useLocale();
  return (
    <div
      className="overlay"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <section
        className={"modal " + (wide ? "wide" : "")}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <header>
          <h2>{title}</h2>
          <button aria-label={t("Close dialog")} onClick={onClose}>
            <X size={18} />
          </button>
        </header>
        {children}
      </section>
    </div>
  );
}
export function Badge({ provider }: { provider: string }) {
  useLocale();
  return (
    <span className={"provider " + providerClass(provider)}>
      {provider === "ChatGPT" ? "◉" : provider === "Claude" ? "✳" : "⌘"}{" "}
      <span>{provider}</span>
    </span>
  );
}
