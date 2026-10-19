import { useEffect, useState } from "react";
import { api, act } from "../../shared/state";
import { t } from "../../shared/i18n/i18n";
export function LocalDiscovery({
  onFiles,
  busy,
}: {
  onFiles: (files: File[]) => Promise<void>;
  busy: boolean;
}) {
  const [settings, setSettings] = useState<any>(null),
    [roots, setRoots] = useState<string[]>([]),
    [exclude, setExclude] = useState("");
  const [consent, setConsent] = useState(false),
    [files, setFiles] = useState<any[]>([]),
    [chosen, setChosen] = useState<string[]>([]),
    [loading, setLoading] = useState(false);
  useEffect(() => {
    act(async () => setSettings(await api("/imports/discovery")));
  }, []);
  return (
    <details>
      <summary>{t("Find local tool histories (optional)")}</summary>
      <p>
        {t(settings?.explanation || "Checking local discovery availability…")}
      </p>
      {settings?.enabled && (
        <>
          <p>
            {t(
              "Choose directories to inspect. Scanning lists transcript filenames and sizes; selected files are read only when you import them.",
            )}
          </p>
          {settings.roots.map((r: any) => (
            <label key={r.id}>
              <input
                type="checkbox"
                checked={roots.includes(r.id)}
                onChange={(e) =>
                  setRoots(
                    e.target.checked
                      ? [...roots, r.id]
                      : roots.filter((id) => id !== r.id),
                  )
                }
              />
              {r.provider} · {r.path}
            </label>
          ))}
          <label>
            {t("Exclude relative directories (one per line)")}
            <textarea
              value={exclude}
              onChange={(e) => setExclude(e.target.value)}
            />
          </label>
          <label>
            <input
              type="checkbox"
              checked={consent}
              onChange={(e) => setConsent(e.target.checked)}
            />
            {t(
              "Allow read-only inspection of the selected transcript directories on this computer.",
            )}
          </label>
          <button
            disabled={!consent || !roots.length || loading || busy}
            onClick={() =>
              act(async () => {
                setLoading(true);
                try {
                  setFiles(
                    await api("/imports/discovery/scan", {
                      roots,
                      excludeDirectories: exclude
                        .split("\n")
                        .map((s) => s.trim())
                        .filter(Boolean),
                      consent,
                    }),
                  );
                  setChosen([]);
                } finally {
                  setLoading(false);
                }
              })
            }
          >
            {t("Scan selected directories")}
          </button>
          {files.map((f) => (
            <label key={f.id}>
              <input
                type="checkbox"
                checked={chosen.includes(f.id)}
                onChange={(e) =>
                  setChosen(
                    e.target.checked
                      ? [...chosen, f.id]
                      : chosen.filter((id) => id !== f.id),
                  )
                }
              />
              {f.relativePath} · {f.size.toLocaleString()} bytes
            </label>
          ))}
          {files.length > 0 && (
            <button
              disabled={!chosen.length || loading || busy}
              onClick={() =>
                act(async () => {
                  setLoading(true);
                  try {
                    const selected: File[] = [];
                    for (const f of files.filter((f) =>
                      chosen.includes(f.id),
                    )) {
                      const response = await fetch(
                        `/api/imports/discovery/files/${f.id}`,
                      );
                      if (!response.ok)
                        throw new Error(
                          "Source changed or discovery expired; scan again.",
                        );
                      selected.push(
                        new File([await response.blob()], f.relativePath),
                      );
                    }
                    await onFiles(selected);
                  } finally {
                    setLoading(false);
                  }
                })
              }
            >
              {t("Preview selected histories")}
            </button>
          )}
        </>
      )}
    </details>
  );
}
