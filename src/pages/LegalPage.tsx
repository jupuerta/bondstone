import { Link } from "react-router-dom";
import { PageShell } from "../components/layout/PageShell";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

export type LegalKind = "privacy" | "terms" | "collection";

/** Privacidad, términos y aviso de recolección. El texto legal está pendiente y la página lo dice. */
export function LegalPage({ kind }: { kind: LegalKind }) {
  const t = useT();
  const title = t(`web.legal.${kind}`);
  usePageTitle(title);

  return (
    <PageShell>
      <section className="page flex max-w-[760px] flex-col gap-6 py-16 md:py-24">
        <h1 className="text-[40px] leading-none tracking-[-0.03em] md:text-page">{title}</h1>
        <p className="text-body text-ink-2">{t("web.legal.pending")}</p>
        <Link to="/about" className="link self-start">
          {t("web.nav.about")}
        </Link>
      </section>
    </PageShell>
  );
}
