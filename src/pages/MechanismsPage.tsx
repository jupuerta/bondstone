import { useEffect } from "react";
import { Link } from "react-router-dom";
import { PageHead } from "../components/layout/PageHead";
import { PageShell } from "../components/layout/PageShell";
import { ReferenceBack } from "../components/layout/ReferenceBack";
import { Callout } from "../components/ui/Callout";
import { Chip } from "../components/ui/Chip";
import { MECHANISMS, MECHANISM_GROUPS, MECHANISM_IDS, mechanismBand, mechanismLabel, scores, type MechanismId } from "../domain/mechanisms";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

export function MechanismsPage() {
  const t = useT();
  usePageTitle(t("ui.mechanisms.eyebrow"));

  useEffect(() => {
    const id = decodeURIComponent(window.location.hash.slice(1));
    if (id) document.getElementById(id)?.scrollIntoView({ block: "start" });
  }, []);

  return (
    <PageShell>
      <PageHead eyebrow={t("ui.mechanisms.eyebrow")} title={t("ui.mechanisms.headline")} lead={t("ui.mechanisms.lead")} />

      <div className="page flex flex-col gap-10 pt-8 md:gap-14 md:pt-12">
        <nav aria-label={t("ui.mechanisms.eyebrow")} className="flex flex-wrap gap-2">
          {MECHANISM_IDS.map((id) => (
            <a key={id} href={`#${id}`} className="inline-flex min-h-9 items-center rounded-pill border border-line px-3 text-small text-ink-2 no-underline hover:border-ink hover:text-ink">
              {t(mechanismLabel(id))}
            </a>
          ))}
        </nav>
        <p className="max-w-[880px] rounded-card border border-line bg-surface px-5 py-4 text-small text-ink-2">{t("ui.mechanisms.whyNot")}</p>

        {MECHANISM_GROUPS.map((group) => (
          <section key={group.id} className="flex flex-col gap-5">
            <div className="flex flex-col gap-2">
              <h2 className="text-[30px] tracking-[-0.02em] md:text-section">{t(`ui.mechanisms.group.${group.id}`)}</h2>
              <p className="max-w-[880px] text-body text-ink-2">{t(`ui.mechanisms.group.${group.id}.lead`)}</p>
            </div>
            <div className="flex flex-col gap-4">
              {group.mechanisms.map((id) => (
                <Sheet key={id} id={id} />
              ))}
            </div>
          </section>
        ))}

        <p className="max-w-[880px] text-small text-ink-3">{t("ui.mechanisms.closing")}</p>
      </div>

      <ReferenceBack />
    </PageShell>
  );
}

function Sheet({ id }: { id: MechanismId }) {
  const t = useT();
  const sheet = MECHANISMS[id];

  return (
    <article id={id} className="flex scroll-mt-6 flex-col gap-4 rounded-card border border-line bg-surface p-5 md:p-6">
      <header className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <h3 className="text-block">{t(mechanismLabel(id))}</h3>
        <Chip>{t(mechanismBand(id, "where"))}</Chip>
        <span className="flex flex-wrap gap-1.5 md:ml-auto">
          <Chip variant={scores(sheet) ? "passed" : "outline"}>{t(scores(sheet) ? "ui.mechanisms.scored" : "ui.mechanisms.notScored")}</Chip>
          <Chip variant={sheet.simulated ? "passed" : "outline"}>{t(sheet.simulated ? "ui.mechanisms.simulated" : "ui.mechanisms.notSimulated")}</Chip>
        </span>
      </header>
      <p className="max-w-[880px] text-body">{t(mechanismBand(id, "problem"))}</p>
      <div className="flex flex-col gap-2">
        <span className="eyebrow">{t("ui.mechanisms.band.model")}</span>
        <p className="max-w-[880px] text-small text-ink-2">{t(mechanismBand(id, "model"))}</p>
        {sheet.formula && <pre className="overflow-x-auto rounded-card border border-line bg-bg px-4 py-3 font-mono text-code leading-[1.8] text-ink-2">{t(mechanismBand(id, "formula"))}</pre>}
      </div>
      <Callout label={t("ui.mechanisms.band.avoid")} compact>
        <p className="text-small text-ink-2">{t(mechanismBand(id, "avoid"))}</p>
      </Callout>
      <div className="flex flex-col gap-2.5">
        <span className="eyebrow">{t("ui.mechanisms.band.watchers")}</span>
        <span className="flex flex-wrap gap-1.5">
          {sheet.rules.length === 0 ? (
            <span className="text-small text-ink-3">{t("ui.mechanisms.noRules")}</span>
          ) : (
            sheet.rules.map((code) => (
              <Link key={code} to={`/rules#${code}`} className="inline-flex min-h-8 items-center rounded-pill border border-line px-2.5 font-mono text-label uppercase tracking-[0.06em] text-ink-2 no-underline hover:border-ink hover:text-ink">
                {code}
              </Link>
            ))
          )}
        </span>
        <span className="flex flex-wrap gap-x-4 gap-y-1.5">
          {sheet.challenges.length === 0 ? (
            <span className="text-small text-ink-3">{t("ui.mechanisms.noChallenges")}</span>
          ) : (
            sheet.challenges.map((slug) => (
              <Link key={slug} to={`/c/${slug}`} className="link text-small">
                {t(`challenge.${slug}.title`)}
              </Link>
            ))
          )}
        </span>
      </div>
    </article>
  );
}
