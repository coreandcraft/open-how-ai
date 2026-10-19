import { useEffect, useState } from "react";
import { api, act } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
import { SuppliedFile } from "./SuppliedFile";

export function RecoveredSource({ id }: { id: string }) {
  const [data, setData] = useState<any>(null);
  const [versions, setVersions] = useState<any[]>([]);
  useEffect(() => {
    act(async () => {
      const [source, history] = await Promise.all([
        api(`/conversations/${id}/recovered-source`),
        api(`/conversations/${id}/versions`),
      ]);
      setData(source);
      setVersions(history);
    });
  }, [id]);
  if (!data) return null;
  return (
    <details className="source-history">
      <summary>{t("Recovered files, tools & revisions")}</summary>
      <p>{t("Source evidence · private workspace content")}</p>
      {data.attachments.length === 0 && (
        <p>{t("No attachment references recorded.")}</p>
      )}
      {data.attachments.map((a: any) => (
        <SuppliedFile key={a.id} file={a} />
      ))}
      {data.executions.map((e: any) => (
        <details key={e.id}>
          <summary>
            {e.toolName || e.externalCallId} · {e.status}
          </summary>
          <p>
            {t("Explicit source call ID")}: {e.externalCallId}
          </p>
          <pre>{e.arguments}</pre>
          <pre>{e.result}</pre>
        </details>
      ))}
      {versions.map((v: any) => (
        <details key={v.id}>
          <summary>
            {t("Previous source version")} ·{" "}
            {new Date(v.createdAt).toLocaleString()}
          </summary>
          <pre>
            {JSON.parse(v.payload)
              .blocks.map((b: any) => b.text)
              .join("\n")}
          </pre>
        </details>
      ))}
    </details>
  );
}
