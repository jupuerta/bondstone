import { describe, expect, it } from "vitest";
import {
  SPARK,
  bottleneckOf,
  breakIndex,
  breakReading,
  busiestOf,
  clampIndex,
  latencies,
  lessonText,
  lessons,
  pointAt,
  scaleAt,
  spark,
  sweepable,
  OVER_CAPACITY,
} from "./trafficDial";
import { SATURATION } from "./loadMeter";
import type { ComponentLoad, LoadBreakPoint, LoadCurve, LoadCurvePoint } from "../../../types/architecture";
import { LOCALES, createTranslator } from "../../../i18n";

const LADDER = [1, 1.25, 1.6, 2, 2.5, 3.15, 4, 5, 6.3, 8, 10, 12.5, 16, 20];

type Shape = (scale: number, at: number) => Partial<Omit<LoadCurvePoint, "scale">>;

function ladder(shape: Shape = () => ({}), breakPoint: LoadBreakPoint | null = null): LoadCurve {
  return {
    points: LADDER.map((scale, at) => ({
      scale,
      enteringRps: 100 * scale,
      nominalLatencyMs: 40,
      backlogRps: 0,
      loadAmplification: 1,
      components: [load("db", 0.2 * scale)],
      ...shape(scale, at),
    })),
    breakPoint,
  };
}

function load(nodeId: string, utilization: number): ComponentLoad {
  return { nodeId, rps: Math.round(utilization * 500), capacityRps: 500, utilization };
}

describe("indexing the ladder", () => {
  it("never walks off either end of the curve it was given", () => {
    const curve = ladder();

    expect(clampIndex(curve, -3)).toBe(0);
    expect(clampIndex(curve, 99)).toBe(13);
    expect(scaleAt(curve, 99)).toBe(20);
    expect(pointAt(curve, -1)?.scale).toBe(1);
  });

  it("rests at ×1 while there is no curve, which is the canvas the editor already had", () => {
    expect(scaleAt(null, 7)).toBe(1);
    expect(pointAt(null, 7)).toBeNull();
    expect(clampIndex(null, 7)).toBe(0);
  });

  it("has nothing to sweep when nobody declared traffic", () => {
    expect(sweepable(ladder(() => ({ components: [] })))).toBe(false);
    expect(sweepable(ladder())).toBe(true);
    expect(sweepable(null)).toBe(false);
  });
});

describe("the breaking point", () => {
  const at = (scale: number): LoadBreakPoint => ({
    scale,
    nodeId: "db",
    enteringRps: 100 * scale,
    rps: 1_700,
    utilization: 0.82,
  });

  it("reads as a forecast when the ladder has to climb to reach it", () => {
    const reading = breakReading(ladder(() => ({}), at(4)));

    expect(reading).toEqual({ kind: "at", scale: 4, nodeId: "db", enteringRps: 400, utilization: 0.82 });
    expect(breakIndex(ladder(() => ({}), at(4)))).toBe(6);
  });

  it("reads as a verdict when the declared load already crosses the notch", () => {
    expect(breakReading(ladder(() => ({}), at(1))).kind).toBe("already");
    expect(breakIndex(ladder(() => ({}), at(1)))).toBe(0);
  });

  it("says nothing rather than inventing a number when nobody crosses", () => {
    expect(breakReading(ladder())).toEqual({ kind: "none" });
    expect(breakReading(null)).toEqual({ kind: "none" });
    expect(breakIndex(ladder())).toBeNull();
  });
});

describe("the bottleneck the second plot follows", () => {
  it("is whoever broke first, because that is the component the headline just named", () => {
    const curve = ladder(
      () => ({ components: [load("api", 0.9), load("db", 0.3)] }),
      { scale: 2, nodeId: "db", enteringRps: 200, rps: 150, utilization: 0.3 },
    );

    expect(bottleneckOf(curve)?.nodeId).toBe("db");
  });

  it("falls back to the busiest box at the top of the ladder when nobody breaks", () => {
    const curve = ladder((scale) => ({
      components: [load("api", 0.1 * scale), load("db", 0.02 * scale)],
    }));

    expect(bottleneckOf(curve)?.nodeId).toBe("api");
    expect(bottleneckOf(curve)?.utilizations).toHaveLength(14);
  });

  it("breaks a tie by id, the same resolution the sweep itself uses", () => {
    expect(busiestOf({ ...ladder().points[0], components: [load("z", 0.5), load("a", 0.5)] })?.nodeId).toBe("a");
  });

  it("reads a zero for a box that is not in the point, instead of dropping the step", () => {
    const curve = ladder((_, at) => ({ components: at === 0 ? [] : [load("db", 0.5)] }));
    const found = bottleneckOf(curve);

    expect(found?.nodeId).toBe("db");
    expect(found?.utilizations[0]).toBe(0);
  });

  it("has no bottleneck at all on an idle architecture", () => {
    expect(bottleneckOf(ladder(() => ({ components: [] })))).toBeNull();
    expect(bottleneckOf(null)).toBeNull();
  });
});

