import { describe, expect, it } from "vitest";
import { createTranslator } from "../../../i18n";
import type { AnalysisResult, AreaScore } from "../../../types/architecture";
import {
  areaFormula,
  averageFormula,
  belowScoreThreshold,
  connectedComponents,
  elementsOf,
  penaltyBar,
  scoreBand,
  scoredAreas,
} from "./scoreExplain";

const t = createTranslator("es");

const area = (overrides: Partial<AreaScore>): AreaScore => ({
  area: "Resilience",
  score: 100,
  state: "Scored",
  penalties: [],
  ...overrides,
});

describe("the word next to the number (QA §4)", () => {
  it("cuts at 95, 85 and 70", () => {
    expect(scoreBand(100)).toBe("solid");
    expect(scoreBand(95)).toBe("solid");
    expect(scoreBand(94)).toBe("fair");
    expect(scoreBand(85)).toBe("fair");
    expect(scoreBand(84)).toBe("fragile");
    expect(scoreBand(70)).toBe("fragile");
    expect(scoreBand(67)).toBe("serious");
    expect(scoreBand(0)).toBe("serious");
  });

  it("refuses to label a number computed over fewer than five areas (V14 §4.4, V15 D1)", () => {
    expect(scoreBand(98, 3)).toBe("unevaluated");
    expect(scoreBand(100, 0)).toBe("unevaluated");
    expect(scoreBand(60, 2)).toBe("unevaluated");
    expect(scoreBand(96, 4)).toBe("unevaluated");

    expect(scoreBand(98, 5)).toBe("solid");
    expect(scoreBand(98, 6)).toBe("solid");
    expect(scoreBand(67, 6)).toBe("serious");
  });

  it("refuses to label a drawing of fewer than three connected components (V15 D1)", () => {
    expect(scoreBand(97, 5, 2)).toBe("unevaluated");
    expect(scoreBand(100, 6, 2)).toBe("unevaluated");
    expect(scoreBand(100, 6, 0)).toBe("unevaluated");

    expect(scoreBand(97, 5, 3)).toBe("solid");
    expect(scoreBand(80, 6, 12)).toBe("fragile");

    expect(scoreBand(97, 4, 12)).toBe("unevaluated");
  });

  it("counts the components that take part in a flow, telemetry aside", () => {
    const nodes = [
      { id: "web", type: "Client" as const },
      { id: "api", type: "Api" as const },
      { id: "db", type: "Database" as const },
      { id: "obs", type: "Observability" as const },
      { id: "orphan", type: "Service" as const },
    ];
    const edges = [
      { sourceNodeId: "web", targetNodeId: "api" },
      { sourceNodeId: "api", targetNodeId: "db" },
      { sourceNodeId: "obs", targetNodeId: "api" },
      { sourceNodeId: "obs", targetNodeId: "db" },
    ];

    expect(connectedComponents(nodes, edges)).toBe(3);
    expect(connectedComponents(nodes, [])).toBe(0);
  });

  it("counts the areas the number was actually computed over", () => {
    const result = {
      score: 98,
      areas: [
        area({ area: "Availability", score: 95 }),
        area({ area: "Resilience", score: 100 }),
        area({ area: "Coupling", score: 100 }),
        area({ area: "Scalability", score: null, state: "NotApplicable" }),
        area({ area: "Security", score: null, state: "NotApplicable" }),
        area({ area: "Observability", score: null, state: "NoRules" }),
      ],
    } as unknown as AnalysisResult;

    expect(scoredAreas(result)).toEqual({ scored: 3, total: 6 });
  });
});

