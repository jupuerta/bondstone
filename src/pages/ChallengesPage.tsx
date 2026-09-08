import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ApiError, createArchitecture, getArchitectureHistory } from "../api/architectureClient";
import { listChallenges } from "../api/challengeClient";
import { PageHead } from "../components/layout/PageHead";
import { PageShell } from "../components/layout/PageShell";
import { Button } from "../components/ui/Button";
import { Callout } from "../components/ui/Callout";
import { Chip, FilterChip } from "../components/ui/Chip";
import { ArrowIcon } from "../components/ui/Icons";
import { InlineError } from "../components/ui/InlineError";
import { Segmented } from "../components/ui/Segmented";
import { Skeleton } from "../components/ui/Skeleton";
import { bestGains, type AreaGain } from "../domain/areasImproved";
import { bandLabelKey, bandLeadKey, groupByBand } from "../domain/challengeBands";
import { passedChallenges, startedChallenges } from "../domain/progress";
import { editorPath, rememberWork, savedWork } from "../domain/savedWork";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";
import { AREA_ORDER, type Area, type ChallengeSummary } from "../types/architecture";

type Status = "all" | "pending" | "passed";

const HISTORIES_READ = 8;

export function ChallengesPage() {
  const t = useT();
  usePageTitle(t("web.challenges.title"));
  const navigate = useNavigate();
  const [challenges, setChallenges] = useState<ChallengeSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [status, setStatus] = useState<Status>("all");
  const [area, setArea] = useState<Area | null>(null);
  const [passed] = useState(() => passedChallenges());
  const [started] = useState(() => startedChallenges());
  const [work] = useState(() => savedWork().slice(0, 3));
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [gains, setGains] = useState<AreaGain[]>([]);

  useEffect(() => {
    let cancelled = false;
    const tokens = savedWork()
      .slice(0, HISTORIES_READ)
      .map((entry) => entry.shareToken);
    if (tokens.length === 0) return;
    Promise.all(tokens.map((shareToken) => getArchitectureHistory(shareToken).catch(() => []))).then((histories) => {
      if (!cancelled) setGains(bestGains(histories));
    });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setError(null);
    listChallenges()
      .then((loaded) => {
        if (!cancelled) setChallenges(loaded);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof ApiError ? t(err.key, err.args) : t("web.challenges.loadFailed"));
      });
    return () => {
      cancelled = true;
    };
  }, [attempt, t]);

  const visible = useMemo(
    () =>
      (challenges ?? []).filter((challenge) => {
        if (area !== null && challenge.topic !== area) return false;
        if (status === "passed") return passed.has(challenge.slug);
        if (status === "pending") return !passed.has(challenge.slug);
        return true;
      }),
    [challenges, area, status, passed],
  );
  const bands = useMemo(() => groupByBand(visible), [visible]);
  const total = challenges?.length ?? 0;
  const passedCount = challenges?.filter((challenge) => passed.has(challenge.slug)).length ?? 0;

  const startFromScratch = () => {
    setCreating(true);
    setCreateError(null);
    createArchitecture(t("ui.home.untitled"))
      .then((architecture) => {
        rememberWork({ editToken: architecture.editToken, shareToken: architecture.shareToken, name: architecture.name, challengeSlug: null });
        navigate(`/e/${encodeURIComponent(architecture.editToken)}`);
      })
      .catch((err: unknown) => {
        setCreating(false);
        setCreateError(err instanceof ApiError ? t(err.key, err.args) : t("ui.home.createFailed"));
      });
  };

  let index = 0;

  return (
    <PageShell>
      <PageHead
        eyebrow={t("web.challenges.eyebrow")}
        title={t("web.challenges.title")}
        lead={t("web.challenges.lead")}
        aside={
          passedCount > 0 ? (
            <span className="num self-start whitespace-nowrap rounded-pill border border-line-2 px-4 py-2.5 text-small">
              <span className="font-semibold text-accent">{passedCount}</span> / {total} {t("ui.home.passed")}
            </span>
          ) : undefined
        }
      />

      <section className="page flex flex-col gap-4 pt-6 md:pt-8">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2.5">
          <Segmented
            label={t("ui.home.filterStatus")}
            value={status}
            onChange={setStatus}
            options={[
              { value: "all", label: t("ui.home.filter.all") },
              { value: "pending", label: t("ui.home.filter.pending") },
              { value: "passed", label: t("ui.home.filter.passed") },
            ]}
          />
          <div className="flex flex-wrap gap-2" role="group" aria-label={t("ui.home.filterArea")}>
            <FilterChip selected={area === null} onClick={() => setArea(null)}>
              {t("web.challenges.filter.allAreas")}
            </FilterChip>
            {AREA_ORDER.filter((candidate) => candidate !== "Cost").map((candidate) => (
              <FilterChip key={candidate} selected={area === candidate} onClick={() => setArea(area === candidate ? null : candidate)}>
                {t(`ui.area.${candidate}`)}
              </FilterChip>
            ))}
          </div>
        </div>

        {error !== null && (
          <div className="flex flex-col items-start gap-3 border-t border-line py-8">
            <p className="text-body font-semibold">{t("web.challenges.loadFailed")}</p>
            <p className="text-small text-ink-2">{t("web.challenges.loadFailedBody")}</p>
            <p className="font-mono text-meta text-ink-3">{error}</p>
            <Button variant="ghost" size="sm" onClick={() => setAttempt((n) => n + 1)}>
              {t("web.engine.retry")}
            </Button>
          </div>
        )}

        {challenges === null && error === null && (
          <div className="flex flex-col" aria-busy>
            {[0, 1, 2].map((n) => (
              <div key={n} className="grid grid-cols-[40px_1.6fr_2fr_1fr] gap-3 border-t border-line py-4">
                <Skeleton width={24} />
                <Skeleton width="80%" />
                <Skeleton width="90%" />
                <Skeleton width="60%" />
              </div>
            ))}
            <p className="pt-3 font-mono text-label text-ink-3">{t("ui.loading")}</p>
          </div>
        )}

        {challenges !== null && challenges.length === 0 && <p className="border-t border-line py-8 text-body text-ink-2">{t("ui.home.noChallenges")}</p>}

        {challenges !== null && challenges.length > 0 && visible.length === 0 && (
          <div className="flex flex-col items-start gap-2.5 border-t border-line py-8">
            <p className="text-body">{t("ui.home.noMatch")}</p>
            <button type="button" className="link text-[15px]" onClick={() => { setStatus("all"); setArea(null); }}>
              {t("ui.rules.filter.clear")}
            </button>
          </div>
        )}

        {bands.map((band) => (
          <div key={band.difficulty}>
            <div className="flex flex-col gap-1 pt-7 pb-3 md:flex-row md:items-baseline md:gap-4 md:pt-11">
              <h2 className="text-[26px] tracking-[-0.02em] md:text-[32px]">{t(bandLabelKey(band.difficulty))}</h2>
              <span className="text-body text-ink-2">{t(bandLeadKey(band.difficulty))}</span>
              <span className="font-mono text-meta text-ink-3 md:ml-auto">{t("web.home.challenges.count", { count: band.challenges.length })}</span>
            </div>
            <div className="eyebrow hidden gap-4 py-2 md:grid md:grid-cols-[48px_2.2fr_3fr_1fr_0.8fr_1fr]">
              <span>#</span>
              <span>{t("ui.home.col.challenge")}</span>
              <span>{t("ui.home.col.lesson")}</span>
              <span>{t("ui.home.col.area")}</span>
              <span>{t("ui.home.col.requirements")}</span>
              <span>{t("ui.home.col.status")}</span>
            </div>
            <ul className="flex flex-col gap-2 md:gap-0">
              {band.challenges.map((challenge) => {
                index += 1;
                const state = passed.has(challenge.slug) ? "passed" : started.has(challenge.slug) ? "started" : null;
                return (
                  <li key={challenge.slug} className="rounded-card border border-line bg-surface p-4 md:grid md:grid-cols-[48px_2.2fr_3fr_1fr_0.8fr_1fr] md:items-start md:gap-4 md:rounded-none md:border-x-0 md:border-b-0 md:bg-transparent md:px-0 md:py-4 md:hover:bg-surface-2">
                    <span className="font-mono text-code text-ink-3">{String(index).padStart(2, "0")}</span>
                    <div className="flex items-start justify-between gap-2 md:contents">
                      <Link to={`/c/${challenge.slug}`} className="text-body font-semibold text-ink no-underline hover:text-accent md:order-none">
                        {t(challenge.title)}
                      </Link>
                      <span className="md:hidden">{state && <Chip variant={state}>{t(state === "passed" ? "ui.home.passed" : "ui.home.attemptStarted")}</Chip>}</span>
                    </div>
                    <span className="mt-1.5 block text-small text-ink-2 md:mt-0">{t(`challenge.${challenge.slug}.lesson`)}</span>
                    <span className="mt-2 block font-mono text-meta text-ink-3 md:mt-0 md:font-sans md:text-small md:text-ink">
                      {t(`ui.area.${challenge.topic}`)}
                      <span className="md:hidden"> · {t("ui.challenge.requirementCount", { count: challenge.requirementCount })}</span>
                    </span>
                    <span className="hidden font-mono text-code md:block">{t("ui.challenge.requirementCount", { count: challenge.requirementCount })}</span>
                    <span className="hidden md:block">{state ? <Chip variant={state}>{t(state === "passed" ? "ui.home.passed" : "ui.home.attemptStarted")}</Chip> : <span className="font-mono text-meta text-ink-3">—</span>}</span>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </section>

      <section className="page pt-12 md:pt-16">
        <div className="flex flex-col gap-2.5 rounded-card border border-line bg-surface p-5 md:p-6">
          <h2 className="text-[22px] tracking-[-0.015em]">{t("ui.home.work")}</h2>
          <p className="text-small text-ink-2">{t("ui.home.workLead")}</p>
          {work.length === 0 ? (
            <div className="flex flex-col items-start gap-3 pt-2">
              <p className="text-small text-ink-2">{t("web.challenges.work.empty")}</p>
              <Button size="sm" to="/c/single-point-of-failure">
                {t("web.nav.start")}
              </Button>
            </div>
          ) : (
            <ul className="hairline-rows mt-2 flex flex-col">
              {work.map((entry) => (
                <li key={entry.editToken} className="flex items-center justify-between gap-3 py-3">
                  <span className="flex flex-col gap-0.5">
                    <span className="text-[15px] font-semibold">{entry.challengeSlug ? t(`challenge.${entry.challengeSlug}.title`) : entry.name}</span>
                    {entry.score !== null && <span className="font-mono text-meta text-ink-3">{entry.score} / 100</span>}
                  </span>
                  <Link to={editorPath(entry)} className="inline-flex items-center gap-1.5 text-small font-semibold text-accent no-underline hover:text-ink">
                    {t("ui.home.workContinue")}
                    <ArrowIcon size={16} />
                  </Link>
                </li>
              ))}
            </ul>
          )}
          {gains.length > 0 && (
            <div className="mt-3 flex flex-col gap-2.5 border-t border-line pt-4">
              <div className="flex flex-wrap items-baseline gap-x-3.5 gap-y-1">
                <h3 className="text-[17px] tracking-[-0.01em]">{t("ui.home.learned")}</h3>
                <p className="text-small text-ink-2">{t("ui.home.learnedLead")}</p>
              </div>
              <ul className="flex flex-wrap gap-2">
                {gains.map((gain) => (
                  <li key={gain.area}>
                    <Chip variant="passed" className="num">
                      {t("ui.home.learnedGain", { area: t(`ui.area.${gain.area}`), from: gain.from, to: gain.to })}
                    </Chip>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      </section>

      <section className="page pt-10 pb-12 md:pt-14 md:pb-20">
        <div className="flex flex-col gap-3 border-t border-line pt-6 md:flex-row md:items-center md:gap-5 md:pt-8">
          <span className="text-[17px]">{t("web.challenges.scratch.lead")}</span>
          <Button variant="ghost" size="md" onClick={startFromScratch} loading={creating} loadingLabel={t("ui.home.creating")}>
            {t("web.challenges.scratch.cta")}
          </Button>
          {createError && <InlineError>{createError}</InlineError>}
        </div>
        <Callout label={t("web.challenge.narrow.label")} compact className="mt-4 lg:hidden">
          <p className="text-small">{t("web.challenges.scratch.narrow")}</p>
        </Callout>
      </section>
    </PageShell>
  );
}
