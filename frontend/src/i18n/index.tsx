import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { render, type Catalog } from "./catalog";
import type { LocalizedText } from "../types/architecture";
import es from "./es.json";

export type Locale = "es" | "en";

export const LOCALES: Locale[] = ["es", "en"];

export const LOCALE_NAMES: Record<Locale, string> = { es: "Español", en: "English" };

/** Español por defecto (decisión 00 §7); el navegador solo decide si no hay elección guardada. */
const DEFAULT_LOCALE: Locale = "es";

/*
 * Los dos catálogos juntos son 481 KB —131 comprimidos—, que era más de la mitad del fichero que
 * descarga la portada. El inglés se lo llevaba entero quien nunca lo iba a leer.
 *
 * El español no se aplaza: es el idioma por omisión **y** el respaldo de `render`, así que hace
 * falta se mire como se mire, y pedirlo aparte serían los mismos bytes un viaje más tarde. El
 * inglés sí, y quien lo use paga una petición extra en lugar de todo el mundo pagar el catálogo
 * que no usa.
 */
const CATALOGS: Partial<Record<Locale, Catalog>> = { es: es as Catalog };

const FETCH: Record<Locale, (() => Promise<Catalog>) | null> = {
  es: null,
  en: () => import("./en.json").then((module) => module.default as Catalog),
};

export function catalogReady(locale: Locale): boolean {
  return CATALOGS[locale] !== undefined;
}

export async function loadCatalog(locale: Locale): Promise<void> {
  const fetch = FETCH[locale];
  if (catalogReady(locale) || fetch === null) return;
  CATALOGS[locale] = await fetch();
}

const STORAGE_KEY = "bondstone.locale";

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
    // Sin el catálogo pedido se contesta en español antes que con la clave en crudo: es lo que
    // pasa si la petición del inglés se pierde, y una página en el idioma equivocado se entiende.
    const catalog = CATALOGS[locale] ?? CATALOGS[DEFAULT_LOCALE]!;
    return render(text, catalog, locale, CATALOGS[DEFAULT_LOCALE]);
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
  const [ready, setReady] = useState(() => catalogReady(locale));

  // Solo en el primer dibujo, y solo para quien llega en inglés: se espera al catálogo en vez de
  // pintar la página en español y cambiarla medio segundo después.
  useEffect(() => {
    if (catalogReady(locale)) {
      setReady(true);
      return;
    }

    let live = true;
    // Pase lo que pase se dibuja: si la petición falla, `createTranslator` contesta en español.
    loadCatalog(locale).catch(() => {}).finally(() => {
      if (live) setReady(true);
    });

    return () => {
      live = false;
    };
  }, [locale]);

  useEffect(() => {
    try {
      window.localStorage.setItem(STORAGE_KEY, locale);
    } catch {
      /* sin almacenamiento: el idioma vive solo en esta visita */
    }
    document.documentElement.lang = locale;
  }, [locale]);

  // Cambiar de idioma trae el catálogo **antes** de cambiarlo: al revés, la página parpadearía en
  // español justo después de que alguien pulse «English».
  const setLocale = useCallback((next: Locale) => {
    if (catalogReady(next)) {
      setLocaleState(next);
      return;
    }

    loadCatalog(next).catch(() => {}).finally(() => setLocaleState(next));
  }, []);

  const t = useMemo(() => createTranslator(locale), [locale]);
  const value = useMemo(() => ({ locale, setLocale, t }), [locale, setLocale, t]);

  return <LanguageContext.Provider value={value}>{ready ? children : null}</LanguageContext.Provider>;
}

export function useLanguage(): LanguageContextValue {
  const context = useContext(LanguageContext);
  if (context === null) throw new Error("useLanguage must be used inside a LanguageProvider.");
  return context;
}

export function useT(): Translator {
  return useLanguage().t;
}
