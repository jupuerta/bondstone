import {
  AREA_ORDER,
  type AnalysisResult,
  type Area,
  type AreaState,
  type ArchitectureDto,
  type NodeType,
} from "../types/architecture";
import { connectedComponents, scoreBand, scoredAreas, type ScoreBand } from "../components/editor/panel/scoreExplain";

export type Tone = "cool" | "warm" | "hot";

export interface PostcardArea {
  area: Area;
  score: number | null;
  state: AreaState;
  tone: Tone | null;
}

export interface PostcardModel {
  name: string;
  score: number | null;
  band: ScoreBand;
  tone: Tone | null;
  areas: PostcardArea[];
  scored: number;
  total: number;
  critical: number;
  warning: number;
  passed: number;
  components: number;
  calls: number;
  version: number;
  link: string;
}

export function postcardModel({
  architecture,
  analysis,
  origin,
}: {
  architecture: ArchitectureDto;
  analysis: AnalysisResult;
  origin: string;
}): PostcardModel {
  const areas = scoredAreas(analysis);
  const wired = connectedComponents(
    architecture.nodes.map((node) => ({ id: node.id, type: node.type as NodeType })),
    architecture.connections,
  );

  return {
    name: architecture.name,
    score: analysis.score,
    band: scoreBand(analysis.score ?? 0, areas.scored, wired),
    tone: analysis.score === null ? null : scoreTone(analysis.score),
    areas: AREA_ORDER.map((area) => {
      const found = analysis.areas.find((candidate) => candidate.area === area);
      const score = found?.state === "Scored" ? (found.score ?? null) : null;

      return { area, score, state: found?.state ?? "NoRules", tone: score === null ? null : scoreTone(score) };
    }),
    scored: areas.scored,
    total: areas.total,
    critical: analysis.summary.critical,
    warning: analysis.summary.warning,
    passed: analysis.summary.passed,
    components: architecture.nodes.length,
    calls: architecture.connections.length,
    version: architecture.version,
    link: `${origin}/a/${architecture.shareToken}`,
  };
}

export function scoreTone(score: number): Tone {
  return score >= 90 ? "cool" : score >= 70 ? "warm" : "hot";
}
