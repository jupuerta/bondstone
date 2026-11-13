import { describe, expect, it } from "vitest";
import { NO_LENS, lensQuestion, lensTabs, lensView } from "./lens";
import {
  AREA_ORDER,
  type AnalysisResult,
  type Area,
  type AreaScore,
  type FindingElement,
  type RuleFinding,
  type Severity,
} from "../../../types/architecture";
import { LOCALES, createTranslator } from "../../../i18n";

type FindingSpec = [Area, Severity, ...string[]];

function node(id: string): FindingElement {
  return { type: "Node", id };
}

function call(id: string): FindingElement {
  return { type: "Connection", id };
}

function finding([area, severity, ...ids]: FindingSpec, index: number): RuleFinding {
  return {
    code: `RULE_${index}`,
    area,
    severity,
    message: { key: `finding.RULE_${index}.message` },
    explanation: { key: `finding.RULE_${index}.explanation` },
    elements: ids.map((id) => (id.startsWith("c") ? call(id) : node(id))),
  };
}

function analysis(findings: FindingSpec[], areas: Partial<Record<Area, AreaScore>> = {}): AnalysisResult {
  return {
    scoringVersion: "test",
    score: 83,
    areas: AREA_ORDER.map(
      (area) => areas[area] ?? { area, score: 100, state: "Scored" },
    ),
    summary: { critical: 0, warning: 0, info: 0, passed: 0 },
    findings: findings.map(finding),
  };
}

describe("the canvas lens", () => {
  it("offers the seven areas in the panel's own order, plus the one that is today", () => {
    const tabs = lensTabs(analysis([]));

    expect(tabs).toHaveLength(8);
    expect(tabs[0].area).toBeNull();
    expect(tabs.slice(1).map((tab) => tab.area)).toEqual(AREA_ORDER);
    expect(tabs.slice(1).map((tab) => tab.label)).toEqual(AREA_ORDER.map((area) => `ui.area.${area}`));
  });

  it("quotes the score the analyzer published and never a score of its own", () => {
    const result = analysis([], {
      Resilience: { area: "Resilience", score: 61, state: "Scored" },
      Security: { area: "Security", score: null, state: "NotApplicable" },
    });

    const tabs = lensTabs(result);

    expect(tabs[0].score).toBe(result.score);
    expect(tabs.find((tab) => tab.area === "Resilience")?.score).toBe(61);
    expect(tabs.find((tab) => tab.area === "Security")?.score).toBeNull();
  });

  it("tints only what its own area points at, and leaves the drawing alone when it is off", () => {
    const result = analysis([
      ["Resilience", "Warning", "c1"],
      ["Security", "Critical", "n1"],
    ]);

    expect(lensView(result, null).ids.size).toBe(0);
    expect([...lensView(result, "Resilience").ids]).toEqual(["c1"]);
    expect([...lensView(result, "Security").ids]).toEqual(["n1"]);
  });

  it("tints calls, which is most of what Resilience has to point at", () => {
    const view = lensView(
      analysis([
        ["Resilience", "Warning", "c1", "c2"],
        ["Resilience", "Critical", "n1", "c3"],
      ]),
      "Resilience",
    );

    expect([...view.ids].filter((id) => id.startsWith("c"))).toEqual(["c1", "c2", "c3"]);
    expect(view.findings).toBe(2);
  });

  it("keeps the worst severity per element, the way the badges always did", () => {
    const view = lensView(
      analysis([
        ["Coupling", "Warning", "n1", "n2"],
        ["Coupling", "Critical", "n1"],
        ["Coupling", "Warning", "n1"],
      ]),
      "Coupling",
    );

    expect(view.severities.get("n1")).toBe("Critical");
    expect(view.severities.get("n2")).toBe("Warning");
  });

  it("never carries a passed check, which names no element to carry", () => {
    const view = lensView(analysis([["Observability", "Good"], ["Observability", "Warning", "n9"]]), "Observability");

    expect(view.findings).toBe(1);
    expect([...view.ids]).toEqual(["n9"]);
  });

  it("says why the drawing is grey, and says a different thing for each reason", () => {
    const clean = lensView(analysis([]), "Availability");
    const dimmed = lensView(analysis([["Availability", "Critical", "n1"]]), "Availability");

    const notApplicable = lensView(
      analysis([], { Security: { area: "Security", score: null, state: "NotApplicable" } }),
      "Security",
    );

    const noRules = lensView(
      analysis([], { Security: { area: "Security", score: null, state: "NoRules" } }),
      "Security",
    );

    expect(clean.note).toBe("ui.lens.clean");
    expect(dimmed.note).toBe("ui.lens.dimmed");
    expect(notApplicable.note).toBe("ui.lens.notApplicable");
    expect(noRules.note).toBe("ui.lens.noRules");
    expect(lensView(analysis([]), null).note).toBeNull();
  });

  it("says nothing at all until the first analysis lands", () => {
    expect(lensView(null, null)).toEqual(NO_LENS);
    expect(lensView(null, "Coupling").ids.size).toBe(0);
    expect(lensView(null, "Coupling").note).toBeNull();
    expect(lensView(null, "Coupling").analyzed).toBe(false);
    expect(lensView(analysis([]), "Coupling").analyzed).toBe(true);
    expect(lensTabs(null).map((tab) => tab.score)).toEqual([null, null, null, null, null, null, null, null]);
  });

  it("says something in both languages, with nothing left unfilled", () => {
    const notes = ["ui.lens.clean", "ui.lens.dimmed", "ui.lens.noRules", "ui.lens.notApplicable"];
    const chrome = ["ui.lens.title", "ui.lens.all", "ui.lens.allNote"];

    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const key of chrome) expect(t(key), `${key} in ${locale}`).not.toBe(key);

      for (const key of notes) {
        for (const count of [1, 4]) {
          const sentence = t(key, { count });
          expect(sentence, `${key} in ${locale}`).not.toBe(key);
          expect(sentence, `${key} in ${locale}`).not.toMatch(/[{}]/);
        }
      }

      for (const area of AREA_ORDER) {
        expect(t(lensQuestion(area)), `${area} in ${locale}`).not.toBe(lensQuestion(area));
        expect(t(`ui.area.${area}`), `${area} in ${locale}`).not.toBe(`ui.area.${area}`);
      }
    }
  });
});
