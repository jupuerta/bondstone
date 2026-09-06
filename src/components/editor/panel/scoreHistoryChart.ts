import type { ArchitectureSnapshot } from "../../../types/architecture";

export const PLOT_HEIGHT = 130;
const Y_TOP = 10;
const Y_BOTTOM = 126;
const X_INSET = 4;

export function yOf(score: number): number {
  return Y_BOTTOM - (score / 100) * (Y_BOTTOM - Y_TOP);
}

export interface PlotPoint {
  entry: ArchitectureSnapshot;
  x: number;
  y: number;
}

export interface Chart {
  points: PlotPoint[];
  line: string;
  area: string;
  spark: string;
  total: number | null;
}

export function buildChart(history: ArchitectureSnapshot[]): Chart | null {
  if (history.length < 2) return null;

  const points: PlotPoint[] = history.map((entry, index) => ({
    entry,
    x: X_INSET + (index * (100 - 2 * X_INSET)) / (history.length - 1),
    y: yOf(entry.score ?? 0),
  }));

  const runs: PlotPoint[][] = [];
  let run: PlotPoint[] = [];
  for (const point of points) {
    if (point.entry.score === null) {
      if (run.length > 0) runs.push(run);
      run = [];
    } else {
      run.push(point);
    }
  }
  if (run.length > 0) runs.push(run);

  const drawable = runs.filter((candidate) => candidate.length > 1);
  const scored = points.filter((point) => point.entry.score !== null);

  return {
    points,
    line: drawable.map(polyline).join(" "),
    area: drawable
      .map((r) => `${polyline(r)} L${round(r[r.length - 1].x)} ${Y_BOTTOM} L${round(r[0].x)} ${Y_BOTTOM} Z`)
      .join(" "),
    spark: sparkline(history),
    total:
      scored.length > 1
        ? scored[scored.length - 1].entry.score! - scored[0].entry.score!
        : null,
  };
}

function polyline(points: PlotPoint[]): string {
  return points.map((p, index) => `${index === 0 ? "M" : "L"}${round(p.x)} ${round(p.y)}`).join(" ");
}

function sparkline(history: ArchitectureSnapshot[]): string {
  const scores = history.map((entry) => entry.score);
  const present = scores.filter((score): score is number => score !== null);
  if (present.length < 2) return "";

  const low = Math.min(...present) - 2;
  const high = Math.max(...present) + 2;

  const runs: string[] = [];
  let run: string[] = [];

  scores.forEach((score, index) => {
    if (score === null) {
      if (run.length > 1) runs.push(run.join(" "));
      run = [];
      return;
    }

    const x = 3 + (index * 94) / (history.length - 1);
    const y = 17 - ((score - low) / (high - low)) * 14;
    run.push(`${run.length === 0 ? "M" : "L"}${round(x)} ${round(y)}`);
  });

  if (run.length > 1) runs.push(run.join(" "));

  return runs.join(" ");
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}
