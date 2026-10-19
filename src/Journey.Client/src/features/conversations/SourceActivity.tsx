import {
  ChevronRight,
  CircleCheck,
  FileJson,
  Sparkles,
  type LucideIcon,
} from "lucide-react";
import type { ReactNode } from "react";
import { t, useLocale } from "../../shared/i18n/i18n";
import {
  describeActivity,
  sourceText,
  type SourceBlock,
} from "./messagePresentation";
import { SourcePayload } from "./JsonPayload";

export function SourceDisclosure({
  icon: Icon,
  title,
  detail,
  detailTitle,
  detailFallback,
  path,
  sourceLabel,
  payload,
  sourceRef,
  plain = false,
  activity,
  mark,
  summaryExtra,
  footer,
}: {
  icon: LucideIcon;
  title: string;
  detail?: string;
  detailTitle?: string;
  detailFallback?: string;
  path?: string;
  sourceLabel: string;
  payload: string;
  sourceRef: (element: HTMLElement | null) => void;
  plain?: boolean;
  activity?: string;
  mark?: string;
  summaryExtra?: ReactNode;
  footer?: ReactNode;
}) {
  return (
    <div className="tool-call-entry">
      <details className="tool-call" data-activity={activity}>
        <summary>
          <Icon className="tool-call-icon" size={16} aria-hidden="true" />
          <span className="tool-call-label">
            <strong>{title}</strong>
            {mark && <span className="activity-mark">{mark}</span>}
            {detail ? (
              <span className="tool-call-target" title={detailTitle || detail}>
                {detail}
              </span>
            ) : (
              <span className="tool-call-hint">{detailFallback}</span>
            )}
          </span>
          {summaryExtra}
          <ChevronRight
            className="tool-call-chevron"
            size={15}
            aria-hidden="true"
          />
        </summary>
        <div className="tool-call-source">
          {path && <div className="tool-call-path">{path}</div>}
          <span className="tool-call-source-label">{sourceLabel}</span>
          <SourcePayload text={payload} sourceRef={sourceRef} plain={plain} />
        </div>
      </details>
      {footer}
    </div>
  );
}

export function SourceActivity({
  block,
  sourceRef,
  plain = false,
}: {
  block: SourceBlock;
  sourceRef: (element: HTMLElement | null) => void;
  plain?: boolean;
}) {
  useLocale();
  const activity = describeActivity(block);
  const Icon =
    activity.tone === "progress"
      ? Sparkles
      : activity.tone === "result"
        ? CircleCheck
        : FileJson;
  return (
    <SourceDisclosure
      icon={Icon}
      title={t(activity.label)}
      detail={activity.preview}
      detailFallback={t("Preserved source block")}
      sourceLabel={t("Original source payload")}
      payload={sourceText(block)}
      sourceRef={sourceRef}
      plain={plain}
      activity={activity.tone}
      mark={
        activity.tone === "progress"
          ? t("Progress")
          : activity.tone === "result"
            ? t("Result")
            : undefined
      }
    />
  );
}
