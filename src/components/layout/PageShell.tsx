import type { ReactNode } from "react";
import { useT } from "../../i18n";
import { SiteFooter } from "./SiteFooter";
import { SiteHeader } from "./SiteHeader";

/** Cabecera, contenido y pie. Todas las páginas públicas pasan por aquí. */
export function PageShell({ children }: { children: ReactNode }) {
  const t = useT();

  return (
    <div className="flex min-h-screen flex-col bg-bg text-ink">
      <a
        href="#main"
        className="sr-only rounded-pill bg-ink px-4 py-2 text-white focus:not-sr-only focus:absolute focus:top-3 focus:left-3 focus:z-[60]"
      >
        {t("web.a11y.skip")}
      </a>
      <SiteHeader />
      <main id="main" className="flex flex-1 flex-col">
        {children}
      </main>
      <SiteFooter />
    </div>
  );
}
