import { useEffect, useState } from "react";
import { api, act, useApp } from "../../shared/state";
import { t, useLocale } from "../../shared/i18n/i18n";

export type ReplacementRule = { find: string; replace: string };

export function ReplacementRules({
  rules,
  onChange,
  disabled = false,
}: {
  rules: ReplacementRule[];
  onChange: (rules: ReplacementRule[]) => void;
  disabled?: boolean;
}) {
  return (
    <fieldset disabled={disabled}>
      <legend>{t("Automatic replacements")}</legend>
      <p>
        {t(
          "Exact, case-sensitive text. Longer phrases match first; replacements are applied once.",
        )}
      </p>
      {rules.map((rule, index) => (
        <div className="row" key={index}>
          <label>
            {t("Find text")}
            <input
              value={rule.find}
              maxLength={1000}
              onChange={(e) =>
                onChange(
                  rules.map((r, i) =>
                    i === index ? { ...r, find: e.target.value } : r,
                  ),
                )
              }
            />
          </label>
          <label>
            {t("Replace with")}
            <input
              value={rule.replace}
              maxLength={1000}
              onChange={(e) =>
                onChange(
                  rules.map((r, i) =>
                    i === index ? { ...r, replace: e.target.value } : r,
                  ),
                )
              }
            />
          </label>
          <button
            type="button"
            aria-label={t("Remove replacement") + " " + (index + 1)}
            onClick={() => onChange(rules.filter((_, i) => i !== index))}
          >
            {t("Remove")}
          </button>
        </div>
      ))}
      <button
        type="button"
        disabled={rules.length >= 100}
        onClick={() =>
          onChange([...rules, { find: "", replace: t("[REDACTED]") }])
        }
      >
        {t("Add replacement")}
      </button>
    </fieldset>
  );
}

export function SanitizationSettings() {
  useLocale();
  const [rules, setRules] = useState<ReplacementRule[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [saving, setSaving] = useState(false);
  useEffect(() => {
    act(async () => {
      setRules(await api("/me/sanitization"));
      setLoaded(true);
    });
  }, []);
  return (
    <div className="panel settings">
      <h3>{t("Sanitize sensitive information")}</h3>
      <p>
        {t(
          "Your account's saved rules are loaded for publishing and optional import sanitization.",
        )}
      </p>
      <ReplacementRules
        rules={rules}
        onChange={setRules}
        disabled={!loaded || saving}
      />
      <button
        disabled={!loaded || saving || rules.some((r) => !r.find.trim())}
        onClick={() =>
          act(async () => {
            setSaving(true);
            try {
              await api("/me/sanitization", rules, "PUT");
              useApp.getState().notify(t("Replacement rules saved"));
            } finally {
              setSaving(false);
            }
          })
        }
      >
        {saving ? t("Saving…") : t("Save replacement rules")}
      </button>
    </div>
  );
}
