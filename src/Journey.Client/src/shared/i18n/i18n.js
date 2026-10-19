import { useSyncExternalStore } from "react";
import en from "./locales/en.json";
import es from "./locales/es.json";
import ja from "./locales/ja.json";
import zh from "./locales/zh-CN.json";
import ru from "./locales/ru.json";
export const languages = [
    { code: "en", name: "English" },
    { code: "es", name: "Español" },
    { code: "ja", name: "日本語" },
    { code: "zh-CN", name: "简体中文" },
    { code: "ru", name: "Русский" },
];
const catalogs = {
    en,
    es,
    ja,
    "zh-CN": zh,
    ru,
};
export const localeKey = "journey.locale";
export function normalizeLocale(value) {
    return languages.some((language) => language.code === value)
        ? value
        : "en";
}
function readLocale() {
    try {
        return normalizeLocale(localStorage.getItem(localeKey));
    }
    catch {
        return "en";
    }
}
let locale = readLocale();
const listeners = new Set();
function applyLocale() {
    document.documentElement.lang = locale;
    document.documentElement.dir = "ltr";
}
applyLocale();
export function getLocale() {
    return locale;
}
export function setLocale(value) {
    locale = normalizeLocale(value);
    try {
        localStorage.setItem(localeKey, locale);
    }
    catch {
        /* Session preference still works. */
    }
    applyLocale();
    listeners.forEach((listener) => listener());
}
window.addEventListener("storage", (event) => {
    if (event.key === localeKey || event.key === null) {
        locale = readLocale();
        applyLocale();
        listeners.forEach((listener) => listener());
    }
});
export function useLocale() {
    return useSyncExternalStore((listener) => {
        listeners.add(listener);
        return () => {
            listeners.delete(listener);
        };
    }, getLocale, () => "en");
}
export function t(key, values = {}) {
    const message = catalogs[locale][key] ?? catalogs.en[key] ?? key;
    return message.replace(/\{(\w+)\}/g, (match, name) => values[name] === undefined
        ? match
        : typeof values[name] === "number"
            ? formatNumber(values[name])
            : String(values[name]));
}
export function formatNumber(value) {
    return new Intl.NumberFormat(locale).format(value);
}
export function formatDate(value, options) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime()))
        return "—";
    return new Intl.DateTimeFormat(locale, options ?? { dateStyle: "medium", timeStyle: "short" }).format(date);
}
export function count(key, value) {
    const category = new Intl.PluralRules(locale).select(value);
    const specific = `${key}.${category}`;
    return t(catalogs[locale][specific] ? specific : `${key}.other`, {
        count: value,
    });
}
