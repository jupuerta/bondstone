import { describe, expect, it } from "vitest";
import type { AnalysisResult, RuleFinding } from "../../../types/architecture";
import { drawingCodes, drawingFindings } from "./drawingErrors";

const result = (coverage: AnalysisResult["coverage"]): AnalysisResult =>
  ({ scoringVersion: "v10", score: 0, areas: [], summary: { critical: 0, warning: 0, info: 0, passed: 0 }, findings: [], coverage }) as AnalysisResult;

const finding = (code: string, severity: RuleFinding["severity"] = "Critical"): RuleFinding => ({
  code,
  area: "Resilience",
  severity,
  message: { key: `finding.${code}.message` },
  explanation: { key: `finding.${code}.explanation` },
  elements: [],
});

describe("which codes are about the drawing", () => {
  it("takes them off the wire, so this file can never disagree with the rules", () => {
    const codes = drawingCodes(
      result([
        { code: "IMPLAUSIBLE_DEPENDENCY", area: "Resilience", state: "Fired", describesTheDrawing: true },
        { code: "SOMETHING_ELSE_ABOUT_THE_DRAWING", area: "Resilience", state: "Passed", describesTheDrawing: true },
        { code: "SPOF", area: "Availability", state: "Fired" },
      ]),
    );

    expect(codes).toEqual(new Set(["IMPLAUSIBLE_DEPENDENCY", "SOMETHING_ELSE_ABOUT_THE_DRAWING"]));
  });

  it("is empty for a payload recorded before the flag existed, rather than guessing", () => {
    expect(drawingCodes(result([{ code: "IMPLAUSIBLE_DEPENDENCY", area: "Resilience", state: "Fired" }])).size).toBe(0);
  });

  it("is empty when there is no analysis at all", () => {
    expect(drawingCodes(null).size).toBe(0);
  });
});

describe("what the band shows", () => {
  const codes = new Set(["IMPLAUSIBLE_DEPENDENCY"]);

  it("hoists only the drawing findings, leaving the architecture alone", () => {
    const all = [finding("SPOF"), finding("IMPLAUSIBLE_DEPENDENCY"), finding("MISSING_RESILIENCE")];

    expect(drawingFindings(all, codes).map((f) => f.code)).toEqual(["IMPLAUSIBLE_DEPENDENCY"]);
  });

  it("says nothing about a drawing rule that passed", () => {
    expect(drawingFindings([finding("IMPLAUSIBLE_DEPENDENCY", "Good")], codes)).toEqual([]);
  });

  it("keeps the order it was handed, which is already worst-first", () => {
    const all = [finding("IMPLAUSIBLE_DEPENDENCY"), finding("SPOF"), finding("IMPLAUSIBLE_DEPENDENCY", "Warning")];

    expect(drawingFindings(all, codes).map((f) => f.severity)).toEqual(["Critical", "Warning"]);
  });

  it("is empty when nothing is wrong with the drawing, so the band never appears", () => {
    expect(drawingFindings([finding("SPOF"), finding("CYCLE")], codes)).toEqual([]);
  });
});
