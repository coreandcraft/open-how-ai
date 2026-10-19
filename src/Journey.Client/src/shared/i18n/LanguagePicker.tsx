import { languages, setLocale, t, useLocale } from "./i18n";

export function LanguagePicker() {
  const locale = useLocale();
  return (
    <label className="language-picker">
      <select
        aria-label={t("Language")}
        value={locale}
        onChange={(event) => setLocale(event.target.value)}
      >
        {languages.map((language) => (
          <option
            key={language.code}
            value={language.code}
            lang={language.code}
          >
            {language.name}
          </option>
        ))}
      </select>
    </label>
  );
}
