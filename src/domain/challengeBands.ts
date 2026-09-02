import type { ChallengeSummary } from "../types/architecture";

export type Difficulty = "beginner" | "intermediate" | "advanced";

export const DIFFICULTIES: Difficulty[] = ["beginner", "intermediate", "advanced"];

export function bandLabelKey(difficulty: string): string {
  return `ui.home.band.${difficulty}`;
}

export function bandLeadKey(difficulty: string): string {
  return `ui.home.pathBandLead.${difficulty}`;
}

export interface Band {
  difficulty: Difficulty;
  challenges: ChallengeSummary[];
}

/** El catálogo ya llega ordenado por banda y por orden dentro de la banda; aquí solo se agrupa. */
export function groupByBand(challenges: ChallengeSummary[]): Band[] {
  return DIFFICULTIES.map((difficulty) => ({
    difficulty,
    challenges: challenges.filter((challenge) => challenge.difficulty === difficulty),
  })).filter((band) => band.challenges.length > 0);
}

/** «Reto 3 de 7»: la posición dentro de su banda. */
export function positionInBand(challenges: ChallengeSummary[], slug: string): { index: number; total: number } | null {
  const target = challenges.find((challenge) => challenge.slug === slug);
  if (!target) return null;
  const band = challenges.filter((challenge) => challenge.difficulty === target.difficulty);
  return { index: band.findIndex((challenge) => challenge.slug === slug) + 1, total: band.length };
}
