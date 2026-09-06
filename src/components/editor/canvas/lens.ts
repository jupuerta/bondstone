import { AREA_ORDER, type AnalysisResult, type Area, type AreaScore, type AreaState, type RuleFinding, type Severity } from "../../../types/architecture";

export type Problem = Exclude<Severity, "Good" | "Info">;

export interface LensTab {
  area: Area | null;
  label: string;
  score: number | null;
}

export interface LensView {
  area: Area | null;
  analyzed: boolean;
  ids: ReadonlySet<string>;
  severities: ReadonlyMap<string, Problem>;
  findings: number;
  state: AreaState | null;
  note: string | null;
}

const NO_IDS: ReadonlySet<string> = new Set();

export const NO_LENS: LensView = {
  area: null,
  analyzed: false,
  ids: NO_IDS,
  severities: new Map(),
  findings: 0,
  state: null,
  note: null,
};

export function lensView(result: AnalysisResult | null, area: Area | null): LensView {
  const analyzed = result !== null;
  const findings = problemsOf(result, area);
  const severities = worstByElement(findings);

  if (area === null) {
    return { area: null, analyzed, ids: NO_IDS, severities, findings: findings.length, state: null, note: null };
  }

  const state = areaOf(result, area)?.state ?? null;
  const ids = new Set(severities.keys());
  const note = analyzed ? noteFor(state, ids.size, findings.length) : null;

  return { area, analyzed, ids, severities, findings: findings.length, state, note };
}

export function lensTabs(result: AnalysisResult | null): LensTab[] {
  return [
    { area: null, label: "ui.lens.all", score: result?.score ?? null },
    ...AREA_ORDER.map((area) => ({ area, label: `ui.area.${area}`, score: areaOf(result, area)?.score ?? null })),
  ];
}

export function lensQuestion(area: Area): string {
  return `ui.area.question.${area}`;
}

function noteFor(state: AreaState | null, lit: number, findings: number): string {
  if (state === "NotApplicable") return "ui.lens.notApplicable";
  if (state !== "Scored") return "ui.lens.noRules";
  if (lit === 0 || findings === 0) return "ui.lens.clean";
  return "ui.lens.dimmed";
}

function problemsOf(result: AnalysisResult | null, area: Area | null): RuleFinding[] {
  return (result?.findings ?? []).filter((finding) => finding.severity !== "Good" && (area === null || finding.area === area));
}

function worstByElement(findings: readonly RuleFinding[]): ReadonlyMap<string, Problem> {
  const worst = new Map<string, Problem>();

  for (const finding of findings) {
    const severity = finding.severity;
    if (severity === "Good" || severity === "Info") continue;

    for (const element of finding.elements) {
      if (severity === "Critical" || !worst.has(element.id)) worst.set(element.id, severity);
    }
  }

  return worst;
}

function areaOf(result: AnalysisResult | null, area: Area): AreaScore | null {
  return result?.areas.find((candidate) => candidate.area === area) ?? null;
}
