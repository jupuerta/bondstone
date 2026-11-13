import type { LoadCurveStatus } from "../../../hooks/useLoadCurve";
import { useLanguage } from "../../../i18n";
import { applyFormat } from "../../../i18n/format";
import type { LoadCurve } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { SATURATION } from "./loadMeter";
import { OVER_CAPACITY, SPARK, bottleneckOf, breakIndex, breakReading, clampIndex, latencies, lessonText, lessons, pointAt, scaleAt, spark, sweepable, type Spark } from "./trafficDial";

export interface TrafficDialProps {
  status: LoadCurveStatus;
  curve: LoadCurve | null;
  error: string | null;
  index: number;
  scale: number;
  nameOf: (nodeId: string) => string;
  onIndexChange: (index: number) => void;
  onClose: () => void;
  onRetry: () => void;
}

export function TrafficDial({ status, curve, error, index, nameOf, onIndexChange, onClose, onRetry, narrow }: TrafficDialProps & { narrow: boolean }) {
  const { locale, t } = useLanguage();
  const at = clampIndex(curve, index);
  const scale = scaleAt(curve, at);
  const point = pointAt(curve, at);
  const steps = Math.max((curve?.points.length ?? 1) - 1, 1);
  const hypothetical = scale > 1;
  const broken = breakReading(curve);
  const bottleneck = bottleneckOf(curve);
  const breakAt = breakIndex(curve);
  const latencySpark = spark(latencies(curve), at, { mark: breakAt });
  const bottleneckSpark = bottleneck === null ? null : spark(bottleneck.utilizations, at, { guide: SATURATION, cap: OVER_CAPACITY, mark: breakAt });

  return (
    <div className="flex flex-col gap-2.5">
      <div className="flex items-center justify-between gap-3">
        <span className="eyebrow text-panel-ink-3">{t("ui.dial.title")}</span>
        <div className="flex items-center gap-2">
          {status === "loading" && <span aria-hidden className="spin size-3 rounded-full border-2 border-panel-line border-t-accent-panel" />}
          <button type="button" onClick={onClose} aria-label={t("ui.dial.close")} title={t("ui.dial.close")} className={`inline-flex items-center justify-center rounded-pill text-panel-ink-3 hover:text-panel-ink ${narrow ? "size-11" : "size-7"}`}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden>
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1.5">
        {curve && <span className="num shrink-0 text-[10px] text-panel-ink-3">{t("ui.dial.scale", { scale: 1 })}</span>}
        <span className="relative flex min-w-32 flex-1 items-center">
          <input
            type="range"
            min={0}
            max={steps}
            step={1}
            value={at}
            onChange={(event) => onIndexChange(Number(event.target.value))}
            disabled={!sweepable(curve)}
            aria-label={t("ui.dial.rangeLabel")}
            aria-valuetext={t("ui.dial.scale", { scale })}
            className="af-dial w-full"
          />
          {breakAt !== null && breakAt >= 0 && (
            <span aria-hidden className="pointer-events-none absolute top-1/2 h-[11px] w-px -translate-y-1/2 bg-hot-panel" style={{ left: `calc(6.5px + ${(breakAt / steps).toFixed(4)} * (100% - 13px))` }} />
          )}
        </span>
        {curve && <span className="num shrink-0 text-[10px] text-panel-ink-3">{t("ui.dial.scale", { scale: scaleAt(curve, steps) })}</span>}
        <span className={`num inline-flex h-[22px] shrink-0 items-center rounded-pill border px-2 text-[11px] ${hypothetical ? "border-[color-mix(in_oklab,var(--af-accent-panel)_45%,var(--af-panel))] bg-[color-mix(in_oklab,var(--af-accent-panel)_18%,var(--af-panel))] text-panel-ink" : "border-panel-line text-panel-ink-2"}`}>
          {t("ui.dial.scale", { scale })}
        </span>
        {point && <span className="num shrink-0 text-[10.5px] text-panel-ink-2">{t("ui.dial.entering", { value: point.enteringRps })}</span>}
      </div>

      {status === "error" && (
        <div className="flex items-center justify-between gap-2 rounded-card border border-[color-mix(in_oklab,var(--af-hot-panel)_40%,transparent)] px-2.5 py-1.5">
          <span className="text-[11px] leading-[1.45] text-hot-panel">{error ?? t("ui.dial.failed")}</span>
          <Button variant="panel" size="sm" onClick={onRetry} className="min-h-8 shrink-0 px-3 text-[12px]">
            {t("ui.editor.retry")}
          </Button>
        </div>
      )}

      {curve && !sweepable(curve) && <p className="text-[12px] leading-[1.45] text-panel-ink-3">{t("ui.dial.idle")}</p>}

      {curve && sweepable(curve) && (
        <div className={`flex flex-col gap-1.5 ${status === "loading" ? "opacity-60" : ""}`}>
          <p className="text-[12px] leading-[1.45] text-panel-ink">
            {broken.kind === "at" && (
              <>
                <span className="font-semibold">{t("ui.dial.break.headline", { rps: broken.enteringRps, name: nameOf(broken.nodeId) })}</span>
                <span className="text-panel-ink-3">{` · ${t("ui.dial.break.detail", { scale: broken.scale, utilization: broken.utilization })}`}</span>
              </>
            )}
            {broken.kind === "already" && <span className="text-warm-panel">{t("ui.dial.break.already", { name: nameOf(broken.nodeId), utilization: broken.utilization, rps: broken.enteringRps })}</span>}
            {broken.kind === "none" && <span className="text-panel-ink-2">{t("ui.dial.break.none")}</span>}
          </p>

          <p className={`text-[11px] leading-[1.45] ${hypothetical ? "text-accent-panel" : "text-panel-ink-3"}`}>{hypothetical ? t("ui.dial.hypothetical", { scale }) : t("ui.dial.resting")}</p>

          {!narrow && (latencySpark !== null || bottleneckSpark !== null) && (
            <div className="mt-1 flex flex-wrap items-start gap-x-4 gap-y-2">
              {latencySpark && point && <Plot label={t("ui.dial.latency")} spark={latencySpark} now={applyFormat(point.nominalLatencyMs, "durationPrecise", locale)} peak={applyFormat(latencySpark.ceiling, "durationPrecise", locale)} />}
              {bottleneckSpark && bottleneck && (
                <Plot label={t("ui.dial.bottleneck", { name: nameOf(bottleneck.nodeId) })} spark={bottleneckSpark} now={applyFormat(bottleneck.utilizations[at], "percent", locale)} peak={applyFormat(Math.max(...bottleneck.utilizations), "percent", locale)} />
              )}
              <ul className="flex min-w-40 flex-1 flex-col gap-1">
                {lessons(curve).map((lesson) => (
                  <li key={lesson.kind} className="text-[11px] leading-[1.45] text-panel-ink-2">
                    {t(lessonText(lesson))}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function Plot({ label, spark, now, peak }: { label: string; spark: Spark; now: string; peak: string }) {
  const { t } = useLanguage();

  return (
    <div className="shrink-0" style={{ width: SPARK.width }} title={label}>
      <span className="eyebrow block truncate text-panel-ink-3">{label}</span>
      <svg viewBox={`0 0 ${SPARK.width} ${SPARK.height}`} width={SPARK.width} height={SPARK.height} className="mt-0.5 block" aria-hidden>
        <path d={spark.area} fill="color-mix(in oklab, var(--af-accent-panel) 14%, transparent)" />
        {spark.mark !== null && <line x1={spark.mark} x2={spark.mark} y1={SPARK.top - 4} y2={SPARK.height - SPARK.bottom} stroke="var(--af-hot-panel)" strokeWidth={1} />}
        <path d={spark.line} fill="none" stroke="var(--af-accent-panel)" strokeWidth={1.5} strokeLinejoin="round" />
        {spark.guide !== null && <line x1={0} x2={SPARK.width} y1={spark.guide} y2={spark.guide} stroke="var(--af-warm-panel)" strokeWidth={1} strokeDasharray="3 3" />}
        <circle cx={spark.dot.x} cy={spark.dot.y} r={2.6} fill="var(--af-accent-panel)" />
      </svg>
      <span className="num mt-0.5 flex items-baseline justify-between gap-2 text-[10px]">
        <span className="text-panel-ink">{now}</span>
        <span className="text-panel-ink-3">{t("ui.dial.peak", { value: peak })}</span>
      </span>
    </div>
  );
}