describe("the two non-linearities of Anexo B, and the backlog", () => {
  it("names the step where a rate limiter stops the entrance growing, and where it stayed", () => {
    const capped = ladder((scale) => ({ enteringRps: Math.min(100 * scale, 300) }));

    expect(lessons(capped)).toContainEqual({ kind: "flattened", scale: 3.15, rps: 300 });
  });

  it("says nothing about a limiter on an entrance that keeps climbing", () => {
    expect(lessons(ladder()).some((lesson) => lesson.kind === "flattened")).toBe(false);
  });

  it("never reads an architecture with no traffic as a rate limit", () => {
    expect(lessons(ladder(() => ({ enteringRps: 0 })))).toEqual([]);
  });

  it("names the step where the retries start amplifying, and the worst of the ladder", () => {
    const storm = ladder((scale) => ({ loadAmplification: scale <= 2.5 ? 1 : 1 + (scale - 2.5) * 0.1 }));

    expect(lessons(storm)).toContainEqual({ kind: "amplified", scale: 3.15, peak: 1 + 17.5 * 0.1 });
  });

  it("does not call a rounding crumb a retry storm", () => {
    expect(lessons(ladder(() => ({ loadAmplification: 1.004 })))).toEqual([]);
  });

  it("names the step where the queue starts piling work up", () => {
    const queue = ladder((scale) => ({ backlogRps: scale >= 2.5 ? (scale - 2) * 100 : 0 }));

    expect(lessons(queue)).toContainEqual({ kind: "piled", scale: 2.5, peak: 1_800 });
  });

  it("carries every lesson it found, in the order the annex tells them", () => {
    const everything = ladder((scale) => ({
      enteringRps: Math.min(100 * scale, 300),
      loadAmplification: scale >= 4 ? 1.5 : 1,
      backlogRps: scale >= 8 ? 50 : 0,
    }));

    expect(lessons(everything).map((lesson) => lesson.kind)).toEqual(["flattened", "amplified", "piled"]);
  });

  it("has nothing to teach about a curve that is a straight line", () => {
    expect(lessons(ladder())).toEqual([]);
    expect(lessons(null)).toEqual([]);
  });
});

describe("the two small plots", () => {
  const floor = SPARK.height - SPARK.bottom;

  it("draws a point per step of the ladder and puts the dot on the one you are reading", () => {
    const drawn = spark(latencies(ladder((scale) => ({ nominalLatencyMs: 40 * scale }))), 3);

    expect(drawn?.line.match(/[ML]/g)).toHaveLength(14);
    expect(drawn?.dot.x).toBeCloseTo(SPARK.left + (3 / 13) * (SPARK.width - SPARK.left - SPARK.right), 1);
    expect(drawn?.ceiling).toBe(800);
  });

  it("is zero-based, so a curve that stops growing draws a plateau and not a rise", () => {
    const flat = spark([100, 200, 200, 200], 0);

    expect(flat?.line).toContain(`${SPARK.top}`);
    expect(flat?.dot.y).toBeGreaterThan(SPARK.top);
    expect(flat?.dot.y).toBeLessThan(floor);
  });

  it("puts the 80 % notch where the meters put it, above a curve that never reaches it", () => {
    const quiet = spark([0.1, 0.2, 0.3], 2, { guide: SATURATION });

    expect(quiet?.ceiling).toBe(SATURATION);
    expect(quiet?.guide).toBe(SPARK.top);
    expect(quiet?.dot.y).toBeGreaterThan(SPARK.top);
  });

  it("does not divide by a flat zero", () => {
    const nothing = spark([0, 0, 0], 1);

    expect(nothing?.dot).toEqual({ x: expect.any(Number), y: floor });
    expect(nothing?.line).not.toContain("NaN");
  });

  it("stops the axis at twice capacity, the way the meter stops the bar at one", () => {
    const runaway = spark([0.5, 1, 10], 2, { guide: SATURATION, cap: OVER_CAPACITY });

    expect(runaway?.ceiling).toBe(OVER_CAPACITY);
    expect(runaway?.dot.y).toBe(SPARK.top);
    expect(runaway?.guide).toBeCloseTo(floor - (SATURATION / OVER_CAPACITY) * (floor - SPARK.top), 1);
  });

  it("never lets the cap push the notch off the top of its own plot", () => {
    const quiet = spark([0.02, 0.04], 1, { guide: SATURATION, cap: OVER_CAPACITY });

    expect(quiet?.ceiling).toBe(SATURATION);
    expect(quiet?.guide).toBe(SPARK.top);
  });

  it("puts the breaking point on both plots at the step the slider marks it", () => {
    const marked = spark([1, 2, 3, 4, 5], 0, { mark: 2 });

    expect(marked?.mark).toBeCloseTo(SPARK.left + 0.5 * (SPARK.width - SPARK.left - SPARK.right), 1);
    expect(spark([1, 2, 3], 0)?.mark).toBeNull();
    expect(spark([1, 2, 3], 0, { mark: null })?.mark).toBeNull();
  });

  it("refuses to draw a line out of a single point", () => {
    expect(spark([4], 0)).toBeNull();
    expect(spark([], 0)).toBeNull();
  });

  it("clamps the dot instead of reading off the end of the series", () => {
    expect(spark([1, 2, 3], 40)?.dot.x).toBe(spark([1, 2, 3], 2)?.dot.x);
  });
});

