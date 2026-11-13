import { useMemo, useState, type ReactNode } from "react";
import type { SimulationStatus } from "../../../hooks/useSimulation";
import { useLanguage, type Translator } from "../../../i18n";
import { applyFormat } from "../../../i18n/format";
import type { ElementState, FindingElement, HealthState, SimulationMetrics, SimulationResult } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { Skeleton } from "../../ui/Skeleton";
import { metricArithmetic, type ImpactMetric, type MetricArithmetic } from "../simulate/impactExplain";
import { MechanismChip } from "../simulate/MechanismChip";
import { frameIndexAt, propagationFrames, type TimelineFrame } from "../simulate/propagationTimeline";
import { containedStates, groupByRootCause, type CauseGroup } from "../simulate/rootCause";
import { LINK, blastTone } from "../simulate/tones";
import type { PropagationWave } from "../simulate/usePropagationWave";
import { Term } from "../help/Term";
import { LatencyCascade } from "./LatencyCascade";

interface ImpactPanelProps {
  status: SimulationStatus;
  result: SimulationResult | null;
  error: string | null;
  onRetry: () => void;
  previous: SimulationMetrics | null;
  nameOf: (element: FindingElement) => string;
  budgetMs: number | null;
  wave: PropagationWave;
  fragility?: ReactNode;
  narrow: boolean;
}

const STATE_TONE: Record<HealthState, string> = {
  Healthy: "var(--af-ok-panel)",
  Degraded: "var(--af-warm-panel)",
  Down: "var(--af-hot-panel)",
};

const SECTION = "flex flex-col gap-1.5 border-t border-panel-line pt-[11px]";
const TITLE = "text-[14px] font-semibold text-panel-ink";

export function ImpactPanel({ status, result, error, onRetry, previous, nameOf, budgetMs, wave, fragility, narrow }: ImpactPanelProps) {
  const { t } = useLanguage();

  if (status === "error" && !result) {
    return (
      <div className="flex flex-col items-start gap-3">
        <p className="text-[13px] font-semibold text-panel-ink">{t("ui.impact.failed")}</p>
        <p className="text-[12px] leading-[1.45] text-panel-ink-2">{t("editor.scenario.errorNote")}</p>
        {error && <p className="font-mono text-[11px] text-panel-ink-3">{error}</p>}
        <Button variant="panel" size="sm" onClick={onRetry}>
          {t("ui.editor.retry")}
        </Button>
      </div>
    );
  }

  if (!result) {
    if (status === "loading") {
      return (
        <div className="flex flex-col gap-3" aria-busy>
          <Skeleton height={96} dark />
          <Skeleton width="60%" dark />
          <Skeleton height={74} dark />
        </div>
      );
    }
    return <p className="text-[12px] leading-[1.45] text-panel-ink-3">{t("ui.impact.empty")}</p>;
  }

  return (
    <div className={`flex flex-col gap-[11px] ${status === "loading" ? "opacity-60" : ""}`} aria-busy={status === "loading"}>
      {status === "error" && (
        <div className="flex items-center justify-between gap-2">
          <p className="text-[12px] leading-[1.45] text-hot-panel">{error ?? t("ui.impact.failed")}</p>
          <Button variant="panel" size="sm" onClick={onRetry} className="min-h-8 shrink-0 px-3 text-[12px]">
            {t("ui.editor.retry")}
          </Button>
        </div>
      )}

      {result.discardedFaults.length > 0 && <p className="rounded-card border border-[color-mix(in_oklab,var(--af-warm-panel)_45%,transparent)] px-2.5 py-2 text-[11px] leading-[1.45] text-warm-panel">{t("ui.canvas.faultsDropped", { count: result.discardedFaults.length })}</p>}

      <MetricsCard metrics={result.metrics} previous={previous} nameOf={nameOf} narrow={narrow} />
      <LatencyCascade path={result.criticalPath} budgetMs={budgetMs} nameOf={nameOf} narrow={narrow} />
      <Timeline states={result.states} wave={wave} nameOf={nameOf} narrow={narrow} />
      <Propagation states={result.states} nameOf={nameOf} narrow={narrow} />
      <Containment states={result.states} nameOf={nameOf} />
      {fragility}
    </div>
  );
}

interface MetricsCardProps {
  metrics: SimulationMetrics;
  previous: SimulationMetrics | null;
  nameOf: (element: FindingElement) => string;
  narrow: boolean;
}

