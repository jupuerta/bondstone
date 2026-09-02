import { LOCALES, useLanguage } from "../../i18n";

export function LanguageToggle({ dark = false }: { dark?: boolean }) {
  const { locale, setLocale, t } = useLanguage();

  return (
    <div role="radiogroup" aria-label={t("web.nav.language")} className={`inline-flex rounded-pill border p-0.5 font-mono text-meta ${dark ? "border-panel-line" : "border-line-2"}`}>
      {LOCALES.map((candidate) => {
        const on = candidate === locale;
        return (
          <button
            key={candidate}
            type="button"
            role="radio"
            aria-checked={on}
            lang={candidate}
            onClick={() => setLocale(candidate)}
            className={`min-h-7 rounded-pill px-2.5 uppercase transition-colors ${on ? "bg-ink font-semibold text-white" : "text-ink-2 hover:bg-[color-mix(in_oklab,var(--af-ink)_6%,transparent)]"}`}
          >
            {candidate}
          </button>
        );
      })}
    </div>
  );
}
