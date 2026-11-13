import type {
  ArchitectureCreatedDto,
  ArchitectureDto,
  ChallengeSolutionPreview,
  ChallengeDetail,
  ChallengeResult,
  ChallengeSummary,
} from "../types/architecture";
import { API_URL, parseErrorResponse } from "./architectureClient";

export async function listChallenges(): Promise<ChallengeSummary[]> {
  const response = await fetch(`${API_URL}/challenges`);
  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function getChallenge(slug: string): Promise<ChallengeDetail> {
  const response = await fetch(`${API_URL}/challenges/${encodeURIComponent(slug)}`);
  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function startChallenge(slug: string, name?: string): Promise<ArchitectureCreatedDto> {
  const response = await fetch(`${API_URL}/challenges/${encodeURIComponent(slug)}/start`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function evaluateChallenge(
  slug: string,
  shareToken: string,
  options?: { signal?: AbortSignal },
): Promise<ChallengeResult> {
  const response = await fetch(`${API_URL}/challenges/${encodeURIComponent(slug)}/evaluate`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ shareToken }),
    signal: options?.signal,
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function previewChallengeSolution(
  slug: string,
  options?: { signal?: AbortSignal },
): Promise<ChallengeSolutionPreview> {
  const response = await fetch(`${API_URL}/challenges/${encodeURIComponent(slug)}/solution/preview`, {
    signal: options?.signal,
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function openChallengeSolution(slug: string, name?: string): Promise<ArchitectureDto> {
  const response = await fetch(`${API_URL}/challenges/${encodeURIComponent(slug)}/solution`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}