function MetricsCard({ metrics, previous, nameOf, narrow }: MetricsCardProps) {
  const { locale, t } = useLanguage();
  const [showArithmetic, setShowArithmetic] = useState(false);
  const arithmetic = (metric: ImpactMetric) => (showArithmetic ? metricArithmetic(metric, metrics, t, nameOf) : null);
  const percent = (value: number) => applyFormat(value, "percent", locale);
  const ms = (value: number | null) => (value === null ? "—" : t("ui.impact.milliseconds", { value }));

  return (
    <div className="rounded-card border border-panel-line bg-panel-2 p-3">
      <Headline label={t("ui.impact.blastRadius")} value={percent(metrics.blastRadius)} tone={blastTone(metrics.blastRadius)} delta={delta(metrics.blastRadius, previous?.blastRadius, percent)} />
      <Headline label={t("ui.impact.requestsLost")} value={percent(metrics.requestsAffected)} tone={blastTone(metrics.requestsAffected)} delta={delta(metrics.requestsAffected, previous?.requestsAffected, percent)} className="mt-1.5" />

      <button type="button" aria-expanded={showArithmetic} onClick={() => setShowArithmetic((current) => !current)} className={`${LINK} mt-2 ${narrow ? "min-h-11" : ""}`}>
        {t(showArithmetic ? "ui.impact.hideArithmetic" : "ui.impact.showArithmetic")}
      </button>

      {showArithmetic && (
        <div className="mt-2 flex flex-col gap-1">
          <p className="text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.impact.formula.legend")}</p>
          <ArithmeticLine label={t("ui.impact.blastRadius")} arithmetic={arithmetic("blastRadius")} />
          <ArithmeticLine label={t("ui.impact.requestsLost")} arithmetic={arithmetic("requestsAffected")} />
        </div>
      )}

      <dl className="mt-2.5 flex flex-col gap-1 border-t border-panel-line pt-2.5">
        <Row label={t("ui.impact.componentsDown")} value={t("ui.impact.outOf", { value: metrics.componentsDown, total: metrics.componentsTotal })} arithmetic={arithmetic("componentsDown")} note={delta(metrics.componentsDown, previous?.componentsDown, String)} />
        <Row label={t("ui.impact.degraded")} value={String(metrics.componentsDegraded)} arithmetic={arithmetic("componentsDegraded")} />
        <Row label={t("ui.impact.entryPointsHit")} value={t("ui.impact.outOf", { value: metrics.entryPointsAffected, total: metrics.entryPointsTotal })} arithmetic={arithmetic("entryPoints")} />
        <Row label={t("ui.impact.latency")} value={`${ms(metrics.nominalLatencyMs)} → ${ms(metrics.faultedLatencyMs)}`} arithmetic={arithmetic("latency")} note={metrics.faultedLatencyMs === null ? t("ui.impact.noRoute") : null} />
        <Row label={t("ui.impact.amplification")} value={t("ui.impact.times", { value: metrics.requestAmplification })} arithmetic={arithmetic("amplification")} note={metrics.requestAmplification > 1 ? t("ui.impact.worstPath") : null} />
        {metrics.loadAmplification > 1 && <Row label={<Term id="loadAmplification">{t("ui.impact.loadAmplification")}</Term>} value={t("ui.impact.times", { value: metrics.loadAmplification })} arithmetic={arithmetic("loadAmplification")} note={t("ui.impact.busiest")} />}
        {metrics.backlogRps > 0 && <Row label={t("ui.impact.backlog")} value={t("ui.impact.rps", { value: metrics.backlogRps })} arithmetic={arithmetic("backlog")} note={t("ui.impact.pilingUp")} />}
        {metrics.bottleneck && <Row label={t("ui.impact.bottleneck")} value={nameOf(metrics.bottleneck)} tone="var(--af-warm-panel)" arithmetic={arithmetic("bottleneck")} />}
      </dl>
    </div>
  );
}

function delta(current: number, before: number | undefined, format: (value: number) => string): { text: string; improved: boolean } | null {
  if (before === undefined || before === current) return null;
  return { text: format(before), improved: current < before };
}

interface HeadlineProps {
  label: string;
  value: string;
  tone: string;
  delta: { text: string; improved: boolean } | null;
  className?: string;
}

function Headline({ label, value, tone, delta: moved, className = "" }: HeadlineProps) {
  const { t } = useLanguage();
  return (
    <div className={`flex items-baseline justify-between gap-2 ${className}`}>
      <span className="min-w-0 text-[12px] leading-[1.45] text-panel-ink-2">{label}</span>
      <span className="flex shrink-0 flex-col items-end">
        <span className="num text-[26px] leading-none font-semibold tracking-[-0.02em]" style={{ color: tone }}>
          {value}
        </span>
        {moved && (
          <span className="num mt-1 text-[10px]" style={{ color: moved.improved ? "var(--af-ok-panel)" : "var(--af-hot-panel)" }}>
            {moved.improved ? "↓ " : "↑ "}
            {t("ui.impact.from", { value: moved.text })}
          </span>
        )}
      </span>
    </div>
  );
}

