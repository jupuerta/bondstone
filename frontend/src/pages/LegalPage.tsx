import { useEffect, useState, type ReactNode } from "react";
import { PageHead } from "../components/layout/PageHead";
import { PageShell } from "../components/layout/PageShell";
import { CONTACT_EMAIL, LINKEDIN_URL } from "../components/layout/SiteFooter";
import { Button } from "../components/ui/Button";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

const SECTIONS = ["who", "data", "none", "rights", "terms"] as const;
const KEPT = ["arch", "ip", "local"] as const;
const ABSENT = ["accounts", "email", "cookies", "third"] as const;

/** Aviso legal y privacidad: quién responde, qué se guarda, qué no hay y en qué condiciones se ofrece. */
export function LegalPage() {
  const t = useT();
  usePageTitle(t("web.legal.title"));
  const active = useActiveSection(SECTIONS);

  return (
    <PageShell>
      <PageHead
        eyebrow={t("web.legal.eyebrow")}
        title={t("web.legal.title")}
        lead={t("web.legal.lead")}
        aside={<span className="num text-meta text-ink-2 md:pb-2">{t("web.legal.updated")}</span>}
      />

      <div className="page pt-8 pb-4 md:pt-12 md:pb-8 min-[1200px]:grid min-[1200px]:grid-cols-[minmax(0,840px)_190px] min-[1200px]:items-start min-[1200px]:gap-x-16">
        <nav aria-label={t("web.legal.nav")} className="mb-10 flex flex-wrap gap-2 md:mb-14 min-[1200px]:hidden">
          {SECTIONS.map((id) => (
            <a
              key={id}
              href={`#${id}`}
              className="inline-flex min-h-9 items-center rounded-pill border border-line px-3 text-small text-ink-2 no-underline hover:border-ink hover:text-ink"
            >
              {t(`web.legal.${id}.title`)}
            </a>
          ))}
        </nav>

        <div className="flex flex-col gap-10 md:gap-14">
          <Section id="who" title={t("web.legal.who.title")}>
            <p className="max-w-prose-legal text-body text-ink-2">{t("web.legal.who.p1")}</p>
            <div className="flex flex-wrap gap-2">
              <Button variant="ghost" size="md" href={`mailto:${CONTACT_EMAIL}`}>
                {t("web.legal.who.email")}
              </Button>
              <Button variant="ghost" size="md" href={LINKEDIN_URL} external>
                {t("web.legal.who.linkedin")}
              </Button>
            </div>
          </Section>

          <Section id="data" title={t("web.legal.data.title")} lead={t("web.legal.data.lead")}>
            <Rows keys={KEPT.map((k) => `data.${k}`)} />
          </Section>

          <Section id="none" title={t("web.legal.none.title")} lead={t("web.legal.none.lead")}>
            <Rows keys={ABSENT.map((k) => `none.${k}`)} />
            <p className="max-w-prose-legal text-small text-ink-2">{t("web.legal.none.closing")}</p>
          </Section>

          <Section id="rights" title={t("web.legal.rights.title")}>
            <p className="max-w-prose-legal text-body text-ink-2">{t("web.legal.rights.p1")}</p>
            <p className="max-w-prose-legal text-body text-ink-2">{t("web.legal.rights.p2")}</p>
          </Section>

          <Section id="terms" title={t("web.legal.terms.title")}>
            {[1, 2, 3, 4].map((i) => (
              <p key={i} className="max-w-prose-legal text-body text-ink-2">
                {t(`web.legal.terms.p${i}`)}
              </p>
            ))}
          </Section>
        </div>

        <nav aria-label={t("web.legal.nav")} className="sticky top-10 hidden min-[1200px]:block">
          <span className="eyebrow">{t("web.legal.nav")}</span>
          <ul className="mt-3 flex flex-col border-l border-line">
            {SECTIONS.map((id) => (
              <li key={id} className="flex">
                <a
                  href={`#${id}`}
                  aria-current={active === id ? "true" : undefined}
                  className={`-ml-px border-l py-1.5 pl-3.5 text-small no-underline ${
                    active === id ? "border-ink font-semibold text-ink" : "border-transparent text-ink-2 hover:text-ink"
                  }`}
                >
                  {t(`web.legal.${id}.title`)}
                </a>
              </li>
            ))}
          </ul>
        </nav>
      </div>
    </PageShell>
  );
}

/** La sección por la que va el lector: la última cuyo título ya ha cruzado la banda alta de la
    ventana. Al final del documento manda la última, porque la página deja de desplazarse antes de
    que llegue a esa banda. */
function useActiveSection(ids: readonly string[]): string {
  const [active, setActive] = useState(ids[0]);

  useEffect(() => {
    const read = () => {
      const root = document.documentElement;
      if (window.scrollY + window.innerHeight >= root.scrollHeight - 4) {
        setActive(ids[ids.length - 1]);
        return;
      }
      const band = window.innerHeight * 0.2;
      let current = ids[0];
      for (const id of ids) {
        const top = document.getElementById(id)?.getBoundingClientRect().top;
        if (top !== undefined && top <= band) current = id;
      }
      setActive(current);
    };

    read();
    window.addEventListener("scroll", read, { passive: true });
    window.addEventListener("resize", read);
    return () => {
      window.removeEventListener("scroll", read);
      window.removeEventListener("resize", read);
    };
  }, [ids]);

  return active;
}

function Section({ id, title, lead, children }: { id: string; title: string; lead?: string; children: ReactNode }) {
  return (
    <section id={id} className="flex scroll-mt-8 flex-col gap-5">
      <div className="flex flex-col gap-2">
        <h2 className="text-[30px] tracking-[-0.02em] md:text-section">{title}</h2>
        {lead && <p className="max-w-prose-legal text-body text-ink-2">{lead}</p>}
      </div>
      {children}
    </section>
  );
}

function Rows({ keys }: { keys: string[] }) {
  const t = useT();
  return (
    <ul className="hairline-rows flex max-w-[840px] flex-col border-y border-line">
      {keys.map((key) => (
        <li key={key} className="grid gap-1.5 py-4 md:grid-cols-[minmax(0,180px)_minmax(0,1fr)] md:gap-10 md:py-5">
          <span className="eyebrow md:pt-1.5">{t(`web.legal.${key}.label`)}</span>
          <p className="text-body text-ink-2">{t(`web.legal.${key}.body`)}</p>
        </li>
      ))}
    </ul>
  );
}
