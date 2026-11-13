import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { createTranslator } from "../../../i18n";
import type {
  ChallengeDetail,
  ChallengeRequirementInfo,
  HopOutcome,
  LatencyHop,
  SimulationResult,
} from "../../../types/architecture";
import { buildWaterfall, hopFormula, hopNote, latencyBudgetOf } from "./latencyWaterfall";

const es = createTranslator("es");
const en = createTranslator("en");

function hop(overrides: Partial<LatencyHop> & { cumulativeMs: number }): LatencyHop {
  const base: LatencyHop = {
    nodeId: "n1",
    connectionId: null,
    outcome: "Completed",
    processingMs: 0,
    queueingMs: 0,
    queueingFactor: 1,
    networkMs: 0,
    injectedMs: 0,
    totalMs: 0,
    callMs: 0,
    rawCallMs: null,
    timeoutMs: null,
    retries: 0,
    ...overrides,
  };

  return { ...base, totalMs: base.networkMs + base.processingMs + base.queueingMs + base.injectedMs };
}

function failed(outcome: Exclude<HopOutcome, "Completed">, overrides: Partial<LatencyHop> & { cumulativeMs: number }) {
  return {
    ...hop({ ...overrides, outcome }),
    outcome,
    totalMs: overrides.totalMs ?? overrides.callMs ?? 0,
    callMs: overrides.callMs ?? overrides.totalMs ?? 0,
  } as LatencyHop;
}

describe("there is nothing to draw", () => {
  it("says so for a payload recorded before the field existed, and for a path with no entry point", () => {
    expect(buildWaterfall(undefined)).toBeNull();
    expect(buildWaterfall(null)).toBeNull();
    expect(buildWaterfall([])).toBeNull();
  });

  it("says so for a path that costs nothing at all", () => {
    expect(buildWaterfall([hop({ cumulativeMs: 0 })])).toBeNull();
  });

  it("draws a costless path anyway once there is a budget to draw it against", () => {
    const waterfall = buildWaterfall([hop({ cumulativeMs: 0 })], 100)!;

    expect(waterfall.axisMs).toBe(100);
    expect(waterfall.budget!.exceeded).toBe(false);
  });
});

describe("a waterfall, not a bar chart", () => {
  const path = [
    hop({ nodeId: "gateway", processingMs: 5, cumulativeMs: 5 }),
    hop({ nodeId: "orders", networkMs: 5, processingMs: 10, cumulativeMs: 20 }),
    hop({ nodeId: "db", networkMs: 2, processingMs: 8, cumulativeMs: 30 }),
  ];

  it("starts each bar where the one above it ended", () => {
    const rows = buildWaterfall(path)!.rows;

    expect(rows.map((row) => [row.start, row.end])).toEqual([
      [0, 16.7],
      [16.7, 66.7],
      [66.7, 100],
    ]);
  });

  it("ends the last bar at the axis, which is the whole path", () => {
    const waterfall = buildWaterfall(path)!;

    expect(waterfall.totalMs).toBe(30);
    expect(waterfall.axisMs).toBe(30);
    expect(waterfall.rows[2].end).toBe(100);
  });
});

