import type { AnalysisResult, AreaScore, FindingElement, NodeType, PenaltyLine } from "../../../types/architecture";
import type { Translator } from "../../../i18n";

export type ScoreBand = "solid" | "fair" | "fragile" | "serious" | "unevaluated";

export const MIN_AREAS_TO_LABEL = 5;

export const MIN_COMPONENTS_TO_LABEL = 3;

export function scoreBand(
  score: number,
  scoredAreas: number = MIN_AREAS_TO_LABEL,
  components: number = MIN_COMPONENTS_TO_LABEL,
): ScoreBand {
  if (scoredAreas < MIN_AREAS_TO_LABEL || components < MIN_COMPONENTS_TO_LABEL) return "unevaluated";
  if (score >= 95) return "solid";
  if (score >= 85) return "fair";
  if (score >= 70) return "fragile";
  return "serious";
}

export function connectedComponents(
  nodes: ReadonlyArray<{ id: string; type: NodeType }>,
  edges: ReadonlyArray<{ sourceNodeId: string; targetNodeId: string }>,
): number {
  const wired = new Set<string>();
  for (const edge of edges) {
    wired.add(edge.sourceNodeId);
    wired.add(edge.targetNodeId);
  }
  return nodes.filter((node) => node.type !== "Observability" && wired.has(node.id)).length;
}

export function scoredAreas(result: AnalysisResult): { scored: number; total: number } {
  return {
    scored: result.areas.filter((area) => area.state === "Scored" && area.score !== null).length,
    total: result.areas.length,
  };
}

export function areaFormula(area: AreaScore, t: Translator): string | null {
  if (area.state !== "Scored" || area.score === null) return null;

  const terms = (area.penalties ?? []).map((line) => {
    const term =
      line.findings > 1
        ? t("ui.analysis.formula.penaltyRepeated", { penalty: line.penalty, code: line.code, count: line.findings })
        : t("ui.analysis.formula.penalty", { penalty: line.penalty, code: line.code });
    return line.capped ? `${term} ${t("ui.analysis.formula.capped")}` : term;
  });

  if (terms.length === 0) return t("ui.analysis.formula.nothingToSubtract", { score: area.score });

  return `100 ${terms.join(" ")} = ${area.score}`;
}

export const MIN_AREAS_TO_SCORE = 4;

export function belowScoreThreshold(result: AnalysisResult): number | null {
  if (result.score !== null) return null;

  const scored = result.areas.filter((area) => area.state === "Scored" && area.score !== null).length;
  return scored < MIN_AREAS_TO_SCORE ? scored : null;
}

export function averageFormula(result: AnalysisResult, t: Translator): { formula: string; excluded: number } | null {
  const scored = result.areas.filter((area) => area.state === "Scored" && area.score !== null);
  if (scored.length === 0 || result.score === null) return null;

  return {
    formula: t("ui.analysis.formula.average", {
      sum: scored.map((area) => String(area.score)).join(" + "),
      count: scored.length,
      score: result.score,
    }),
    excluded: result.areas.length - scored.length,
  };
}

export interface PenaltySegment {
  line: PenaltyLine;
  start: number;
  width: number;
}

export interface PenaltyBar {
  remaining: number;
  segments: PenaltySegment[];
  floor: number;
  overshoot: number;
}

export function penaltyBar(area: AreaScore): PenaltyBar | null {
  if (area.state !== "Scored" || area.score === null) return null;

  const lines = area.penalties ?? [];
  if (lines.length === 0) return null;

  const subtracted = lines.reduce((total, line) => total + line.penalty, 0);
  const track = Math.max(100, subtracted);
  const kept = Math.max(0, 100 - subtracted);

  let cursor = kept;
  const segments = lines.map((line) => {
    const segment: PenaltySegment = { line, start: share(cursor, track), width: share(line.penalty, track) };
    cursor += line.penalty;
    return segment;
  });

  return {
    remaining: share(kept, track),
    segments,
    floor: share(100, track),
    overshoot: Math.max(0, subtracted - 100),
  };
}

export function elementsOf(findings: ReadonlyArray<{ elements: FindingElement[] }>): FindingElement[] {
  const seen = new Set<string>();
  const elements: FindingElement[] = [];

  for (const finding of findings) {
    for (const element of finding.elements) {
      const key = `${element.type}:${element.id}`;
      if (seen.has(key)) continue;
      seen.add(key);
      elements.push(element);
    }
  }

  return elements;
}

function share(value: number, track: number): number {
  return Math.round((value / track) * 10000) / 100;
}
