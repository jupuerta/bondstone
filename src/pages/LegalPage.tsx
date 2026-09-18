import { PageHead } from "../components/layout/PageHead";
import { PageShell } from "../components/layout/PageShell";
import { CONTACT_EMAIL, LINKEDIN_URL } from "../components/layout/SiteFooter";
import { Button } from "../components/ui/Button";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

const SECTIONS: Array<{ key: string; paragraphs: number; contact?: boolean }> = [
  { key: "who", paragraphs: 1, contact: true },
  { key: "data", paragraphs: 3 },
  { key: "none", paragraphs: 2 },
  { key: "rights", paragraphs: 2 },
  { key: "terms", paragraphs: 4 },
];

/** Aviso legal y privacidad en una sola página: quién responde, qué se guarda, qué no hay y en qué condiciones se ofrece. */
export function LegalPage() {
  const t = useT();
  usePageTitle(t("web.legal.title"));

  return (
    <PageShell>
      <PageHead eyebrow={t("web.legal.eyebrow")} title={t("web.legal.title")} lead={t("web.legal.lead")} />
      <section className="page flex max-w-[760px] flex-col gap-8 py-10 md:gap-10 md:py-14">
        {SECTIONS.map((section) => (
          <div key={section.key} className="flex flex-col gap-3 border-t border-ink pt-5">
            <h2 className="text-[24px] tracking-[-0.02em] md:text-[28px]">{t(`web.legal.${section.key}.title`)}</h2>
            {Array.from({ length: section.paragraphs }, (_, i) => (
              <p key={i} className="text-body text-ink-2">
                {t(`web.legal.${section.key}.p${i + 1}`)}
              </p>
            ))}
            {section.contact && (
              <div className="flex flex-wrap gap-2">
                <Button variant="ghost" size="md" href={`mailto:${CONTACT_EMAIL}`} className="self-start">
                  {t("web.legal.who.email")}
                </Button>
                <Button variant="ghost" size="md" href={LINKEDIN_URL} external className="self-start">
                  {t("web.about.who.contact")}
                </Button>
              </div>
            )}
          </div>
        ))}
      </section>
    </PageShell>
  );
}
