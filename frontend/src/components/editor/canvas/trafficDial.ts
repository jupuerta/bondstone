import type { ComponentLoad, LoadCurve, LoadCurvePoint, LocalizedText } from "../../../types/architecture";

const EPS = 0.005;

const AMPLIFIED = 1.01;

export function clampIndex(curve: LoadCurve | null, index: number): number {
  const last = (curve?.points.length ?? 1) - 1;
  return Math.min(Math.max(index, 0), Math.max(last, 0));
}

export function scaleAt(curve: LoadCurve | null, index: number): number {
  return curve?.points[clampIndex(curve, index)]?.scale ?? 1;
}

export function pointAt(curve: LoadCurve | null, index: number): LoadCurvePoint | null {
  return curve?.points[clampIndex(curve, index)] ?? null;
}

export function sweepable(curve: LoadCurve | null): boolean {
  return (curve?.points[curve.points.length - 1]?.components.length ?? 0) > 0;
}

export type BreakReading =
  | { kind: "none" }
  | { kind: "already"; scale: number; nodeId: string; enteringRps: number; utilization: number }
  | { kind: "at"; scale: number; nodeId: string; enteringRps: number; utilization: number };

export function breakReading(curve: LoadCurve | null): BreakReading {
  const point = curve?.breakPoint ?? null;
  if (point === null) return { kind: "none" };

  const found = {
    scale: point.scale,
    nodeId: point.nodeId,
    enteringRps: point.enteringRps,
    utilization: point.utilization,
  };

  return point.scale <= 1 ? { kind: "already", ...found } : { kind: "at", ...found };
}

export function breakIndex(curve: LoadCurve | null): number | null {
  const point = curve?.breakPoint ?? null;
  if (point === null) return null;

  const at = curve?.points.findIndex((candidate) => candidate.scale === point.scale) ?? -1;
  return at < 0 ? null : at;
}

export interface Bottleneck {
  nodeId: string;
  utilizations: number[];
}

export function bottleneckOf(curve: LoadCurve | null): Bottleneck | null {
  if (curve === null || curve.points.length === 0) return null;

  const nodeId = curve.breakPoint?.nodeId ?? busiestOf(curve.points[curve.points.length - 1])?.nodeId ?? null;
  if (nodeId === null) return null;

  return { nodeId, utilizations: curve.points.map((point) => utilizationOf(point, nodeId)) };
}

export function busiestOf(point: LoadCurvePoint | null): ComponentLoad | null {
  let busiest: ComponentLoad | null = null;

  for (const load of point?.components ?? []) {
    const wins =
      busiest === null ||
      load.utilization > busiest.utilization ||
      (load.utilization === busiest.utilization && load.nodeId < busiest.nodeId);

    if (wins) busiest = load;
  }

  return busiest;
}

export function latencies(curve: LoadCurve | null): number[] {
  return (curve?.points ?? []).map((point) => point.nominalLatencyMs);
}

function utilizationOf(point: LoadCurvePoint, nodeId: string): number {
  return point.components.find((load) => load.nodeId === nodeId)?.utilization ?? 0;
}

export type Lesson =
  | { kind: "flattened"; scale: number; rps: number }
  | { kind: "amplified"; scale: number; peak: number }
  | { kind: "piled"; scale: number; peak: number };

export function lessons(curve: LoadCurve | null): Lesson[] {
  return [flattening(curve), amplifying(curve), piling(curve)].filter(
    (lesson): lesson is Lesson => lesson !== null,
  );
}

export function lessonText(lesson: Lesson): LocalizedText {
  const key = `ui.dial.lesson.${lesson.kind}${lesson.scale <= 1 ? "Already" : ""}`;

  return lesson.kind === "flattened"
    ? { key, args: { scale: lesson.scale, rps: lesson.rps } }
    : { key, args: { scale: lesson.scale, peak: lesson.peak } };
}

function flattening(curve: LoadCurve | null): Lesson | null {
  const points = curve?.points ?? [];

  for (let at = 1; at < points.length; at++) {
    if (points[at].enteringRps > points[at - 1].enteringRps + EPS) continue;
    if (points[at - 1].enteringRps <= 0) continue;

    return { kind: "flattened", scale: points[at - 1].scale, rps: points[at - 1].enteringRps };
  }

  return null;
}

function amplifying(curve: LoadCurve | null): Lesson | null {
  const points = curve?.points ?? [];
  const from = points.find((point) => point.loadAmplification >= AMPLIFIED);
  if (from === undefined) return null;

  return { kind: "amplified", scale: from.scale, peak: Math.max(...points.map((point) => point.loadAmplification)) };
}

function piling(curve: LoadCurve | null): Lesson | null {
  const points = curve?.points ?? [];
  const from = points.find((point) => point.backlogRps > EPS);
  if (from === undefined) return null;

  return { kind: "piled", scale: from.scale, peak: Math.max(...points.map((point) => point.backlogRps)) };
}

export const SPARK = { width: 176, height: 44, top: 6, bottom: 4, left: 2, right: 3 };

export const OVER_CAPACITY = 2;

export interface SparkOptions {
  guide?: number;
  cap?: number;
  mark?: number | null;
}

export interface Spark {
  line: string;
  area: string;
  dot: { x: number; y: number };
  guide: number | null;
  mark: number | null;
  ceiling: number;
}

export function spark(values: number[], index: number, options: SparkOptions = {}): Spark | null {
  if (values.length < 2) return null;

  const { guide, cap = Infinity, mark } = options;

  const ceiling = Math.max(Math.min(Math.max(...values), cap), guide ?? 0);
  const floor = SPARK.height - SPARK.bottom;
  const span = SPARK.width - SPARK.left - SPARK.right;

  const xOf = (at: number) => SPARK.left + (at / (values.length - 1)) * span;
  const yOf = (value: number) =>
    ceiling <= 0 ? floor : floor - (Math.min(value, ceiling) / ceiling) * (floor - SPARK.top);

  const line = values.map((value, at) => `${at === 0 ? "M" : "L"}${round(xOf(at))} ${round(yOf(value))}`).join(" ");

  const at = clamp(index, values.length - 1);

  return {
    line,
    area: `${line} L${round(xOf(values.length - 1))} ${floor} L${round(xOf(0))} ${floor} Z`,
    dot: { x: round(xOf(at)), y: round(yOf(values[at])) },
    guide: guide === undefined ? null : round(yOf(guide)),
    mark: mark === undefined || mark === null ? null : round(xOf(clamp(mark, values.length - 1))),
    ceiling,
  };
}

function clamp(value: number, max: number): number {
  return Math.min(Math.max(value, 0), max);
}

function round(value: number): number {
  return Math.round(value * 10) / 10;
}
