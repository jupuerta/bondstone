import type { AnalysisResult, Area, ArchitectureSnapshot, ChallengeDetail } from "../../../types/architecture";
import { MECHANISMS, MECHANISM_IDS, type MechanismId } from "../../../domain/mechanisms";
import { CHANGE_VOICE, compareGraphs, type ComparableGraph, type Difference } from "./graphDiff";

export interface Move {
  what: "score" | "area";
  area?: Area;
  from: number;
  to: number;
}

export interface Journal {
  kept: boolean;
  changes: Difference[];
  moves: Move[];
  lesson: string;
  mechanisms: MechanismId[];
}

export interface JournalSources {
  definition: ChallengeDetail;
  startingPoint: ComparableGraph | null;
  current: ComparableGraph;
  first: ArchitectureSnapshot | null;
  analysis: AnalysisResult | null;
}

export function journalOf({ definition, startingPoint, current, first, analysis }: JournalSources): Journal {
  return {
    kept: startingPoint !== null,
    changes: startingPoint === null ? [] : compareGraphs(startingPoint, current, CHANGE_VOICE),
    moves: movesOf(definition.topic, first, analysis),
    lesson: lessonKey(definition.slug),
    mechanisms: mechanismsOf(definition.slug),
  };
}

export function lessonKey(slug: string): string {
  return `challenge.${slug}.lesson`;
}

export function mechanismsOf(slug: string): MechanismId[] {
  return MECHANISM_IDS.filter((id) => MECHANISMS[id].challenges.includes(slug));
}

function movesOf(topic: Area, first: ArchitectureSnapshot | null, analysis: AnalysisResult | null): Move[] {
  if (first === null || analysis === null) return [];
  if (first.scoringVersion !== analysis.scoringVersion) return [];

  const moves: Move[] = [];

  const topicBefore = first.areas?.[topic];
  const topicNow = analysis.areas.find((area) => area.area === topic);

  if (topicBefore !== undefined && topicNow?.state === "Scored" && topicNow.score !== null && topicBefore !== topicNow.score) {
    moves.push({ what: "area", area: topic, from: topicBefore, to: topicNow.score });
  }

  if (first.score !== null && analysis.score !== null && first.score !== analysis.score) {
    moves.push({ what: "score", from: first.score, to: analysis.score });
  }

  return moves;
}
