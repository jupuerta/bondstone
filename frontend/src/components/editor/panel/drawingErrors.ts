import type { AnalysisResult, RuleFinding } from "../../../types/architecture";

export function drawingCodes(result: AnalysisResult | null): ReadonlySet<string> {
  return new Set((result?.coverage ?? []).filter((entry) => entry.describesTheDrawing).map((entry) => entry.code));
}

export function drawingFindings<T extends RuleFinding>(findings: readonly T[], codes: ReadonlySet<string>): T[] {
  return findings.filter((finding) => codes.has(finding.code) && finding.severity !== "Good");
}
