import { describe, expect, it } from "vitest";
import { buildChart, yOf } from "./scoreHistoryChart";
import type { ArchitectureSnapshot } from "../../../types/architecture";

function entry(version: number, score: number | null): ArchitectureSnapshot {
  return {
    version,
    scoringVersion: "v4",
    score,
    summary: null,
    createdAt: new Date(2026, 7, 22, 12, version).toISOString(),
  };
}

describe("yOf", () => {
  it("maps the full 0 to 100 axis, never a fitted one", () => {
    expect(yOf(100)).toBeLessThan(yOf(0));
    expect(yOf(50)).toBeCloseTo((yOf(0) + yOf(100)) / 2, 5);
  });
});

describe("buildChart", () => {
  it("draws nothing for fewer than two versions", () => {
    expect(buildChart([])).toBeNull();
    expect(buildChart([entry(1, 61)])).toBeNull();
  });

  it("spreads the points across the width and insets the ends", () => {
    const chart = buildChart([entry(1, 61), entry(4, 72), entry(9, 78)])!;

    expect(chart.points.map((point) => point.x)).toEqual([4, 50, 96]);
  });

  it("reads the total as first scored to last scored", () => {
    expect(buildChart([entry(1, 61), entry(9, 78)])!.total).toBe(17);
    expect(buildChart([entry(1, 78), entry(9, 61)])!.total).toBe(-17);
  });

  it("breaks the line at an unscored version instead of dropping it to zero", () => {
    const chart = buildChart([entry(1, 61), entry(2, 67), entry(3, null), entry(4, 72), entry(5, 78)])!;

    expect(chart.line.match(/M/g)).toHaveLength(2);
    expect(chart.area.match(/Z/g)).toHaveLength(2);
  });

  it("keeps the unscored version as a point so its slot on the axis stays", () => {
    const chart = buildChart([entry(1, 61), entry(2, null), entry(3, 78)])!;

    expect(chart.points).toHaveLength(3);
    expect(chart.points[1].entry.score).toBeNull();
  });

  it("ignores unscored versions when reading the total", () => {
    const chart = buildChart([entry(1, null), entry(2, 61), entry(3, 78), entry(4, null)])!;

    expect(chart.total).toBe(17);
  });

  it("has no total when only one version ever carried a number", () => {
    expect(buildChart([entry(1, 61), entry(2, null)])!.total).toBeNull();
  });

  it("draws no stroke for a run of one, which has no length", () => {
    const chart = buildChart([entry(1, 61), entry(2, null), entry(3, 78)])!;

    expect(chart.line).toBe("");
  });

  it("fits the sparkline to its own range, unlike the plot", () => {
    const flat = buildChart([entry(1, 58), entry(2, 78)])!;
    const ys = [...flat.spark.matchAll(/[ML]([\d.]+) ([\d.]+)/g)].map((match) => Number(match[2]));

    expect(Math.max(...ys) - Math.min(...ys)).toBeGreaterThan(10);
  });

  it("survives every version scoring the same", () => {
    const chart = buildChart([entry(1, 72), entry(2, 72)])!;

    expect(chart.total).toBe(0);
    expect(chart.line).not.toBe("");
  });
});

describe("the closed row's sparkline", () => {
  it("breaks at an unscored version, the same as the plot", () => {
    const chart = buildChart([entry(1, 61), entry(2, 67), entry(3, null), entry(4, 72), entry(5, 78)])!;

    expect(chart.spark.match(/M/g)).toHaveLength(2);
  });

  it("draws nothing when every run is a single point", () => {
    const chart = buildChart([entry(1, 61), entry(2, null), entry(3, 78)])!;

    expect(chart.spark).toBe("");
  });
});
