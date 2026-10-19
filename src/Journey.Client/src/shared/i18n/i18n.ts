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
] as const;
export type Locale = (typeof languages)[number]["code"];
const catalogs: Record<Locale, Record<string, string>> = {
  en,
  es,
  ja,
  "zh-CN": zh,
  ru,
};
export const localeKey = "journey.locale";
export function normalizeLocale(value: string | null): Locale {
  return languages.some((language) => language.code === value)
    ? (value as Locale)
    : "en";
}
function readLocale(): Locale {
  try {
    return normalizeLocale(localStorage.getItem(localeKey));
  } catch {
    return "en";
  }
}
let locale = readLocale();
const listeners = new Set<() => void>();
function applyLocale() {
  document.documentElement.lang = locale;
  document.documentElement.dir = "ltr";
}
applyLocale();
export function getLocale() {
  return locale;
}
export function setLocale(value: string) {
  locale = normalizeLocale(value);
  try {
    localStorage.setItem(localeKey, locale);
  } catch {
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
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    getLocale,
    () => "en" as Locale,
  );
}
export function t(
  key: string,
  values: Record<string, string | number> = {},
): string {
  const message = catalogs[locale][key] ?? catalogs.en[key] ?? key;
  return message.replace(/\{(\w+)\}/g, (match, name: string) =>
    values[name] === undefined
      ? match
      : typeof values[name] === "number"
        ? formatNumber(values[name])
        : String(values[name]),
  );
}
export function formatNumber(value: number): string {
  return new Intl.NumberFormat(locale).format(value);
}
export function formatDate(
  value: string | number | Date,
  options?: Intl.DateTimeFormatOptions,
): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat(
    locale,
    options ?? { dateStyle: "medium", timeStyle: "short" },
  ).format(date);
}
export function count(key: string, value: number): string {
  const category = new Intl.PluralRules(locale).select(value);
  const specific = `${key}.${category}`;
  return t(catalogs[locale][specific] ? specific : `${key}.other`, {
    count: value,
  });
}
