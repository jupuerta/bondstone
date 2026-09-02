import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ApiError, NotFoundError } from "../api/architectureClient";
import { getChallenge, listChallenges, startChallenge } from "../api/challengeClient";
import { PageShell } from "../components/layout/PageShell";
import { Button } from "../components/ui/Button";
import { Callout } from "../components/ui/Callout";
import { Chip } from "../components/ui/Chip";
import { CheckCircleIcon } from "../components/ui/Icons";
import { InlineError } from "../components/ui/InlineError";
import { Skeleton } from "../components/ui/Skeleton";
import { bandLabelKey, positionInBand } from "../domain/challengeBands";
import { attemptsOf, rememberWork, type SavedWork } from "../domain/savedWork";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";
import type { ChallengeDetail, ChallengeSummary } from "../types/architecture";

export function ChallengePage() {
  const t = useT();
  const navigate = useNavigate();
  const { slug = "" } = useParams<{ slug: string }>();
  const [challenge, setChallenge] = useState<ChallengeDetail | null>(null);
  const [all, setAll] = useState<ChallengeSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [attempts] = useState<SavedWork[]>(() => attemptsOf(slug));
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);
  usePageTitle(challenge ? t(challenge.title) : null);

  useEffect(() => {
    let cancelled = false;
    setChallenge(null);
    setError(null);
    Promise.all([getChallenge(slug), listChallenges().catch(() => [] as ChallengeSummary[])])
      .then(([detail, list]) => {
        if (cancelled) return;
        setChallenge(detail);
        setAll(list);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setError(err instanceof NotFoundError ? t("ui.challenge.notFound") : err instanceof ApiError ? t(err.key, err.args) : t("ui.challenge.startFailed"));
      });
    return () => {
      cancelled = true;
    };
  }, [slug, t]);

  const start = () => {
    if (!challenge) return;
    setStarting(true);
    setStartError(null);
    startChallenge(slug, t(challenge.title))
      .then((architecture) => {
        rememberWork({ editToken: architecture.editToken, shareToken: architecture.shareToken, name: architecture.name, challengeSlug: slug });
        navigate(`/e/${encodeURIComponent(architecture.editToken)}`);
      })
      .catch((err: unknown) => {
        setStarting(false);
        setStartError(err instanceof NotFoundError ? t("ui.challenge.notFound") : err instanceof ApiError ? t(err.key, err.args) : t("ui.challenge.startFailed"));
      });
  };

  const position = challenge ? positionInBand(all, slug) : null;
  const [latest] = attempts;

  return (
    <PageShell>
      <div className="border-b border-line">
        <p className="page py-3 font-mono text-meta text-ink-2 md:text-code">{t("web.challenge.context")}</p>
      </div>

      <section className="page py-7 md:py-14">
        {error !== null && (
          <div className="flex flex-col items-start gap-3">
            <p className="text-body font-semibold">{error}</p>
            <Link to="/challenges" className="link">
              {t("ui.challenge.backToCatalogue")}
            </Link>
          </div>
        )}

        {challenge === null && error === null && (
          <div className="flex flex-col gap-4" aria-busy>
            <Skeleton width={180} height={28} />
            <Skeleton width="70%" height={48} />
            <Skeleton height={90} />
            <p className="font-mono text-label text-ink-3">{t("ui.loading")}</p>
          </div>
        )}

        {challenge !== null && (
          <div className="grid gap-8 md:grid-cols-[minmax(0,1.6fr)_420px] md:items-start md:gap-16">
            <div className="flex flex-col gap-6 md:gap-8">
              <div className="flex flex-col gap-4">
                <Chip className="self-start">
                  {position ? t("web.challenge.band", { band: t(bandLabelKey(challenge.difficulty)), index: position.index, total: position.total }) : t(bandLabelKey(challenge.difficulty))}
                </Chip>
                <h1 className="text-[40px] leading-none tracking-[-0.03em] md:text-page">{t(challenge.title)}</h1>
              </div>
              <p className="text-[17px] leading-normal md:text-lead">{t(challenge.story)}</p>
              <Callout label={t("web.challenge.teaches")} compact>
                <p className="text-body">{t(`challenge.${slug}.lesson`)}</p>
              </Callout>
              <div className="flex flex-col gap-2">
                <h2 className="mb-2 text-block">{t("web.challenge.requirements")}</h2>
                <ul className="hairline-rows border-b border-line">
                  {challenge.requirements.map((requirement, index) => (
                    <li key={index} className="flex items-start gap-3 py-3 text-body">
                      <CheckCircleIcon size={20} className="mt-0.5 shrink-0 text-ink-3" />
                      <span>{t(requirement.label)}</span>
                    </li>
                  ))}
                </ul>
                <p className="mt-2 text-small text-ink-2">{t("web.challenge.hints")}</p>
              </div>
            </div>

            <aside className="flex flex-col gap-3.5 rounded-card border border-line bg-surface p-5 md:p-6">
              <Button onClick={start} loading={starting} loadingLabel={t("ui.home.creating")} className="min-h-14 text-[17px]">
                {t("web.challenge.start")}
              </Button>
              <p className="text-small text-ink-2">{t("web.challenge.start.note")}</p>
              {startError && <InlineError>{startError}</InlineError>}
              <Callout label={t("web.challenge.narrow.label")} compact className="md:hidden">
                <p className="text-small">{t("web.challenge.narrow")}</p>
              </Callout>
              {latest && (
                <Callout label={t("web.challenge.resume.title")} compact>
                  <p className="text-small text-ink-2">{t("ui.challenge.resumeLead")}</p>
                  <Button variant="ghost" size="md" to={`/e/${encodeURIComponent(latest.editToken)}`}>
                    {t("ui.challenge.resume")}
                  </Button>
                  <button type="button" className="link self-start text-small" onClick={start}>
                    {t("ui.challenge.startOver")}
                  </button>
                  <p className="text-meta text-ink-3">{t("ui.challenge.startOverNote")}</p>
                </Callout>
              )}
              <div className="flex flex-wrap gap-x-4 gap-y-2 border-t border-line pt-3 text-small">
                <Link to="/challenges" className="link">
                  {t("ui.challenge.backToCatalogue")}
                </Link>
                <Link to="/" className="link">
                  {t("web.challenge.whatIsThis")}
                </Link>
              </div>
            </aside>
          </div>
        )}
      </section>
    </PageShell>
  );
}