describe("the parts of a bar", () => {
  it("reconstructs the bar, in the order it happens", () => {
    const waterfall = buildWaterfall([
      hop({ networkMs: 5, processingMs: 10, queueingMs: 80, injectedMs: 5, cumulativeMs: 100 }),
    ])!;

    expect(waterfall.rows[0].segments.map((segment) => [segment.kind, segment.ms])).toEqual([
      ["network", 5],
      ["processing", 10],
      ["queueing", 80],
      ["injected", 5],
    ]);
    expect(waterfall.rows[0].segments.reduce((total, segment) => total + segment.share, 0)).toBeCloseTo(1, 10);
  });

  it("leaves out the parts that are zero, which is most of them in most scenarios", () => {
    const waterfall = buildWaterfall([hop({ networkMs: 5, processingMs: 10, cumulativeMs: 15 })])!;

    expect(waterfall.rows[0].segments.map((segment) => segment.kind)).toEqual(["network", "processing"]);
  });

  it("draws a bar whole rather than in parts that do not add up to it", () => {
    const lying: LatencyHop = { ...hop({ networkMs: 5, processingMs: 10, cumulativeMs: 15 }), totalMs: 40 };

    expect(buildWaterfall([lying])!.rows[0].segments).toEqual([]);
  });

  it("gives a failed call one bar and not four", () => {
    const waterfall = buildWaterfall([
      hop({ processingMs: 5, cumulativeMs: 5 }),
      failed("Expired", { callMs: 15_000, timeoutMs: 5_000, retries: 2, rawCallMs: 20_015, cumulativeMs: 15_005 }),
    ])!;

    expect(waterfall.rows[1].segments).toEqual([]);
  });

  it("names the parts that are on screen and no others", () => {
    const waterfall = buildWaterfall([
      hop({ processingMs: 5, cumulativeMs: 5 }),
      hop({ networkMs: 2, processingMs: 8, queueingMs: 20, cumulativeMs: 35 }),
    ])!;

    expect(waterfall.kinds).toEqual(["network", "processing", "queueing"]);
  });
});

describe("the scale is set by the work, never by the wait", () => {
  const unbounded = [
    hop({ nodeId: "api", processingMs: 5, cumulativeMs: 5 }),
    failed("Unreachable", { nodeId: "db", callMs: 30_000, timeoutMs: null, cumulativeMs: 30_005 }),
  ];

  it("keeps the 30 s bar off the ruler instead of flattening the cascade into a stripe", () => {
    const waterfall = buildWaterfall(unbounded)!;

    expect(waterfall.axisMs).toBe(5);
    expect(waterfall.rows[1].clipped).toBe(true);
    expect(waterfall.clipped).toBe(true);
    expect(waterfall.rows[1].hop.totalMs).toBe(30_000);
  });

  it("keeps the whole path as the total even when the last bar runs off the end", () => {
    expect(buildWaterfall(unbounded)!.totalMs).toBe(30_005);
  });

  it("keeps back enough width for the torn bar to be a bar", () => {
    const rows = buildWaterfall(unbounded)!.rows;

    expect(rows[0].end).toBe(78);
    expect(rows[1].start).toBe(78);
    expect(rows[1].end).toBe(100);
  });

  it("gives the whole width back when nothing ran off the ruler", () => {
    const rows = buildWaterfall([hop({ processingMs: 5, cumulativeMs: 5 })])!.rows;

    expect(rows[0].end).toBe(100);
  });

  it("lets a completed hop set the scale however expensive it is", () => {
    const waterfall = buildWaterfall([
      hop({ processingMs: 5, cumulativeMs: 5 }),
      hop({ networkMs: 5, processingMs: 10, injectedMs: 8_000, cumulativeMs: 8_020 }),
    ])!;

    expect(waterfall.axisMs).toBe(8_020);
    expect(waterfall.clipped).toBe(false);
  });

  it("gives the ruler to the failed bar when it is the only bar there is", () => {
    const waterfall = buildWaterfall([failed("Unreachable", { callMs: 30_000, totalMs: 30_000, cumulativeMs: 30_000 })])!;

    expect(waterfall.axisMs).toBe(30_000);
    expect(waterfall.clipped).toBe(false);
  });
});

describe("the budget line", () => {
  const path = [
    hop({ processingMs: 20, cumulativeMs: 20 }),
    hop({ networkMs: 5, processingMs: 15, queueingMs: 760, cumulativeMs: 800 }),
  ];

  it("lands on the same ruler the bars do, and says by how much you went over", () => {
    const budget = buildWaterfall(path, 100)!.budget!;

    expect(budget.at).toBe(12.5);
    expect(budget.exceeded).toBe(true);
    expect(budget.overshootMs).toBe(700);
  });

  it("stretches the ruler when the budget is longer than the path, so being well inside it is a drawing", () => {
    const waterfall = buildWaterfall([hop({ processingMs: 40, cumulativeMs: 40 })], 100)!;

    expect(waterfall.axisMs).toBe(100);
    expect(waterfall.budget!.at).toBe(100);
    expect(waterfall.rows[0].end).toBe(40);
    expect(waterfall.budget!.exceeded).toBe(false);
  });

  it("is absent when the challenge declares none, and when it declares a nonsense one", () => {
    expect(buildWaterfall(path)!.budget).toBeNull();
    expect(buildWaterfall(path, null)!.budget).toBeNull();
    expect(buildWaterfall(path, 0)!.budget).toBeNull();
  });
});

