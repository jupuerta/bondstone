import type {
  AnalysisResult,
  ArchitectureCreatedDto,
  ArchitectureDto,
  ArchitectureSnapshot,
  Fault,
  FragilityMap,
  LoadCurve,
  SimulationResult,
  UpdateArchitectureRequest,
  UpdateArchitectureResult,
} from "../types/architecture";
import { downZones, nodeFaults, type ScenarioFault } from "../domain/scenarioUrl";

export const API_URL = (import.meta.env.VITE_API_URL as string | undefined) ?? "http://localhost:5012/api";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly key: string = "ui.error.request",
    public readonly args: Record<string, string | number> = {},
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export class NotFoundError extends ApiError {
  constructor() {
    super("Architecture not found.", 404, "ui.error.notFound");
    this.name = "NotFoundError";
  }
}

export class ValidationError extends ApiError {
  constructor(
    message: string,
    public readonly errors: string[] = [],
  ) {
    super(message, 400, "ui.error.validation");
    this.name = "ValidationError";
  }
}

export class ConflictError extends ApiError {
  constructor(
    message: string,
    public readonly currentVersion: number,
  ) {
    super(message, 409, "ui.error.conflict");
    this.name = "ConflictError";
  }
}

export class TooManyRequestsError extends ApiError {
  constructor(message: string) {
    super(message, 429, "ui.error.tooManyRequests");
    this.name = "TooManyRequestsError";
  }
}

export async function parseErrorResponse(response: Response): Promise<never> {
  if (response.status === 404) throw new NotFoundError();

  const body = await response.json().catch(() => null);

  if (response.status === 409) {
    throw new ConflictError(
      body?.message ?? "Architecture was modified by another session.",
      body?.currentVersion ?? 0,
    );
  }

  if (response.status === 400) {
    throw new ValidationError(body?.message ?? "Validation failed.", body?.errors ?? []);
  }

  if (response.status === 429) {
    throw new TooManyRequestsError(body?.message ?? "Too many requests.");
  }

  throw new ApiError(
    body?.message ?? `Request failed with status ${response.status}.`,
    response.status,
    "ui.error.request",
    { status: response.status },
  );
}

export async function createArchitecture(name: string): Promise<ArchitectureCreatedDto> {
  const response = await fetch(`${API_URL}/architectures`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function getArchitectureByShareToken(shareToken: string): Promise<ArchitectureDto> {
  const response = await fetch(`${API_URL}/architectures/${encodeURIComponent(shareToken)}`);

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function getArchitectureByEditToken(editToken: string): Promise<ArchitectureDto> {
  const response = await fetch(`${API_URL}/architectures/edit/${encodeURIComponent(editToken)}`);

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function forkArchitecture(
  shareToken: string,
  options?: { signal?: AbortSignal },
): Promise<ArchitectureCreatedDto> {
  const response = await fetch(`${API_URL}/architectures/${encodeURIComponent(shareToken)}/fork`, {
    method: "POST",
    signal: options?.signal,
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function updateArchitecture(
  editToken: string,
  request: UpdateArchitectureRequest,
  options?: { keepalive?: boolean },
): Promise<UpdateArchitectureResult> {
  const response = await fetch(`${API_URL}/architectures/edit/${encodeURIComponent(editToken)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
    keepalive: options?.keepalive,
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function exportArchitecture(
  id: string,
  faults?: Fault[],
  options?: { signal?: AbortSignal; acceptLanguage?: string },
): Promise<string> {
  const language: Record<string, string> = options?.acceptLanguage
    ? { "Accept-Language": options.acceptLanguage }
    : {};

  const response = await fetch(`${API_URL}/architectures/${id}/export`, {
    method: "POST",
    signal: options?.signal,
    ...(faults === undefined
      ? { headers: language }
      : {
          headers: { ...language, "Content-Type": "application/json" },
          body: JSON.stringify({ faults }),
        }),
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.text();
}

export async function analyzeArchitecture(
  shareToken: string,
  options?: { signal?: AbortSignal },
): Promise<AnalysisResult> {
  const response = await fetch(`${API_URL}/architectures/${encodeURIComponent(shareToken)}/analyze`, {
    method: "POST",
    signal: options?.signal,
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function simulateArchitecture(
  shareToken: string,
  faults: ScenarioFault[],
  options?: { signal?: AbortSignal },
): Promise<SimulationResult> {
  const response = await fetch(`${API_URL}/architectures/${encodeURIComponent(shareToken)}/simulate`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ faults: nodeFaults(faults), zones: downZones(faults) }),
    signal: options?.signal,
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function mapArchitectureFragility(
  shareToken: string,
  options?: { signal?: AbortSignal },
): Promise<FragilityMap> {
  const response = await fetch(`${API_URL}/architectures/${encodeURIComponent(shareToken)}/fragility`, {
    method: "POST",
    signal: options?.signal,
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function sweepArchitectureLoad(
  shareToken: string,
  options?: { signal?: AbortSignal },
): Promise<LoadCurve> {
  const response = await fetch(`${API_URL}/architectures/${encodeURIComponent(shareToken)}/load-curve`, {
    method: "POST",
    signal: options?.signal,
  });

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}

export async function getArchitectureHistory(shareToken: string): Promise<ArchitectureSnapshot[]> {
  const response = await fetch(`${API_URL}/architectures/${encodeURIComponent(shareToken)}/history`);

  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}
