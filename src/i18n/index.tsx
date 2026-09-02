import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { render, type Catalog } from "./catalog";
import type { LocalizedText } from "../types/architecture";
import es from "./es.json";
import en from "./en.json";

export type Locale = "es" | "en";

export const LOCALES: Locale[] = ["es", "en"];

export const LOCALE_NAMES: Record<Locale, string> = { es: "Español", en: "English" };

const CATALOGS: Record<Locale, Catalog> = { es: es as Catalog, en: en as Catalog };

/** Español por defecto (decisión 00 §7); el navegador solo decide si no hay elección guardada. */
const DEFAULT_LOCALE: Locale = "es";

const STORAGE_KEY = "archforge.locale";

export interface Translator {
  (text: LocalizedText): string;
  (key: string, args?: Record<string, string | number | LocalizedText>): string;
}

interface LanguageContextValue {
  locale: Locale;
  setLocale: (locale: Locale) => void;
  t: Translator;
}

const LanguageContext = createContext<LanguageContextValue | null>(null);

export function createTranslator(locale: Locale): Translator {
  return ((first: LocalizedText | string, args?: Record<string, string | number | LocalizedText>) => {
    const text: LocalizedText = typeof first === "string" ? { key: first, args } : first;
    return render(text, CATALOGS[locale], locale, CATALOGS[DEFAULT_LOCALE]);
  }) as Translator;
}

function initialLocale(): Locale {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    if (stored === "es" || stored === "en") return stored;
    const wanted = navigator.languages?.[0] ?? navigator.language ?? "";
    return wanted.toLowerCase().startsWith("en") ? "en" : DEFAULT_LOCALE;
  } catch {
    return DEFAULT_LOCALE;
  }
}

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(initialLocale);

  useEffect(() => {
    try {
      window.localStorage.setItem(STORAGE_KEY, locale);
    } catch {
      /* sin almacenamiento: el idioma vive solo en esta visita */
    }
    document.documentElement.lang = locale;
  }, [locale]);

  const setLocale = useCallback((next: Locale) => setLocaleState(next), []);
  const t = useMemo(() => createTranslator(locale), [locale]);
  const value = useMemo(() => ({ locale, setLocale, t }), [locale, setLocale, t]);

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useLanguage(): LanguageContextValue {
  const context = useContext(LanguageContext);
  if (context === null) throw new Error("useLanguage must be used inside a LanguageProvider.");
  return context;
}

export function useT(): Translator {
  return useLanguage().t;
}
