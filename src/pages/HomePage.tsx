import { useState } from "react";
import { Link } from "react-router-dom";
import { PageShell } from "../components/layout/PageShell";
import { FIRST_CHALLENGE } from "../components/layout/SiteHeader";
import { EnginePanel } from "../components/engine/EnginePanel";
import { Button } from "../components/ui/Button";
import { Callout, CalloutRow } from "../components/ui/Callout";
import { ArrowIcon, LinkIcon, PersonIcon } from "../components/ui/Icons";
import { Segmented } from "../components/ui/Segmented";
import { savedWork, type SavedWork } from "../domain/savedWork";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

const DEMO_BEFORE = import.meta.env.VITE_DEMO_BEFORE_TOKEN ?? null;
const DEMO_AFTER = import.meta.env.VITE_DEMO_AFTER_TOKEN ?? null;
const DEMO_FAULT_NODE = import.meta.env.VITE_DEMO_FAULT_NODE ?? "Postgres";

const FIGURES: Array<[string, string]> = [
  ["16", "web.home.figures.types"],
  ["25", "web.home.figures.rules"],
  ["7", "web.home.figures.areas"],
  ["23", "web.home.figures.challenges"],
  ["2", "web.home.figures.languages"],
];

export function HomePage() {
  const t = useT();
  usePageTitle(null);
  const [work] = useState<SavedWork[]>(() => savedWork().slice(0, 2));

  return (
    <PageShell>
      {work.length > 0 && <Continue work={work} />}

      {/* Héroe: título arriba a todo el ancho, y debajo dos columnas que empiezan a la misma altura. */}
      <section className="border-b border-line">
        <div className="page flex flex-col gap-8 py-9 md:gap-10 md:py-24">
          <div className="flex flex-col gap-5 md:gap-6">
            <span className="eyebrow eyebrow-accent">{t("web.home.eyebrow")}</span>
            <h1 className="text-[42px] leading-none tracking-[-0.035em] md:text-[clamp(64px,6.4vw,96px)] md:leading-[0.96]">{t("web.home.headline")}</h1>
          </div>
          <div className="grid gap-6 border-t border-line pt-6 md:grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)] md:gap-16 md:pt-8">
            <div className="flex flex-col gap-5">
              <p className="text-[17px] leading-normal text-ink-2 md:text-[20px]">{t("web.home.subhead")}</p>
              <div className="flex flex-col gap-2">
                <div className="flex flex-col gap-3 md:flex-row md:flex-wrap md:items-center">
                  <Button to={FIRST_CHALLENGE}>{t("web.home.cta.primary")}</Button>
                  <Button variant="ghost" to="/challenges">
                    {t("web.home.cta.scratch")}
                  </Button>
                  <Link to="/challenges" className="link text-[15px] md:ml-2">
                    {t("web.home.cta.browse")}
                  </Link>
                </div>
                <span className="text-code text-ink-3 md:pl-1">{t("web.home.cta.primary.sub")}</span>
              </div>
            </div>
            <Callout label={t("web.home.notice.label")}>
              <CalloutRow icon={<LinkIcon size={20} />}>{t("web.home.condition")}</CalloutRow>
              <CalloutRow icon={<PersonIcon size={20} />}>{t("web.home.audience")}</CalloutRow>
            </Callout>
          </div>
        </div>
      </section>

      <Demo />

      <section className="border-y border-line">
        <div className="page grid grid-cols-2 gap-6 py-7 md:grid-cols-5 md:gap-0 md:py-10">
          {FIGURES.map(([value, key], index) => (
            <div key={key} className={`flex flex-col gap-1.5 md:px-6 ${index === 0 ? "" : "md:border-l md:border-line"}`}>
              <span className="num text-[36px] leading-none tracking-[-0.02em] md:text-figure">{value}</span>
              <span className="text-code text-ink-2">{t(key)}</span>
            </div>
          ))}
        </div>
      </section>

      <section>
        <div className="page flex flex-col gap-6 py-12 md:gap-8 md:py-[88px]">
          <h2 className="text-[30px] tracking-[-0.02em] md:text-section">{t("web.home.loop.title")}</h2>
          <div className="grid gap-6 md:grid-cols-4 md:gap-7">
            {["1", "2", "3", "4"].map((n) => (
              <div key={n} className="flex flex-col gap-2.5 border-t border-ink pt-4">
                <span className="font-mono text-meta text-ink-2">0{n}</span>
                <h3 className="text-[20px] md:text-[22px] md:tracking-[-0.015em]">{t(`web.home.loop.${n}.step`)}</h3>
                <p className="text-[15px] leading-normal text-ink-2">{t(`web.home.loop.${n}.text`)}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="border-t border-line">
        <div className="page grid gap-6 py-12 md:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)] md:gap-16 md:py-[88px]">
          <div className="flex flex-col gap-6">
            <h2 className="text-[30px] tracking-[-0.02em] md:text-section">{t("web.home.criterion.title")}</h2>
            <div className="hairline-rows flex flex-col border-b border-line">
              {[
                ["web.home.criterion.rules", "/rules"],
                ["web.home.criterion.model", "/model"],
                ["web.home.criterion.mechanisms", "/mechanisms"],
              ].map(([key, to]) => (
                <Link key={key} to={to} className="flex min-h-[52px] items-center justify-between gap-3 text-[17px] font-semibold text-ink no-underline hover:text-accent">
                  {t(key)}
                  <span className="inline-flex items-center gap-2.5 font-mono text-meta font-normal text-ink-3">
                    {to} <ArrowIcon size={16} className="text-ink" />
                  </span>
                </Link>
              ))}
            </div>
          </div>
          <div className="flex flex-col gap-4 md:pt-3">
            <p className="text-body text-ink-2 md:text-[17px]">{t("web.home.criterion.deterministic")}</p>
            <p className="text-body text-ink-2 md:text-[17px]">{t("web.home.criterion.teaching")}</p>
            <p className="text-body text-ink-2 md:text-[17px]">{t("web.home.criterion.noAi")}</p>
          </div>
        </div>
      </section>

      <section className="border-t border-line">
        <div className="page flex flex-col gap-6 py-12 md:gap-8 md:py-[88px]">
          <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between md:gap-8">
            <div className="max-w-[760px]">
              <h2 className="text-[30px] tracking-[-0.02em] md:text-section">{t("web.home.challenges.title")}</h2>
              <p className="mt-3 text-body text-ink-2 md:text-[17px]">{t("web.home.challenges.lead")}</p>
            </div>
            <Button variant="ghost" size="md" to="/challenges" className="self-start">
              {t("web.home.cta.browse")}
            </Button>
          </div>
          <div className="grid gap-4 md:grid-cols-3 md:gap-5">
            {(["beginner", "intermediate", "advanced"] as const).map((band, index) => (
              <Link key={band} to="/challenges" className="flex flex-col gap-2.5 rounded-card border border-line bg-surface p-5 text-ink no-underline transition-colors hover:border-ink md:p-[26px]">
                <div className="flex items-baseline justify-between gap-3">
                  <h3 className="text-[21px] md:text-block">{t(`ui.home.band.${band}`)}</h3>
                  <span className="font-mono text-meta text-ink-3">{t("web.home.challenges.count", { count: [7, 11, 5][index] })}</span>
                </div>
                <p className="text-[15px] text-ink-2">{t(`ui.home.pathBandLead.${band}`)}</p>
                <p className="text-[15px]">«{t(`web.home.challenges.example.${band}`)}»</p>
              </Link>
            ))}
          </div>
        </div>
      </section>

      <section className="border-t border-line">
        <div className="page grid gap-6 py-12 md:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)] md:gap-16 md:py-[88px]">
          <h2 className="text-[30px] tracking-[-0.02em] md:text-section">{t("web.home.access.title")}</h2>
          <div className="grid gap-5 md:grid-cols-2 md:gap-x-8">
            {["share", "export", "progress", "languages"].map((key) => (
              <p key={key} className="border-t border-line pt-4 text-body text-ink-2 md:text-[17px]">
                {t(`web.home.access.${key}`)}
              </p>
            ))}
          </div>
        </div>
      </section>

      <section className="border-t border-line bg-surface">
        <div className="page flex flex-col gap-4 py-9 md:flex-row md:items-center md:justify-between md:gap-12 md:py-14">
          <p className="max-w-[860px] text-[17px] leading-normal md:text-lead">{t("web.home.notWhat")}</p>
          <Link to="/about" className="inline-flex items-center gap-2 font-semibold text-ink no-underline hover:text-accent md:whitespace-nowrap">
            {t("web.home.aboutLink")}
            <ArrowIcon size={16} />
          </Link>
        </div>
      </section>
    </PageShell>
  );
}

function Continue({ work }: { work: SavedWork[] }) {
  const t = useT();
  return (
    <div className="page pt-4">
      <div className="flex flex-col gap-3 rounded-card border border-line bg-surface px-4 py-3 md:flex-row md:items-center md:gap-[18px]">
        <strong className="text-[15px]">{t("web.home.continue.title")}</strong>
        <div className="flex flex-1 flex-wrap gap-2">
          {work.map((entry) => (
            <Link key={entry.editToken} to={`/e/${encodeURIComponent(entry.editToken)}`} className="inline-flex min-h-9 items-center rounded-pill border border-line px-3 text-code text-ink no-underline hover:border-ink">
              {entry.challengeSlug ? t("ui.home.workChallenge", { title: t(`challenge.${entry.challengeSlug}.title`) }) : entry.name}
            </Link>
          ))}
        </div>
        <Button variant="ghost" size="sm" to="/challenges">
          {t("web.home.continue.cta")}
        </Button>
      </div>
    </div>
  );
}

function Demo() {
  const t = useT();
  const [which, setWhich] = useState<"before" | "after">("before");
  const token = which === "after" && DEMO_AFTER ? DEMO_AFTER : DEMO_BEFORE;

  return (
    <section id="demo" className="scroll-mt-6">
      <div className="page flex flex-col gap-5 py-10 md:py-[72px]">
        <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between md:gap-8">
          <div>
            <span className="eyebrow eyebrow-accent">{t("web.home.demo.eyebrow")}</span>
            <h2 className="mt-2 text-[30px] tracking-[-0.02em] md:text-section">{t("web.home.demo.label")}</h2>
          </div>
          <p className="max-w-[380px] text-[15px] text-ink-2 md:text-right">{t("web.home.demo.caption")}</p>
        </div>
        <EnginePanel
          id="demo"
          shareToken={token}
          scenario={{ nodeName: DEMO_FAULT_NODE }}
          offerOpen
          headerRight={
            DEMO_AFTER ? (
              <Segmented
                dark
                label={t("web.home.demo.label")}
                value={which}
                onChange={setWhich}
                options={[
                  { value: "before", label: t("web.home.demo.before") },
                  { value: "after", label: t("web.home.demo.after") },
                ]}
              />
            ) : undefined
          }
        />
        {DEMO_AFTER && <p className="font-mono text-code text-ink-3">{t("web.home.demo.after")}: {t("web.home.demo.afterNote")}</p>}
        <p className="font-mono text-meta text-ink-3 md:hidden">{t("web.engine.narrow")}</p>
      </div>
    </section>
  );
}