describe("the arithmetic beside a bar", () => {
  it("adds the parts up to the bar, in both languages", () => {
    const row = buildWaterfall([hop({ networkMs: 5, processingMs: 10, queueingMs: 85, cumulativeMs: 100 })])!.rows[0];

    expect(hopFormula(row, es)).toBe("5 ms red + 10 ms proceso + 85 ms encolamiento = 100 ms");
    expect(hopFormula(row, en)).toBe("5 ms network + 10 ms processing + 85 ms queueing = 100 ms");
  });

  it("writes a failed call as its timeout times its retries", () => {
    const row = buildWaterfall([
      hop({ processingMs: 5, cumulativeMs: 5 }),
      failed("Expired", { callMs: 15_000, timeoutMs: 5_000, retries: 2, cumulativeMs: 15_005 }),
    ])!.rows[1];

    expect(hopFormula(row, es)).toBe("5 s × (2 + 1) = 15 s");
    expect(hopFormula(row, en)).toBe("5 s × (2 + 1) = 15 s");
  });

  it("writes × (0 + 1) rather than dropping it, because that is where the lesson is", () => {
    const row = buildWaterfall([
      hop({ processingMs: 5, cumulativeMs: 5 }),
      failed("Unreachable", { callMs: 5_000, timeoutMs: 5_000, retries: 0, cumulativeMs: 5_005 }),
    ])!.rows[1];

    expect(hopFormula(row, es)).toBe("5 s × (0 + 1) = 5 s");
  });

  it("keeps a fractional timeout closing, which is why it is not the panel's rounded duration", () => {
    const row = buildWaterfall([
      hop({ processingMs: 5, cumulativeMs: 5 }),
      failed("Expired", { callMs: 4_500, timeoutMs: 1_500, retries: 2, cumulativeMs: 4_505 }),
    ])!.rows[1];

    expect(hopFormula(row, es)).toBe("1,5 s × (2 + 1) = 4,5 s");
    expect(hopFormula(row, en)).toBe("1.5 s × (2 + 1) = 4.5 s");
  });

  it("has nothing to write for an unbounded wait, which is a rule and not a multiplication", () => {
    const row = buildWaterfall([
      hop({ processingMs: 5, cumulativeMs: 5 }),
      failed("Unreachable", { callMs: 30_000, timeoutMs: null, cumulativeMs: 30_005 }),
    ])!.rows[1];

    expect(hopFormula(row, es)).toBeNull();
  });

  it("has nothing to write for a bar it could not take apart", () => {
    const lying: LatencyHop = { ...hop({ processingMs: 10, cumulativeMs: 10 }), totalMs: 40 };

    expect(hopFormula(buildWaterfall([lying])!.rows[0], es)).toBeNull();
  });
});

describe("the three ways of not answering", () => {
  const first = hop({ processingMs: 5, cumulativeMs: 5 });

  const noteOf = (last: LatencyHop) => hopNote(buildWaterfall([first, last])!.rows[1]);

  it("marks the unbounded wait as what it is, and needs no field to know it", () => {
    expect(noteOf(failed("Unreachable", { callMs: 30_000, timeoutMs: null, cumulativeMs: 30_005 }))).toEqual({
      key: "ui.impact.cascade.unbounded",
      ms: 30_000,
    });
  });

  it("separates a call that blew its own timeout from a target that was not there", () => {
    expect(noteOf(failed("Expired", { callMs: 15_000, timeoutMs: 5_000, retries: 2, cumulativeMs: 15_005 }))?.key).toBe(
      "ui.impact.cascade.expired",
    );
    expect(noteOf(failed("Unreachable", { callMs: 5_000, timeoutMs: 5_000, cumulativeMs: 5_005 }))?.key).toBe(
      "ui.impact.cascade.unreachable",
    );
  });

  it("says nothing about a hop that answered", () => {
    expect(hopNote(buildWaterfall([first])!.rows[0])).toBeNull();
  });

  it("renders every sentence it can produce, in both languages", () => {
    for (const key of ["ui.impact.cascade.unbounded", "ui.impact.cascade.expired", "ui.impact.cascade.unreachable"]) {
      for (const t of [es, en]) {
        expect(t(key, { value: 30_000 })).not.toBe(key);
      }
    }
  });
});

