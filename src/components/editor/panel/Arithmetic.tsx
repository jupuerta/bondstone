import { useState } from "react";
import { useT } from "../../../i18n";
import { AREA_ORDER, type AnalysisResult, type AreaScore, type FindingElement, type PenaltyLine, type Severity } from "../../../types/architecture";
import type { IdentifiedFinding } from "./FindingRow";
import { MIN_AREAS_TO_SCORE, areaFormula, averageFormula, belowScoreThreshold, elementsOf, penaltyBar } from "./scoreExplain";

interface ArithmeticProps {
  result: AnalysisResult;
  findings: IdentifiedFinding[];
  onPreview: (elements: FindingElement[] | null) => void;
  onPin: (findingId: string, elements: FindingElement[]) => void;
  pinnedId: string | null;
}

export function Arithmetic({ result, findings, onPreview, onPin, pinnedId }: ArithmeticProps) {
  const t = useT();
  const average = averageFormula(result, t);
  const below = belowScoreThreshold(result);

  return (
    <div className="mt-2.5 flex flex-col gap-2.5 border-t border-panel-line pt-2.5">
      <p className="text-[11px] leading-[1.5] text-panel-ink-3">
        {t("ui.analysis.formula.legend")}
        {result.score !== null && ` ${t("ui.analysis.formula.legendAverage")}`}
      </p>

      {average && (
        <div>
          <p className="font-mono text-[11px] leading-[1.5] text-panel-ink-2">{average.formula}</p>
          {average.excluded > 0 && <p className="mt-0.5 text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.analysis.formula.excluded", { count: average.excluded })}</p>}
        </div>
      )}

      {!average && below !== null && <p className="text-[11px] leading-[1.5] text-panel-ink-2">{t("ui.analysis.formula.belowThreshold", { scored: below, min: MIN_AREAS_TO_SCORE })}</p>}

      <div className="flex flex-col gap-2">
        {AREA_ORDER.map((name) => {
          const area = result.areas.find((candidate) => candidate.area === name);
          if (!area) return null;
          return <AreaLine key={name} area={area} findings={findings.filter((finding) => finding.area === name)} onPreview={onPreview} onPin={onPin} pinnedId={pinnedId} />;
        })}
      </div>
    </div>
  );
}

const SEVERITY_TONE: Record<Severity, string> = {
  Critical: "var(--af-hot-panel)",
  Warning: "var(--af-warm-panel)",
  Info: "var(--af-panel-ink-3)",
  Good: "var(--af-ok-panel)",
};

function areaTone(score: number): string {
  return score >= 90 ? "var(--af-ok-panel)" : score >= 70 ? "var(--af-warm-panel)" : "var(--af-hot-panel)";
}

interface AreaLineProps {
  area: AreaScore;
  findings: IdentifiedFinding[];
  onPreview: (elements: FindingElement[] | null) => void;
  onPin: (findingId: string, elements: FindingElement[]) => void;
  pinnedId: string | null;
}

