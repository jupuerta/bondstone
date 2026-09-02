import { PageHead } from "../components/layout/PageHead";
import { PageShell } from "../components/layout/PageShell";
import { LINKEDIN_URL } from "../components/layout/SiteFooter";
import { Button } from "../components/ui/Button";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

const BLOCKS: Array<{ key: string; link?: { label: string; to?: string; href?: string } }> = [
  { key: "who", link: { label: "web.about.who.contact", href: LINKEDIN_URL } },
  { key: "what" },
  { key: "how" },
  { key: "net" },
  { key: "stack" },
  { key: "limits", link: { label: "web.about.limits.link", to: "/model" } },
  { key: "data", link: { label: "web.about.data.link", to: "/aviso-de-recoleccion" } },
];

export function AboutPage() {
  const t = useT();
  usePageTitle(t("web.about.title"));

  return (
    <PageShell>
      <PageHead eyebrow={t("web.about.eyebrow")} title={t("web.about.title")} lead={t("web.about.lead")} />
      <section className="page grid gap-8 py-10 md:grid-cols-2 md:gap-x-16 md:gap-y-10 md:py-14">
        {BLOCKS.map((block) => (
          <div key={block.key} className="flex flex-col gap-3 border-t border-ink pt-5">
            <h2 className="text-[24px] tracking-[-0.02em] md:text-[28px]">{t(`web.about.${block.key}.title`)}</h2>
            <p className="text-body text-ink-2">{t(`web.about.${block.key}.text`)}</p>
            {block.link && (block.link.href ? (
              <Button variant="ghost" size="md" href={block.link.href} external className="self-start">
                {t(block.link.label)}
              </Button>
            ) : (
              <Button variant="ghost" size="md" to={block.link.to!} className="self-start">
                {t(block.link.label)}
              </Button>
            ))}
          </div>
        ))}
      </section>
    </PageShell>
  );
}
