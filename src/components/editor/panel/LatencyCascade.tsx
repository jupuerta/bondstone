import { useMemo, useState } from "react";
import { useLanguage } from "../../../i18n";
import { applyFormat } from "../../../i18n/format";
import type { FindingElement, LatencyHop } from "../../../types/architecture";
import { buildWaterfall, hopFormula, hopNote, type SegmentKind, type WaterfallBudget, type WaterfallRow } from "../simulate/latencyWaterfall";
import { LINK } from "../simulate/tones";

interface LatencyCascadeProps {
  path: LatencyHop[] | undefined;
  budgetMs: number | null;
  nameOf: (element: FindingElement) => string;
  narrow: boolean;
}

const SEGMENT_TONE: Record<SegmentKind, string> = {
  network: "var(--af-accent-panel)",
  processing: "var(--af-panel-ink-3)",
  queueing: "var(--af-warm-panel)",
  injected: "var(--af-hot-panel)",
};

export function LatencyCascade({ path, budgetMs, nameOf, narrow }: LatencyCascadeProps) {
  const { t } = useLanguage();
  const [showArithmetic, setShowArithmetic] = useState(false);
  const waterfall = useMemo(() => buildWaterfall(path, budgetMs), [path, budgetMs]);
  if (!waterfall) return null;

  const ms = (value: number) => t("ui.impact.milliseconds", { value });
  const budget = waterfall.budget;

  return (
    <section className="flex flex-col gap-1.5 border-t border-panel-line pt-[11px]">
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-[14px] font-semibold text-panel-ink">{t("ui.impact.cascade.title")}</span>
        <span className="num shrink-0 text-[11px] text-panel-ink-3">{ms(waterfall.totalMs)}</span>
      </div>

      <div className="flex flex-col rounded-card border border-panel-line bg-panel-2 px-2.5 pt-0.5 pb-2.5">
        <ol>
          {waterfall.rows.map((row, index) => (
            <Hop key={`${row.hop.nodeId}:${index}`} row={row} budget={budget} first={index === 0} nameOf={nameOf} showArithmetic={showArithmetic} />
          ))}
        </ol>

        {budget && (
          <p className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px] leading-[1.45]" style={{ color: budget.exceeded ? "var(--af-hot-panel)" : "var(--af-ok-panel)" }}>
            <span className="inline-flex h-[20px] items-center rounded-pill border border-current px-2 font-mono text-[10.5px] uppercase tracking-[0.06em]">{t("ui.impact.cascade.budget", { value: budget.ms })}</span>
            <span>{budget.exceeded ? t("ui.impact.cascade.budget.over", { value: budget.overshootMs }) : t("ui.impact.cascade.budget.under", { value: budget.ms - waterfall.totalMs })}</span>
          </p>
        )}

        {waterfall.kinds.length > 0 && (
          <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1">
            {waterfall.kinds.map((kind) => (
              <li key={kind} className="flex items-center gap-[5px] font-mono text-[11px] text-panel-ink-3">
                <span aria-hidden className="size-[7px] shrink-0 rounded-full" style={{ background: SEGMENT_TONE[kind] }} />
                {t(`ui.impact.cascade.segment.${kind}`)}
              </li>
            ))}
          </ul>
        )}

        <button type="button" aria-expanded={showArithmetic} onClick={() => setShowArithmetic((current) => !current)} className={`${LINK} mt-2 ${narrow ? "min-h-11" : ""}`}>
          {t(showArithmetic ? "ui.impact.hideArithmetic" : "ui.impact.showArithmetic")}
        </button>

        {showArithmetic && (
          <div className="mt-2 flex flex-col gap-2 border-t border-panel-line pt-2">
            <p className="text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.impact.cascade.legend")}</p>
            {waterfall.kinds.length > 0 && (
              <ul className="flex flex-col gap-1">
                {waterfall.kinds.map((kind) => (
                  <li key={kind} className="text-[11px] leading-[1.5] text-panel-ink-3">
                    <span className="font-mono text-panel-ink-2">{t(`ui.impact.cascade.segment.${kind}`)}</span> {t(`ui.impact.cascade.segment.${kind}.why`)}
                  </li>
                ))}
              </ul>
            )}
            <pre className="overflow-x-auto font-mono text-[10px] leading-[1.6] text-panel-ink-2">{t("ui.impact.cascade.arithmetic")}</pre>
            <p className="text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.impact.cascade.failedCall")}</p>
            <p className="text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.impact.cascade.twoNumbers")}</p>
          </div>
        )}

        {waterfall.clipped && <p className="mt-2 text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.impact.cascade.offScale", { value: waterfall.axisMs })}</p>}
      </div>
    </section>
  );
}