interface RowProps {
  label: ReactNode;
  value: string;
  tone?: string;
  arithmetic?: MetricArithmetic | null;
  note?: string | { text: string; improved: boolean } | null;
}

function Row({ label, value, tone, arithmetic, note }: RowProps) {
  const { t } = useLanguage();
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-x-3">
      <dt className="shrink-0 text-[12px] text-panel-ink-2">{label}</dt>
      <dd className="num shrink-0 text-right text-[12px]" style={{ color: tone ?? "var(--af-panel-ink)" }}>
        {value}
      </dd>
      {typeof note === "string" && <dd className="w-full text-right text-[10.5px] leading-[1.4] text-panel-ink-3">{note}</dd>}
      {note && typeof note !== "string" && (
        <dd className="num w-full text-right text-[10px]" style={{ color: note.improved ? "var(--af-ok-panel)" : "var(--af-hot-panel)" }}>
          {note.improved ? "↓ " : "↑ "}
          {t("ui.impact.from", { value: note.text })}
        </dd>
      )}
      {arithmetic && (
        <dd className="w-full">
          <ArithmeticLine arithmetic={arithmetic} />
        </dd>
      )}
    </div>
  );
}

function ArithmeticLine({ label, arithmetic }: { label?: string; arithmetic: MetricArithmetic | null }) {
  if (!arithmetic) return null;
  return (
    <p className="text-[10.5px] leading-[1.5] text-panel-ink-3">
      {label && <span className="eyebrow mr-1.5 text-panel-ink-3">{label}</span>}
      <span className="font-mono text-panel-ink-2">{arithmetic.formula}</span>
      <span className="ml-1.5">{arithmetic.why}</span>
    </p>
  );
}

interface TimelineProps {
  states: ElementState[];
  wave: PropagationWave;
  nameOf: (element: FindingElement) => string;
  narrow: boolean;
}