function AreaLine({ area, findings, onPreview, onPin, pinnedId }: AreaLineProps) {
  const t = useT();
  const [hovered, setHovered] = useState<string | null>(null);
  const bar = penaltyBar(area);
  const formula = areaFormula(area, t);
  const scored = area.state === "Scored" && area.score !== null;

  const forCode = (code: string) => findings.filter((finding) => finding.code === code);
  const enter = (line: PenaltyLine) => {
    setHovered(line.code);
    const charged = forCode(line.code);
    if (charged.length > 0) onPreview(elementsOf(charged));
  };
  const leave = () => {
    setHovered(null);
    onPreview(null);
  };
  const press = (line: PenaltyLine) => {
    const charged = forCode(line.code);
    if (charged.length === 0) return;
    onPin(charged[0].findingId, elementsOf(charged));
  };

  return (
    <div>
      <div className="flex items-center gap-2">
        <span className="w-[92px] shrink-0 truncate text-[12px] text-panel-ink-2">{t(`ui.area.${area.area}`)}</span>
        {scored ? (
          <>
            <span className="h-1.5 min-w-6 flex-1 overflow-hidden rounded-full bg-wash/10">
              <span className="block h-full rounded-full" style={{ width: `${area.score}%`, background: areaTone(area.score!) }} />
            </span>
            <span className="num w-5 shrink-0 text-right text-[11px] text-panel-ink-2">{area.score}</span>
          </>
        ) : (
          <span className="flex-1 text-[11px] text-panel-ink-3">— {t(area.state === "NoRules" ? "ui.area.noRules" : "ui.area.nothingToCheck")}</span>
        )}
      </div>

      {bar && (
        <>
          <span className="relative mt-1.5 block h-2.5 overflow-hidden rounded-[5px] bg-wash/10" aria-hidden>
            <span className="absolute inset-y-0 left-0" style={{ width: `${bar.remaining}%`, background: areaTone(area.score ?? 0) }} />
            {bar.segments.map(({ line, start, width }) => (
              <button
                key={line.code}
                type="button"
                tabIndex={-1}
                disabled={forCode(line.code).length === 0}
                title={t("ui.analysis.formula.term", { code: line.code, count: line.findings })}
                onMouseEnter={() => enter(line)}
                onMouseLeave={leave}
                onClick={() => press(line)}
                className="absolute inset-y-0 block"
                style={{
                  left: `${start}%`,
                  width: `${width}%`,
                  background: line.capped ? stripes(SEVERITY_TONE[line.severity]) : SEVERITY_TONE[line.severity],
                  borderRight: line.capped ? "2px solid var(--af-panel)" : "1px solid var(--af-panel)",
                  opacity: hovered !== null && hovered !== line.code ? 0.4 : 1,
                }}
              />
            ))}
            {bar.overshoot > 0 && <span className="absolute inset-y-0 w-px bg-panel-ink" style={{ left: `${bar.floor}%` }} />}
          </span>

          <div className="mt-1.5 flex flex-wrap gap-1">
            {bar.segments.map(({ line }) => {
              const charged = forCode(line.code);
              const pinned = charged.some((finding) => finding.findingId === pinnedId);
              const text =
                line.findings > 1
                  ? t("ui.analysis.formula.penaltyRepeated", { penalty: line.penalty, code: line.code, count: line.findings })
                  : t("ui.analysis.formula.penalty", { penalty: line.penalty, code: line.code });
              const chip = `inline-flex h-[22px] items-center gap-1.5 rounded-pill px-2 font-mono text-[10.5px] whitespace-nowrap`;
              const tone = line.severity === "Critical" ? "text-hot-panel border-[color-mix(in_oklab,var(--af-hot-panel)_45%,transparent)]" : line.severity === "Warning" ? "text-warm-panel border-[color-mix(in_oklab,var(--af-warm-panel)_45%,transparent)]" : "text-panel-ink-2 border-panel-line";

              if (charged.length === 0) {
                return (
                  <span key={line.code} className={`${chip} border ${tone}`}>
                    {line.capped && <CappedMark />}
                    {text}
                  </span>
                );
              }

              return (
                <button
                  key={line.code}
                  type="button"
                  aria-pressed={pinned}
                  title={t("ui.analysis.formula.term", { code: line.code, count: line.findings })}
                  onMouseEnter={() => enter(line)}
                  onMouseLeave={leave}
                  onFocus={() => enter(line)}
                  onBlur={leave}
                  onClick={() => press(line)}
                  className={`${chip} border ${tone} ${pinned ? "bg-wash/12" : hovered === line.code ? "bg-wash/8" : ""}`}
                >
                  {line.capped && <CappedMark />}
                  {text}
                </button>
              );
            })}
          </div>

          {bar.segments
            .filter(({ line }) => line.capped)
            .map(({ line }) => (
              <p key={line.code} className="mt-1 text-[10.5px] leading-[1.5] text-panel-ink-3">
                {t("ui.analysis.formula.cappedTerm", { code: line.code, penalty: line.penalty })}
              </p>
            ))}

          {bar.overshoot > 0 && (
            <p className="mt-1 text-[10.5px] leading-[1.5] text-panel-ink-3">{t("ui.analysis.formula.pastZero", { subtracted: 100 + bar.overshoot, overshoot: bar.overshoot })}</p>
          )}
        </>
      )}

      {formula && <p className="mt-1 font-mono text-[10.5px] leading-[1.5] text-panel-ink-3">{formula}</p>}
    </div>
  );
}

function CappedMark() {
  return <span aria-hidden className="size-2 shrink-0 rounded-[2px] border border-current" style={{ background: stripes("currentColor", 2) }} />;
}

function stripes(tone: string, band = 4): string {
  return `repeating-linear-gradient(135deg, ${tone} 0 ${band}px, color-mix(in oklab, ${tone} 42%, var(--af-panel)) ${band}px ${band + 3}px)`;
}
