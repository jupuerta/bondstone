import { useMemo, useState } from "react";
import { useLanguage, useT, type Translator } from "../../../i18n";
import { relativeTime } from "../../../i18n/relativeTime";
import type { ArchitectureSnapshot } from "../../../types/architecture";
import { PLOT_HEIGHT, buildChart, yOf, type PlotPoint } from "./scoreHistoryChart";

interface ScoreHistoryProps {
  history: ArchitectureSnapshot[];
  olderScoring: number;
}

export function ScoreHistory({ history, olderScoring }: ScoreHistoryProps) {
  const t = useT();
  const { locale } = useLanguage();
  const [hovered, setHovered] = useState<number | null>(null);
  const [pinned, setPinned] = useState<number | null>(null);
  const chart = useMemo(() => buildChart(history), [history]);

  if (chart === null) return <p className="mt-2.5 border-t border-panel-line pt-2.5 text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.history.perChange")}</p>;

  const focused = hovered ?? pinned;
  const reading = chart.points[focused ?? chart.points.length - 1];
  const previous = chart.points[(focused ?? chart.points.length - 1) - 1];

  return (
    <div className="mt-2.5 border-t border-panel-line pt-2.5">
      <div className="flex items-center justify-between gap-2">
        <span className="eyebrow text-panel-ink-3">{t("ui.history.title")}</span>
        {chart.total !== null && chart.total !== 0 && (
          <span className={`num text-[11px] ${chart.total > 0 ? "text-ok-panel" : "text-hot-panel"}`} title={t("ui.history.change")}>
            {chart.total > 0 ? `+${chart.total}` : chart.total}
          </span>
        )}
      </div>

      <div className="mt-2 flex gap-1.5">
        <div className="relative w-5 shrink-0" style={{ height: PLOT_HEIGHT }}>
          {[100, 50, 0].map((tick) => (
            <span key={tick} className="num absolute right-0 text-[8.5px] text-panel-ink-3" style={{ top: yOf(tick), transform: "translateY(-50%)" }}>
              {tick}
            </span>
          ))}
        </div>

        <div className="relative flex-1" style={{ height: PLOT_HEIGHT }}>
          <svg viewBox={`0 0 100 ${PLOT_HEIGHT}`} preserveAspectRatio="none" width="100%" height={PLOT_HEIGHT} className="absolute inset-0 overflow-visible" aria-hidden>
            {[100, 50, 0].map((tick) => (
              <line key={tick} x1="0" y1={yOf(tick)} x2="100" y2={yOf(tick)} stroke="var(--af-panel-line)" strokeWidth="1" vectorEffect="non-scaling-stroke" />
            ))}
            <path d={chart.area} fill="var(--af-accent-panel)" opacity="0.14" />
            <path d={chart.line} fill="none" stroke="var(--af-accent-panel)" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" vectorEffect="non-scaling-stroke" />
          </svg>

          {focused !== null && (
            <span className="absolute w-px bg-panel-line-2" style={{ left: `${chart.points[focused].x}%`, top: 6, bottom: 4, transform: "translateX(-50%)" }} />
          )}

          {chart.points.map((point, index) => (
            <Dot key={point.entry.version} point={point} live={index === chart.points.length - 1} on={index === focused} />
          ))}

          {chart.points.map((point, index) => (
            <button
              key={point.entry.version}
              type="button"
              onMouseEnter={() => setHovered(index)}
              onMouseLeave={() => setHovered(null)}
              onFocus={() => setHovered(index)}
              onBlur={() => setHovered(null)}
              onClick={() => setPinned((current) => (current === index ? null : index))}
              aria-label={t("ui.history.point", { version: point.entry.version, score: point.entry.score ?? 0 })}
              className="absolute top-0 bottom-0 w-10 -translate-x-1/2"
              style={{ left: `${point.x}%` }}
            />
          ))}
        </div>
      </div>

      <div className="mt-1 flex gap-1.5">
        <span className="w-5 shrink-0" />
        <div className="relative h-3 flex-1">
          {chart.points.map((point, index) => (
            <span
              key={point.entry.version}
              className={`num absolute -translate-x-1/2 text-[8.5px] ${index === focused || index === chart.points.length - 1 ? "text-accent-panel" : "text-panel-ink-3"}`}
              style={{ left: `${point.x}%` }}
            >
              {t("ui.history.version", { version: point.entry.version })}
            </span>
          ))}
        </div>
      </div>

      <Milestone entry={reading.entry} previous={previous?.entry ?? null} locale={locale} t={t} />

      <p className="mt-2 text-[10.5px] leading-[1.5] text-panel-ink-3">{t("ui.history.perChange")}</p>
      {olderScoring > 0 && <p className="mt-0.5 text-[10.5px] leading-[1.5] text-panel-ink-3">{t("ui.history.olderScoring", { count: olderScoring })}</p>}
    </div>
  );
}

function Milestone({ entry, previous, locale, t }: { entry: ArchitectureSnapshot; previous: ArchitectureSnapshot | null; locale: string; t: Translator }) {
  const delta = previous && entry.score !== null && previous.score !== null ? entry.score - previous.score : null;

  return (
    <div className="mt-2 border-t border-panel-line pt-2">
      <div className="flex items-baseline gap-1.5">
        <span className="num text-[10px] text-panel-ink-2">{t("ui.history.version", { version: entry.version })}</span>
        <span className="text-[10.5px] text-panel-ink-3">· {relativeTime(Date.parse(entry.createdAt), locale)}</span>
        {delta !== null && delta !== 0 && (
          <span className={`num text-[10px] ${delta > 0 ? "text-ok-panel" : "text-hot-panel"}`}>{delta > 0 ? `+${delta}` : delta}</span>
        )}
        <span className="num ml-auto text-[19px] leading-none font-semibold text-panel-ink">{entry.score ?? "—"}</span>
        <span className="font-mono text-[10px] text-panel-ink-3">{t("ui.analysis.outOf")}</span>
      </div>

      {entry.summary && (
        <div className="mt-1.5 flex flex-wrap items-center gap-x-2.5 gap-y-1">
          <Count tone="var(--af-hot-panel)" label={t("ui.history.criticals", { count: entry.summary.critical })} />
          <Count tone="var(--af-warm-panel)" label={t("ui.history.warnings", { count: entry.summary.warning })} />
          <Count tone="var(--af-ok-panel)" label={t("ui.history.passed", { count: entry.summary.passed })} />
        </div>
      )}
    </div>
  );
}

function Count({ tone, label }: { tone: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-[10.5px] text-panel-ink-2">
      <span aria-hidden className="size-1.5 shrink-0 rounded-full" style={{ background: tone }} />
      {label}
    </span>
  );
}

function Dot({ point, live, on }: { point: PlotPoint; live: boolean; on: boolean }) {
  if (point.entry.score === null) return null;

  const size = live ? 12 : on ? 9 : 7;

  return (
    <span
      aria-hidden
      className="absolute rounded-full bg-accent-panel"
      style={{
        left: `${point.x}%`,
        top: point.y,
        width: size,
        height: size,
        transform: "translate(-50%, -50%)",
        boxShadow: on ? "0 0 0 3px color-mix(in oklab, var(--af-accent-panel) 26%, transparent)" : live ? "0 0 0 2px var(--af-panel-2)" : undefined,
      }}
    />
  );
}