describe("the arithmetic written out (QA §4)", () => {
  it("reads as D1.1 computes it, halves and cap included", () => {
    const worstCaseResilience = area({
      score: 25,
      penalties: [
        { code: "MISSING_RESILIENCE", severity: "Critical", findings: 6, penalty: 45, capped: true },
        { code: "RETRY_AMPLIFICATION", severity: "Critical", findings: 1, penalty: 15, capped: false },
        { code: "SYNC_CHAIN", severity: "Critical", findings: 1, penalty: 15, capped: false },
      ],
    });

    expect(areaFormula(worstCaseResilience, t)).toBe(
      "100 − 45 MISSING_RESILIENCE ×6 (tope) − 15 RETRY_AMPLIFICATION − 15 SYNC_CHAIN = 25",
    );
  });

  it("formats the halves in the reader's language", () => {
    const twoSpofs = area({
      area: "Availability",
      score: 78,
      penalties: [{ code: "SPOF", severity: "Critical", findings: 2, penalty: 22.5, capped: false }],
    });

    expect(areaFormula(twoSpofs, t)).toBe("100 − 22,5 SPOF ×2 = 78");
    expect(areaFormula(twoSpofs, createTranslator("en"))).toBe("100 − 22.5 SPOF ×2 = 78");
  });

  it("says so when there is nothing to subtract, and nothing for an area without a number", () => {
    expect(areaFormula(area({}), t)).toBe("100 − 0 = 100");
    expect(areaFormula(area({ score: null, state: "NotApplicable" }), t)).toBeNull();
    expect(areaFormula(area({ score: 93, penalties: undefined }), t)).toBe("100 − 0 = 93");
  });

  it("writes the average over the scored areas only", () => {
    const result: AnalysisResult = {
      scoringVersion: "v6",
      score: 98,
      areas: [
        area({ area: "Availability", score: 100 }),
        area({ area: "Resilience", score: 100 }),
        area({ area: "Coupling", score: 100 }),
        area({ area: "Scalability", score: 100 }),
        area({ area: "Security", score: 95 }),
        area({ area: "Observability", score: 93 }),
      ],
      summary: { critical: 0, warning: 3, info: 0, passed: 11 },
      findings: [],
    };

    expect(averageFormula(result, t)).toEqual({ formula: "(100 + 100 + 100 + 100 + 95 + 93) / 6 = 98", excluded: 0 });

    const partial: AnalysisResult = {
      ...result,
      score: 99,
      areas: [...result.areas.slice(0, 5), area({ area: "Observability", score: null, state: "NotApplicable" })],
    };

    expect(averageFormula(partial, t)).toEqual({ formula: "(100 + 100 + 100 + 100 + 95) / 5 = 99", excluded: 1 });
    expect(averageFormula({ ...result, score: null, areas: [] }, t)).toBeNull();
  });

  it("says how many areas the drawing is short of when there is no score (B12)", () => {
    const noScore: AnalysisResult = {
      scoringVersion: "v6",
      score: null,
      areas: [
        area({ area: "Coupling", score: 100 }),
        area({ area: "Availability", score: null, state: "NotApplicable" }),
        area({ area: "Resilience", score: null, state: "NotApplicable" }),
        area({ area: "Scalability", score: null, state: "NotApplicable" }),
        area({ area: "Security", score: null, state: "NotApplicable" }),
        area({ area: "Observability", score: null, state: "NoRules" }),
      ],
      summary: { critical: 0, warning: 0, info: 0, passed: 1 },
      findings: [],
    };

    expect(averageFormula(noScore, t)).toBeNull();
    expect(belowScoreThreshold(noScore)).toBe(1);
    expect(belowScoreThreshold({ ...noScore, areas: [] })).toBe(0);
    expect(belowScoreThreshold({ ...noScore, score: 98 })).toBeNull();
  });
});

describe("the arithmetic drawn (V17 D4)", () => {
  it("puts what the area kept at the head and a segment per term after it", () => {
    const bar = penaltyBar(
      area({
        score: 35,
        penalties: [
          { code: "MISSING_RESILIENCE", severity: "Critical", findings: 6, penalty: 45, capped: true },
          { code: "RETRY_AMPLIFICATION", severity: "Critical", findings: 1, penalty: 15, capped: false },
          { code: "SYNC_CHAIN", severity: "Warning", findings: 1, penalty: 5, capped: false },
        ],
      }),
    )!;

    expect(bar.remaining).toBe(35);
    expect(bar.segments.map((segment) => [segment.line.code, segment.start, segment.width])).toEqual([
      ["MISSING_RESILIENCE", 35, 45],
      ["RETRY_AMPLIFICATION", 80, 15],
      ["SYNC_CHAIN", 95, 5],
    ]);
    expect(bar.floor).toBe(100);
    expect(bar.overshoot).toBe(0);
  });

  it("draws the subtraction and not the rounded number", () => {
    const bar = penaltyBar(
      area({
        area: "Availability",
        score: 68,
        penalties: [{ code: "SPOF", severity: "Critical", findings: 4, penalty: 32.5, capped: false }],
      }),
    )!;

    expect(bar.remaining).toBe(67.5);
    expect(bar.segments[0].start).toBe(67.5);
  });

  it("stretches the track rather than dropping a code when the subtraction passes 100", () => {
    const bar = penaltyBar(
      area({
        score: 0,
        penalties: [
          { code: "A_CODE", severity: "Critical", findings: 6, penalty: 45, capped: true },
          { code: "B_CODE", severity: "Critical", findings: 6, penalty: 45, capped: true },
          { code: "C_CODE", severity: "Critical", findings: 6, penalty: 45, capped: true },
        ],
      }),
    )!;

    expect(bar.remaining).toBe(0);
    expect(bar.segments.map((segment) => segment.start)).toEqual([0, 33.33, 66.67]);
    expect(bar.segments.every((segment) => segment.width === 33.33)).toBe(true);
    expect(bar.floor).toBe(74.07);
    expect(bar.overshoot).toBe(35);
  });

  it("draws nothing where there is nothing to draw", () => {
    expect(penaltyBar(area({}))).toBeNull();
    expect(penaltyBar(area({ score: null, state: "NotApplicable" }))).toBeNull();
    expect(penaltyBar(area({ score: null, state: "NoRules" }))).toBeNull();
    expect(penaltyBar(area({ score: 93, penalties: undefined }))).toBeNull();
  });

  it("gathers everything one term was charged for, once each and in order", () => {
    const findings = [
      { elements: [{ type: "Node" as const, id: "api" }, { type: "Connection" as const, id: "api-db" }] },
      { elements: [{ type: "Node" as const, id: "db" }, { type: "Node" as const, id: "api" }] },
    ];

    expect(elementsOf(findings)).toEqual([
      { type: "Node", id: "api" },
      { type: "Connection", id: "api-db" },
      { type: "Node", id: "db" },
    ]);
    expect(elementsOf([])).toEqual([]);
  });
});
