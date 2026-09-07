import type { ChallengeDetail, LatencyHop } from "../../../types/architecture";
import type { Translator } from "../../../i18n";

export type SegmentKind = "network" | "processing" | "queueing" | "injected";

export const SEGMENT_KINDS: readonly SegmentKind[] = ["network", "processing", "queueing", "injected"];

export interface WaterfallSegment {
  kind: SegmentKind;
  ms: number;
  share: number;
}

export interface WaterfallRow {
  hop: LatencyHop;
  start: number;
  end: number;
  segments: WaterfallSegment[];
  clipped: boolean;
}

export interface WaterfallBudget {
  ms: number;
  at: number;
  exceeded: boolean;
  overshootMs: number;
}

export interface Waterfall {
  rows: WaterfallRow[];
  axisMs: number;
  totalMs: number;
  budget: WaterfallBudget | null;
  clipped: boolean;
  kinds: SegmentKind[];
}

const EPSILON = 0.5;

const TORN_TAIL = 22;

export function buildWaterfall(
  path: LatencyHop[] | null | undefined,
  budgetMs?: number | null,
): Waterfall | null {
  if (!path || path.length === 0) return null;

  const totalMs = path[path.length - 1].cumulativeMs;
  const budgeted = budgetMs != null && budgetMs > 0 ? budgetMs : null;

  let axisMs = 0;
  for (const hop of path) {
    if (hop.outcome === "Completed") axisMs = Math.max(axisMs, hop.cumulativeMs);
  }
  if (budgeted !== null) axisMs = Math.max(axisMs, budgeted);
  if (axisMs <= 0) axisMs = totalMs;
  if (axisMs <= 0) return null;

  const clipped = path.map((hop) => hop.cumulativeMs > axisMs + EPSILON);
  const span = clipped.some(Boolean) ? 100 - TORN_TAIL : 100;

  const rows: WaterfallRow[] = path.map((hop, index) => ({
    hop,
    start: pct(hop.cumulativeMs - hop.totalMs, axisMs, span),
    end: clipped[index] ? 100 : pct(hop.cumulativeMs, axisMs, span),
    segments: segmentsOf(hop),
    clipped: clipped[index],
  }));

  const present = new Set(rows.flatMap((row) => row.segments.map((segment) => segment.kind)));

  return {
    rows,
    axisMs,
    totalMs,
    budget:
      budgeted === null
        ? null
        : {
            ms: budgeted,
            at: pct(budgeted, axisMs, span),
            exceeded: totalMs > budgeted,
            overshootMs: Math.max(0, totalMs - budgeted),
          },
    clipped: clipped.some(Boolean),
    kinds: SEGMENT_KINDS.filter((kind) => present.has(kind)),
  };
}

function segmentsOf(hop: LatencyHop): WaterfallSegment[] {
  if (hop.outcome !== "Completed") return [];

  const parts: Array<{ kind: SegmentKind; ms: number }> = [
    { kind: "network", ms: hop.networkMs },
    { kind: "processing", ms: hop.processingMs },
    { kind: "queueing", ms: hop.queueingMs },
    { kind: "injected", ms: hop.injectedMs },
  ];

  const sum = parts.reduce((total, part) => total + part.ms, 0);
  if (sum <= 0 || sum !== hop.totalMs) return [];

  return parts
    .filter((part) => part.ms > 0)
    .map((part) => ({ ...part, share: part.ms / sum }));
}

export function hopFormula(row: WaterfallRow, t: Translator): string | null {
  const { hop } = row;
  const ms = (value: number) => t("ui.impact.milliseconds", { value });

  if (hop.outcome !== "Completed") {
    return hop.timeoutMs == null
      ? null
      : `${t("ui.impact.cascade.duration", { value: hop.timeoutMs })} × (${hop.retries} + 1) = ` +
          t("ui.impact.cascade.duration", { value: hop.totalMs });
  }

  if (row.segments.length === 0) return null;

  const parts = row.segments.map(
    (segment) => `${ms(segment.ms)} ${t(`ui.impact.cascade.segment.${segment.kind}`)}`,
  );

  return `${parts.join(" + ")} = ${ms(hop.totalMs)}`;
}

export function hopNote(row: WaterfallRow): { key: string; ms: number } | null {
  const { hop } = row;
  if (hop.outcome === "Completed") return null;

  if (hop.timeoutMs == null) return { key: "ui.impact.cascade.unbounded", ms: hop.totalMs };

  return hop.outcome === "Expired"
    ? { key: "ui.impact.cascade.expired", ms: hop.totalMs }
    : { key: "ui.impact.cascade.unreachable", ms: hop.totalMs };
}

function pct(ms: number, axisMs: number, span: number): number {
  const share = (span * ms) / axisMs;
  return Math.round(Math.min(span, Math.max(0, share)) * 10) / 10;
}

export function latencyBudgetOf(challenge: ChallengeDetail | null | undefined): number | null {
  if (!challenge) return null;

  for (const requirement of [...challenge.requirements, ...challenge.optionalGoals]) {
    if (requirement.latencyBudgetMs != null) return requirement.latencyBudgetMs;
  }

  return null;
}
