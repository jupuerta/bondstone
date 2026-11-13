import type { ReactNode } from "react";
import { useT } from "../../../i18n";
import { MIN_COMPONENTS_TO_LABEL, scoreBand, type ScoreBand } from "./scoreExplain";

const BAND_TONE: Record<ScoreBand, string> = {
  solid: "text-ok-panel",
  fair: "text-panel-ink-2",
  fragile: "text-warm-panel",
  serious: "text-hot-panel",
  unevaluated: "text-panel-ink-3",
};

export interface Disclosure {
  key: string;
  label: string;
  open: boolean;
  onToggle: () => void;
}

interface ScoreCardProps {
  score: number | null;
  firstScore: number | null;
  problems: number;
  criticals: number;
  areas: { scored: number; total: number };
  components: number;
  disclosures: Disclosure[];
  narrow: boolean;
  children?: ReactNode;
}

export function ScoreCard({ score, firstScore, problems, criticals, areas, components, disclosures, narrow, children }: ScoreCardProps) {
  const t = useT();
  const band = score === null ? "unevaluated" : scoreBand(score, areas.scored, components);
  const moved = score !== null && firstScore !== null && firstScore !== score;

  return (
    <div className="rounded-card border border-panel-line bg-panel-2 p-3">
      <div className="flex items-baseline gap-2.5">
        <span className="num text-figure font-semibold text-panel-ink">{score ?? "—"}</span>
        <span className="min-w-0 text-[13px] text-panel-ink-2">
          {score === null
            ? t("ui.analysis.outOf")
            : areas.scored === areas.total
              ? t("ui.analysis.overAllAreas", { count: areas.total })
              : t("ui.analysis.overSomeAreas", { scored: areas.scored, total: areas.total })}
        </span>
        <span className={`ml-auto inline-flex h-[26px] shrink-0 items-center gap-1.5 rounded-pill border border-panel-line px-2.5 font-mono text-[11px] uppercase tracking-[0.06em] ${BAND_TONE[band]}`}>
          <span aria-hidden className="size-[9px] shrink-0 rounded-full border-2 border-current" />
          {t(`ui.analysis.band.${band}`)}
        </span>
      </div>

      {score !== null && components < MIN_COMPONENTS_TO_LABEL && (
        <p className="mt-2 text-[12px] leading-[1.45] text-panel-ink-3">{t("ui.analysis.tooSmall", { count: components })}</p>
      )}

      <p className="mt-2 text-[13px] leading-[1.4] text-panel-ink">
        {score === null
          ? t("ui.analysis.notEnough")
          : problems === 0
            ? t("ui.analysis.nothingLeft")
            : criticals > 0
              ? t("ui.analysis.problemsLeftCritical", { count: problems, criticals })
              : t("ui.analysis.problemsLeft", { count: problems })}
      </p>

      {moved && (
        <p className="num mt-1.5 text-[12px] text-panel-ink-2">{t("ui.analysis.sinceStart", { first: firstScore!, score: score! })}</p>
      )}

      {disclosures.length > 0 && (
        <div className="mt-2 flex flex-wrap items-center gap-x-3.5 gap-y-1">
          {disclosures.map((disclosure) => (
            <button
              key={disclosure.key}
              type="button"
              aria-expanded={disclosure.open}
              onClick={disclosure.onToggle}
              className={`text-[12px] leading-[1.45] underline underline-offset-4 ${disclosure.open ? "text-panel-ink" : "text-accent-panel hover:text-panel-ink"} ${narrow ? "min-h-11" : ""}`}
            >
              {disclosure.label}
            </button>
          ))}
        </div>
      )}

      {children}
    </div>
  );
}
