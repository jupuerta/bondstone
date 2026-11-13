import { describe, expect, it } from "vitest";
import en from "../../../i18n/en.json";
import es from "../../../i18n/es.json";
import type { AnalysisResult, AppliedFix, Area, AreaScore } from "../../../types/architecture";
import { fixDelta, scoreSnapshot, whatWasDone, type ScoreSnapshot } from "./fixReceipt";

const SCORING = "v6";

function snapshot(areas: Partial<Record<Area, number>>, score: number | null, scoringVersion = SCORING): ScoreSnapshot {
  return { scoringVersion, score, areas };
}

function analysis(areas: AreaScore[], score: number | null): AnalysisResult {
  return {
    scoringVersion: SCORING,
    score,
    areas,
    summary: { critical: 0, warning: 0, info: 0, passed: 0 },
    findings: [],
  };
}

function fix(overrides: Partial<AppliedFix> = {}): AppliedFix {
  return {
    kind: "makeEdgeAsynchronous",
    element: { type: "Connection", id: "c1" },
    label: { key: "fix.MISSING_RESILIENCE.makeAsynchronous", args: null },
    ...overrides,
  };
}

describe("whatWasDone", () => {
  it("is the button's own key, in the past", () => {
    expect(whatWasDone(fix()).key).toBe("fix.MISSING_RESILIENCE.makeAsynchronous.done");
  });

  it("carries the button's arguments through untouched", () => {
    const label = { key: "fix.TIMEOUT_TOO_LONG.lower", args: { seconds: 5 } };
    expect(whatWasDone(fix({ label }))).toEqual({ key: "fix.TIMEOUT_TOO_LONG.lower.done", args: { seconds: 5 } });
  });
});

describe("scoreSnapshot", () => {
  it("keeps only the areas that were scored", () => {
    const result = analysis(
      [
        { area: "Resilience", score: 70, state: "Scored" },
        { area: "Security", score: null, state: "NotApplicable" },
        { area: "Observability", score: null, state: "NoRules" },
      ],
      70,
    );

    expect(scoreSnapshot(result)).toEqual({ scoringVersion: SCORING, score: 70, areas: { Resilience: 70 } });
  });

  it("has nothing to say about an analysis that has not arrived", () => {
    expect(scoreSnapshot(null)).toBeNull();
  });
});

describe("fixDelta", () => {
  it("reports the jump in the score", () => {
    const delta = fixDelta(snapshot({ Resilience: 70 }, 78), snapshot({ Resilience: 85 }, 85));

    expect(delta.from).toBe(78);
    expect(delta.to).toBe(85);
    expect(delta.points).toBe(7);
  });

  it("names the area that moved, biggest climb first", () => {
    const delta = fixDelta(
      snapshot({ Resilience: 70, Availability: 90 }, 80),
      snapshot({ Resilience: 85, Availability: 95 }, 90),
    );

    expect(delta.moved).toEqual([
      { area: "Resilience", from: 70, to: 85, delta: 15 },
      { area: "Availability", from: 90, to: 95, delta: 5 },
    ]);
  });

  it("reports a drop too, which is the half C11 never had to", () => {
    const delta = fixDelta(
      snapshot({ Scalability: 60, Observability: 100 }, 80),
      snapshot({ Scalability: 100, Observability: 80 }, 90),
    );

    expect(delta.moved).toEqual([
      { area: "Scalability", from: 60, to: 100, delta: 40 },
      { area: "Observability", from: 100, to: 80, delta: -20 },
    ]);
  });

  it("says nothing about an area only one end measured", () => {
    const delta = fixDelta(snapshot({ Resilience: 70 }, 70), snapshot({ Resilience: 70, Security: 90 }, 80));

    expect(delta.moved).toEqual([]);
    expect(delta.points).toBe(10);
  });

  it("never subtracts two scoring versions from each other", () => {
    const delta = fixDelta(snapshot({ Resilience: 70 }, 70, "v6"), snapshot({ Resilience: 90 }, 90, "v7"));

    expect(delta.moved).toEqual([]);
    expect(delta.points).toBeNull();
  });

  it("reports a score that did not move, which is a lesson of its own", () => {
    const delta = fixDelta(snapshot({ Resilience: 70 }, 78), snapshot({ Resilience: 70 }, 78));

    expect(delta.points).toBe(0);
    expect(delta.moved).toEqual([]);
  });

  it("has no jump to report where there was no score", () => {
    const delta = fixDelta(snapshot({}, null), snapshot({ Resilience: 70 }, 70));

    expect(delta.points).toBeNull();
    expect(delta.moved).toEqual([]);
  });
});

const PLACEHOLDERS = /\{([A-Za-z][A-Za-z0-9]*)(?::[A-Za-z][A-Za-z0-9]*)?\}/g;

const argumentsOf = (sentence: string | undefined) =>
  [...(sentence ?? "").matchAll(PLACEHOLDERS)].map(([, name]) => name).sort();

const buttons = Object.keys(es).filter((key) => key.startsWith("fix.") && !key.endsWith(".done"));

describe("the sentence a receipt says (V17 D5)", () => {
  it("exists in both languages for every button there is", () => {
    expect(buttons.length).toBeGreaterThan(0);

    for (const button of buttons) {
      expect(`${button}.done` in es, `${button}.done missing from es.json`).toBe(true);
      expect(`${button}.done` in en, `${button}.done missing from en.json`).toBe(true);
    }
  });

  it("asks for nothing the button was not already given", () => {
    const catalogue = es as Record<string, string | { one: string; other: string }>;
    const sentence = (key: string) => {
      const entry = catalogue[key];
      return typeof entry === "string" ? entry : undefined;
    };

    for (const button of buttons) {
      const asked = argumentsOf(sentence(`${button}.done`));
      const given = argumentsOf(sentence(button));

      expect(asked.filter((name) => !given.includes(name)), `${button}.done asks for more`).toEqual([]);
    }
  });
});
