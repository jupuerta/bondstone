import { useT } from "../../../i18n";
import type { AnalysisResult } from "../../../types/architecture";
import { FindingCard, pickFinding } from "../../engine/FindingCard";
import { DOCS_WINDOW } from "./docsWindow";

interface MotorSummaryProps {
  analysis: AnalysisResult;
  shareToken: string;
  faultIds: ReadonlySet<string>;
}

export function MotorSummary({ analysis, shareToken, faultIds }: MotorSummaryProps) {
  const t = useT();
  const finding = pickFinding(analysis.findings, new Set(faultIds));
  const scored = analysis.areas.filter((area) => area.state === "Scored").length;
  const problems = analysis.summary.critical + analysis.summary.warning;

  return (
    <div className="flex flex-col gap-3">
      <div className="rounded-card border border-panel-line bg-panel-2 p-3">
        <span className="eyebrow text-panel-ink-3">{t("ui.analysis.score")}</span>
        <div className="mt-1.5 flex items-baseline gap-2.5">
          <span className="num text-figure font-semibold text-panel-ink">{analysis.score ?? "—"}</span>
          <span className="text-small text-panel-ink-2">
            {scored === analysis.areas.length ? t("ui.analysis.overAllAreas", { count: scored }) : t("ui.analysis.overSomeAreas", { scored, total: analysis.areas.length })}
          </span>
        </div>
        {problems > 0 && (
          <p className="mt-2 text-small text-panel-ink">
            {analysis.summary.critical > 0
              ? t("ui.analysis.problemsLeftCritical", { count: problems, criticals: analysis.summary.critical })
              : t("ui.analysis.problemsLeft", { count: problems })}
          </p>
        )}
      </div>
      {finding ? <FindingCard finding={finding} shareToken={shareToken} /> : <p className="text-small text-panel-ink-2">{t("web.engine.noFinding")}</p>}
      <a href={`/rules/${encodeURIComponent(shareToken)}`} target={DOCS_WINDOW} className="text-small text-accent-panel underline underline-offset-4 hover:text-panel-ink">
        {t("ui.analysis.allRules")}
      </a>
    </div>
  );
}
