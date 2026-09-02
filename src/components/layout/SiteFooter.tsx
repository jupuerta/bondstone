import { Link } from "react-router-dom";
import { useT } from "../../i18n";
import { LinkedInIcon } from "../ui/Icons";
import { LanguageToggle } from "./LanguageToggle";

export const LINKEDIN_URL = "https://www.linkedin.com/in/juanpuertagildegomez/";

export function SiteFooter() {
  const t = useT();
  const item = "inline-flex min-h-7 items-center text-ink-3 no-underline hover:text-ink";

  return (
    <footer className="mt-auto border-t border-line">
      <div className="page flex flex-col gap-3 py-6 font-mono text-meta md:flex-row md:flex-wrap md:items-center md:gap-x-7 md:py-7">
        <span className="text-ink-3">{t("ui.footer.copyright", { year: new Date().getFullYear() })}</span>
        <a href={LINKEDIN_URL} target="_blank" rel="noreferrer" className={`${item} gap-1.5`}>
          <LinkedInIcon size={13} />
          {t("ui.footer.linkedin")}
        </a>
        <Link to="/about" className={item}>
          {t("web.footer.about")}
        </Link>
        <Link to="/privacidad" className={item}>
          {t("ui.footer.privacy")}
        </Link>
        <Link to="/terminos" className={item}>
          {t("ui.footer.terms")}
        </Link>
        <Link to="/aviso-de-recoleccion" className={item}>
          {t("ui.footer.collection")}
        </Link>
        <span className="md:ml-auto">
          <LanguageToggle />
        </span>
      </div>
    </footer>
  );
}