interface HopProps {
  row: WaterfallRow;
  budget: WaterfallBudget | null;
  first: boolean;
  nameOf: (element: FindingElement) => string;
  showArithmetic: boolean;
}

function Hop({ row, budget, first, nameOf, showArithmetic }: HopProps) {
  const { locale, t } = useLanguage();
  const { hop } = row;
  const ms = (value: number) => t("ui.impact.milliseconds", { value });
  const note = hopNote(row);
  const formula = hopFormula(row, t);
  const failed = hop.outcome !== "Completed";
  const label = hop.connectionId ? nameOf({ type: "Connection", id: hop.connectionId }) : nameOf({ type: "Node", id: hop.nodeId });

  return (
    <li className={`py-2 ${first ? "" : "border-t border-panel-line"}`}>
      <div className="flex items-baseline justify-between gap-2">
        <span className="flex min-w-0 items-baseline gap-1.5">
          <span className="truncate text-[12px] text-panel-ink">{label}</span>
          {first && <span className="eyebrow shrink-0 text-panel-ink-3">{t("ui.impact.cascade.entryPoint")}</span>}
        </span>
        <span className="num shrink-0 text-[12px] text-panel-ink-2">{ms(hop.cumulativeMs)}</span>
      </div>

      {hop.queueingFactor > 1 && (
        <span className="mt-1 inline-flex h-[20px] items-center rounded-pill border border-[color-mix(in_oklab,var(--af-warm-panel)_60%,transparent)] px-2 font-mono text-[10.5px] text-warm-panel">{t("ui.impact.cascade.queueingFactor", { value: hop.queueingFactor })}</span>
      )}

      <div className="relative mt-[5px] h-[9px] overflow-hidden rounded-[3px] bg-wash/6">
        <div
          className="absolute inset-y-0 flex overflow-hidden rounded-[2px]"
          style={{
            left: `${row.start}%`,
            width: `${Math.max(row.end - row.start, 0)}%`,
            minWidth: 2,
            ...(row.clipped ? { clipPath: "polygon(0 0, calc(100% - 4px) 0, 100% 25%, calc(100% - 4px) 50%, 100% 75%, calc(100% - 4px) 100%, 0 100%)" } : {}),
          }}
        >
          {row.segments.length === 0 ? (
            <span className="h-full w-full" style={{ background: failed ? "var(--af-hot-panel)" : SEGMENT_TONE.processing }} title={`${label} · ${applyFormat(hop.totalMs, "duration", locale)}`} />
          ) : (
            row.segments.map((segment) => (
              <span key={segment.kind} className="h-full" style={{ width: `${segment.share * 100}%`, background: SEGMENT_TONE[segment.kind] }} title={`${t(`ui.impact.cascade.segment.${segment.kind}`)} · ${ms(segment.ms)}`} />
            ))
          )}
        </div>
        {budget && <span aria-hidden className="absolute -inset-y-0.5 w-px" style={{ left: `${budget.at}%`, background: budget.exceeded ? "var(--af-hot-panel)" : "var(--af-ok-panel)" }} />}
      </div>

      {note && <p className="mt-[5px] text-[11px] leading-[1.4] text-panel-ink-3">{t(note.key, { value: note.ms })}</p>}
      {hop.rawCallMs != null && <p className="mt-0.5 text-[11px] leading-[1.4] text-panel-ink-3">{t("ui.impact.cascade.raw", { value: hop.rawCallMs })}</p>}
      {showArithmetic && formula && <p className="mt-1 font-mono text-[10.5px] leading-[1.5] text-panel-ink-3">{formula}</p>}
    </li>
  );
}
