import type { AnalysisResult, AppliedFix, Area, ArchitectureSnapshot, LocalizedText } from "../../../types/architecture";
import { areasImproved } from "../../../domain/areasImproved";

export interface ScoreSnapshot {
  scoringVersion: string;
  score: number | null;
  areas: Partial<Record<Area, number>>;
}

export interface AreaMove {
  area: Area;
  from: number;
  to: number;
  delta: number;
}

export interface FixDelta {
  from: number | null;
  to: number | null;
  points: number | null;
  moved: AreaMove[];
}

export interface FixReceipt {
  area: Area;
  code: string;
  done: LocalizedText;
  before: ScoreSnapshot | null;
  after: ScoreSnapshot | null;
}

export function whatWasDone(fix: AppliedFix): LocalizedText {
  return { key: `${fix.label.key}.done`, args: fix.label.args };
}

export function scoreSnapshot(result: AnalysisResult | null): ScoreSnapshot | null {
  if (!result) return null;

  const areas: Partial<Record<Area, number>> = {};
  for (const area of result.areas) {
    if (area.state === "Scored" && area.score !== null) areas[area.area] = area.score;
  }

  return { scoringVersion: result.scoringVersion, score: result.score, areas };
}

export function fixDelta(before: ScoreSnapshot, after: ScoreSnapshot): FixDelta {
  const climbs = areasImproved([asSnapshot(before, 1), asSnapshot(after, 2)]);
  const drops = areasImproved([asSnapshot(after, 1), asSnapshot(before, 2)]);

  return {
    from: before.score,
    to: after.score,
    points:
      before.score === null || after.score === null || before.scoringVersion !== after.scoringVersion
        ? null
        : after.score - before.score,
    moved: [
      ...climbs.map((gain) => ({ area: gain.area, from: gain.from, to: gain.to, delta: gain.gain })),
      ...drops.map((gain) => ({ area: gain.area, from: gain.to, to: gain.from, delta: -gain.gain })),
    ],
  };
}

function asSnapshot(snapshot: ScoreSnapshot, version: number): ArchitectureSnapshot {
  return {
    version,
    scoringVersion: snapshot.scoringVersion,
    score: snapshot.score,
    summary: null,
    createdAt: "",
    areas: snapshot.areas,
  };
}