function Timeline({ states, wave, nameOf, narrow }: TimelineProps) {
  const { t } = useLanguage();
  const frames = useMemo(() => propagationFrames(states), [states]);
  const activeIndex = frameIndexAt(frames, wave.visibleStep);
  if (frames.length < 2) return null;
  const active = frames[activeIndex];

  return (
    <section className={SECTION}>
      <div className="flex items-baseline justify-between gap-2">
        <span className={TITLE}>{t("ui.impact.timeline.title")}</span>
        <span className="num shrink-0 text-[11px] text-panel-ink-3">{t("ui.impact.outOf", { value: activeIndex + 1, total: frames.length })}</span>
      </div>

      <div role="group" aria-label={t("ui.impact.timeline.title")} className="flex flex-wrap gap-1.5">
        {frames.map((frame, index) => (
          <Stop key={frame.step} frame={frame} reached={index <= activeIndex} active={index === activeIndex} label={stepLabel(frame, t)} onSelect={() => wave.freeze(frame.step)} narrow={narrow} />
        ))}
      </div>
      <p className="text-[12px] leading-[1.45] text-panel-ink-3">{t("ui.impact.timeline.legend")}</p>

      <div className="rounded-card border border-panel-line bg-panel-2 p-2.5">
        <span className="eyebrow text-panel-ink-3">{stepLabel(active, t)}</span>
        {active.contained && <p className="mt-1 text-[11px] leading-[1.45] text-warm-panel">{t("ui.impact.timeline.contained")}</p>}
        <ul className="mt-1.5 flex flex-col gap-2">
          {active.changed.map((element) => (
            <li key={`${element.type}:${element.id}`}>
              <span className="flex items-center gap-2">
                <span aria-hidden className="size-[7px] shrink-0 rounded-full" style={{ background: STATE_TONE[element.state] }} />
                <span className="min-w-0 truncate text-[12px] font-semibold text-panel-ink">{nameOf(element)}</span>
                {element.mechanism && <MechanismChip mechanism={element.mechanism} />}
              </span>
              <span className="block pl-4 text-[11px] leading-[1.45] text-panel-ink-2">{t(element.reason)}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

interface StopProps {
  frame: TimelineFrame;
  reached: boolean;
  active: boolean;
  label: string;
  onSelect: () => void;
  narrow: boolean;
}

function Stop({ frame, reached, active, label, onSelect, narrow }: StopProps) {
  const tone = frame.down > 0 ? STATE_TONE.Down : STATE_TONE.Degraded;
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={active}
      className={`inline-flex h-[26px] items-center gap-1.5 rounded-pill border px-2.5 font-mono text-[11px] uppercase tracking-[0.06em] whitespace-nowrap transition-[color,background-color,border-color] ${active ? "border-panel-ink bg-panel-ink font-semibold text-panel-on-ink" : reached ? "border-panel-line-2 text-panel-ink" : "border-panel-line text-panel-ink-2 hover:border-panel-line-2"} ${narrow ? "min-h-11" : ""}`}
    >
      <span aria-hidden className={`size-2 shrink-0 rounded-full ${frame.contained ? "border-2" : ""}`} style={frame.contained ? { borderColor: active ? "var(--af-panel)" : "var(--af-warm-panel)" } : { background: active ? "var(--af-panel)" : tone }} />
      {label}
    </button>
  );
}

function stepLabel(frame: TimelineFrame, t: Translator): string {
  return frame.step === 0 ? t("ui.impact.timeline.injected") : t("ui.impact.step", { step: frame.step });
}

interface PropagationProps {
  states: ElementState[];
  nameOf: (element: FindingElement) => string;
  narrow: boolean;
}

function Propagation({ states, nameOf, narrow }: PropagationProps) {
  const { t } = useLanguage();
  const groups = useMemo(() => groupByRootCause(states), [states]);

  return (
    <section className={SECTION}>
      <span className={TITLE}>{t("ui.impact.propagation")}</span>
      {groups.length === 0 ? (
        <p className="flex items-center gap-2 rounded-card border border-[color-mix(in_oklab,var(--af-ok-panel)_35%,transparent)] px-2.5 py-2 text-[12px] text-ok-panel">
          <span aria-hidden className="size-2 shrink-0 rounded-full border-2 border-current" />
          {t("ui.impact.allStanding")}
        </p>
      ) : (
        groups.map((group) => <Group key={group.key} group={group} nameOf={nameOf} narrow={narrow} />)
      )}
    </section>
  );
}

function Group({ group, nameOf, narrow }: { group: CauseGroup; nameOf: (element: FindingElement) => string; narrow: boolean }) {
  const { t } = useLanguage();
  const [open, setOpen] = useState(false);
  const nodes = group.members.filter((member) => member.type === "Node");

  return (
    <div className="rounded-card border border-panel-line bg-panel-2">
      <button type="button" aria-expanded={open} onClick={() => setOpen((current) => !current)} className={`flex w-full items-center gap-2 px-[9px] py-2 text-left text-[13px] leading-[1.4] text-panel-ink hover:bg-wash/6 ${narrow ? "min-h-11" : ""}`}>
        <Caret open={open} />
        <span className="min-w-0 flex-1">{group.cause ? t("ui.impact.groupBecause", { count: nodes.length, cause: nameOf(group.cause) }) : t("ui.impact.group", { count: nodes.length })}</span>
      </button>
      {open && (
        <ul className="flex flex-col gap-2 border-t border-panel-line p-[9px]">
          {nodes.map((member) => (
            <li key={`${member.type}:${member.id}`}>
              <span className="flex items-center gap-2">
                <span aria-hidden className="size-[7px] shrink-0 rounded-full" style={{ background: STATE_TONE[member.state] }} />
                <span className="min-w-0 truncate text-[12px] font-semibold text-panel-ink">{nameOf(member)}</span>
                <span className="num shrink-0 text-[10px] text-panel-ink-3">{t("ui.impact.step", { step: member.propagationStep })}</span>
              </span>
              <span className="block pl-4 text-[11px] leading-[1.45] text-panel-ink-2">{t(member.reason)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function Containment({ states, nameOf }: { states: ElementState[]; nameOf: (element: FindingElement) => string }) {
  const { t } = useLanguage();
  const contained = useMemo(() => containedStates(states), [states]);
  if (contained.length === 0) return null;

  return (
    <section className={SECTION}>
      <span className={`${TITLE} text-warm-panel`}>{t("ui.impact.stoppedHere")}</span>
      <ul className="flex flex-col gap-2 rounded-card border border-[color-mix(in_oklab,var(--af-warm-panel)_40%,transparent)] bg-[color-mix(in_oklab,var(--af-warm-panel)_8%,var(--af-panel-2))] p-2.5">
        {contained.map((state) => (
          <li key={`${state.type}:${state.id}`}>
            <span className="flex flex-wrap items-center gap-1.5">
              <span className="text-[12px] font-semibold text-panel-ink">{nameOf(state)}</span>
              <MechanismChip mechanism={state.mechanism} />
            </span>
            <span className="mt-1 block text-[11px] leading-[1.45] text-panel-ink-2">{t(state.reason)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function Caret({ open }: { open: boolean }) {
  return (
    <svg width="10" height="10" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" aria-hidden className={`shrink-0 text-panel-ink-3 transition-transform ${open ? "rotate-90" : ""}`}>
      <path d="M4 2l4 4-4 4" />
    </svg>
  );
}