describe("what the dial says", () => {
  const sentences: [string, Record<string, string | number>][] = [
    ["ui.dial.ask", {}],
    ["ui.dial.title", {}],
    ["ui.dial.close", {}],
    ["ui.dial.failed", {}],
    ["ui.dial.idle", {}],
    ["ui.dial.latency", {}],
    ["ui.dial.rangeLabel", {}],
    ["ui.dial.resting", {}],
    ["ui.dial.scale", { scale: 3.15 }],
    ["ui.dial.entering", { value: 1_700 }],
    ["ui.dial.peak", { value: "410 ms" }],
    ["ui.dial.bottleneck", { name: "Postgres" }],
    ["ui.dial.hypothetical", { scale: 2.5 }],
    ["ui.dial.break.headline", { rps: 1_700, name: "Postgres" }],
    ["ui.dial.break.detail", { scale: 4, utilization: 0.82 }],
    ["ui.dial.break.already", { name: "Postgres", utilization: 0.94, rps: 400 }],
    ["ui.dial.break.none", {}],
    ["ui.error.loadCurve", {}],
    ["ui.node.loadAt", { scale: 2.5, loadRps: 850, capacityRps: 500, utilization: 1.7 }],
  ];

  it("says every one of its sentences in both languages, with nothing left unfilled", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const [key, args] of sentences) {
        const sentence = t(key, args);
        expect(sentence, `${key} in ${locale}`).not.toBe(key);
        expect(sentence, `${key} in ${locale}`).not.toMatch(/[{}]/);
      }
    }
  });

  it("says each lesson in both languages, filled from the lesson it found", () => {
    const found = lessons(
      ladder((scale) => ({
        enteringRps: Math.min(100 * scale, 300),
        loadAmplification: scale >= 4 ? 2.7 : 1,
        backlogRps: scale >= 8 ? 1_800 : 0,
      })),
    );

    expect(found).toHaveLength(3);

    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const lesson of found) {
        const sentence = t(lessonText(lesson));
        expect(sentence, `${lesson.kind} in ${locale}`).not.toBe(lessonText(lesson).key);
        expect(sentence, `${lesson.kind} in ${locale}`).not.toMatch(/[{}]/);
      }
    }
  });

  it("changes the tense when the lesson is already true at the declared load", () => {
    const already = lessons(
      ladder(() => ({ enteringRps: 300, loadAmplification: 1.4, backlogRps: 90 })),
    );

    expect(already.map((lesson) => lessonText(lesson).key)).toEqual([
      "ui.dial.lesson.flattenedAlready",
      "ui.dial.lesson.amplifiedAlready",
      "ui.dial.lesson.piledAlready",
    ]);

    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const lesson of already) {
        const sentence = t(lessonText(lesson));
        expect(sentence, `${lesson.kind} in ${locale}`).not.toBe(lessonText(lesson).key);
        expect(sentence, `${lesson.kind} in ${locale}`).not.toMatch(/[{}]/);
        expect(sentence, `${lesson.kind} in ${locale}`).not.toMatch(/×1[^,.\d]/);
      }
    }
  });

  it("prints the ladder's fractional steps as a locale would write them", () => {
    expect(createTranslator("es")("ui.dial.scale", { scale: 3.15 })).toBe("×3,15");
    expect(createTranslator("en")("ui.dial.scale", { scale: 3.15 })).toBe("×3.15");
    expect(createTranslator("es")("ui.dial.scale", { scale: 20 })).toBe("×20");
  });
});
