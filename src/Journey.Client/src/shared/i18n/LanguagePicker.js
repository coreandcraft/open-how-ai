import { jsx as _jsx } from "react/jsx-runtime";
import { languages, setLocale, t, useLocale } from "./i18n";
export function LanguagePicker() {
    const locale = useLocale();
    return (_jsx("label", { className: "language-picker", children: _jsx("select", { "aria-label": t("Language"), value: locale, onChange: (event) => setLocale(event.target.value), children: languages.map((language) => (_jsx("option", { value: language.code, lang: language.code, children: language.name }, language.code))) }) }));
}