describe("against the recording", () => {
  const recorded: SimulationResult = JSON.parse(
    readFileSync(join(__dirname, "..", "..", "..", "export", "__fixtures__", "minimal-api-db.down.simulation.json"), "utf8"),
  );

  it("ends on the figure the panel prints above it", () => {
    expect(buildWaterfall(recorded.criticalPath)!.totalMs).toBe(recorded.metrics.faultedLatencyMs);
  });

  it("draws the API's 5 ms and tears the 5 s call off the end of them", () => {
    const waterfall = buildWaterfall(recorded.criticalPath)!;

    expect(waterfall.rows.map((row) => row.hop.outcome)).toEqual(["Completed", "Unreachable"]);
    expect(waterfall.axisMs).toBe(5);
    expect(waterfall.rows[0].segments.map((segment) => segment.kind)).toEqual(["processing"]);
    expect(waterfall.rows[1].clipped).toBe(true);
    expect(waterfall.rows.map((row) => [row.start, row.end])).toEqual([
      [0, 78],
      [78, 100],
    ]);
    expect(hopNote(waterfall.rows[1])).toEqual({ key: "ui.impact.cascade.unreachable", ms: 5_000 });
  });
});

describe("Anexo A, published rather than rewritten", () => {
  it("keeps all five branches of the cost of a call, in both languages", () => {
    for (const t of [es, en]) {
      const block = t("ui.impact.cascade.arithmetic");

      expect(block).not.toBe("ui.impact.cascade.arithmetic");
      expect(block.split("\n")).toHaveLength(10);
      expect(block).toContain("circuit breaker");
      expect(block).toContain("UnboundedWait");
      expect(block.match(/=/g)!.length).toBeGreaterThanOrEqual(2);
    }
  });

  it("keeps the two sentences the model already wrote about failed calls", () => {
    for (const t of [es, en]) {
      for (const key of ["ui.impact.cascade.failedCall", "ui.impact.cascade.twoNumbers"]) {
        expect(t(key)).not.toBe(key);
      }
    }
  });
});

describe("which budget the cascade draws", () => {
  const requirement = (latencyBudgetMs?: number | null): ChallengeRequirementInfo => ({
    kind: "underScenario",
    label: { key: "challenge.x.requirement.1" },
    latencyBudgetMs,
  });

  const challenge = (
    requirements: ChallengeRequirementInfo[],
    optionalGoals: ChallengeRequirementInfo[] = [],
  ): ChallengeDetail => ({
    slug: "x",
    title: { key: "challenge.x.title" },
    story: { key: "challenge.x.story" },
    difficulty: "intermediate",
    topic: "Scalability",
    requirements,
    optionalGoals,
    hints: [],
  });

  it("draws nothing when there is no challenge, which is most of the time", () => {
    expect(latencyBudgetOf(null)).toBeNull();
    expect(latencyBudgetOf(undefined)).toBeNull();
  });

  it("holds the line that decides whether you passed, not the stretch goal", () => {
    expect(latencyBudgetOf(challenge([requirement(100)], [requirement(75)]))).toBe(100);
  });

  it("falls back to the optional one when that is the only budget there is", () => {
    expect(latencyBudgetOf(challenge([requirement(null)], [requirement(60)]))).toBe(60);
  });

  it("draws nothing when every requirement is graded under faults", () => {
    expect(latencyBudgetOf(challenge([requirement(null)], [requirement(null)]))).toBeNull();
    expect(latencyBudgetOf(challenge([requirement()], []))).toBeNull();
  });

  it("draws nothing for a challenge that never mentions latency", () => {
    expect(latencyBudgetOf(challenge([{ kind: "containsNode", label: { key: "challenge.x.requirement.1" } }]))).toBeNull();
  });
});
