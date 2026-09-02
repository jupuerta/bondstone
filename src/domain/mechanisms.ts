/* Copia del taller del frontend actual, sin los dibujos de patrón (que son del editor). */

/* ── the eleven ──────────────────────────────────────────────────────────── */

export type MechanismId =
  | "timeout"
  | "retries"
  | "circuitBreaker"
  | "fallback"
  | "queue"
  | "deadLetterQueue"
  | "cache"
  | "readReplicas"
  | "rateLimit"
  | "healthCheck"
  | "instances";

export type MechanismGroupId = "call" | "between" | "load" | "door";

export interface MechanismSheet {
  id: MechanismId;
  formula: boolean;
  simulated: boolean;
  rules: string[];
  challenges: string[];
}

export const MECHANISMS: Record<MechanismId, MechanismSheet> = {
  timeout: {
    id: "timeout",
    formula: true,
    simulated: true,
    rules: ["TIMEOUT_TOO_SHORT", "TIMEOUT_TOO_LONG", "MISSING_RESILIENCE"],
    challenges: ["thirty-seconds", "payments-friday", "timeout-was-enough"],
  },

  retries: {
    id: "retries",
    formula: true,
    simulated: true,
    rules: ["RETRY_AMPLIFICATION"],
    challenges: ["retry-storm"],
  },

  circuitBreaker: {
    id: "circuitBreaker",
    formula: true,
    simulated: true,
    rules: ["MISSING_RESILIENCE"],
    challenges: ["retry-storm"],
  },

  fallback: {
    id: "fallback",
    formula: true,
    simulated: true,
    rules: ["EXTERNAL_NO_FALLBACK", "MISSING_RESILIENCE"],
    challenges: ["already-signed-in", "third-party-outage"],
  },

  queue: {
    id: "queue",
    formula: true,
    simulated: true,
    rules: ["SYNC_CHAIN", "ASYNC_NO_QUEUE", "MISSING_RESILIENCE", "QUEUE_NO_CONSUMER"],
    challenges: ["email-can-wait", "nobody-drains-it", "backlog"],
  },

  deadLetterQueue: {
    id: "deadLetterQueue",
    formula: false,
    simulated: false,
    rules: ["QUEUE_NO_DLQ"],
    challenges: ["nobody-drains-it"],
  },

  cache: {
    id: "cache",
    formula: true,
    simulated: true,
    rules: ["MISSING_CACHE", "CACHE_AS_STORE"],
    challenges: ["database-overload", "latency-budget"],
  },

  readReplicas: {
    id: "readReplicas",
    formula: true,
    simulated: true,
    rules: ["UNDERSIZED_COMPONENT", "MISSING_CACHE"],
    challenges: ["backlog", "million-a-day"],
  },

  rateLimit: {
    id: "rateLimit",
    formula: true,
    simulated: true,
    rules: ["NO_RATE_LIMIT", "TRAFFIC_REJECTED"],
    challenges: ["million-a-day"],
  },

  healthCheck: {
    id: "healthCheck",
    formula: true,
    simulated: true,
    rules: [],
    challenges: ["single-point-of-failure", "both-in-the-same-zone"],
  },

  instances: {
    id: "instances",
    formula: true,
    simulated: true,
    rules: ["SPOF", "UNDERSIZED_COMPONENT"],
    challenges: ["single-point-of-failure", "it-does-not-fit", "backlog", "both-in-the-same-zone"],
  },
};

export const MECHANISM_GROUPS: Array<{ id: MechanismGroupId; mechanisms: MechanismId[] }> = [
  { id: "call", mechanisms: ["timeout", "retries", "circuitBreaker", "fallback"] },
  { id: "between", mechanisms: ["queue", "deadLetterQueue"] },
  { id: "load", mechanisms: ["cache", "readReplicas", "instances"] },
  { id: "door", mechanisms: ["rateLimit", "healthCheck"] },
];

export const MECHANISM_IDS = MECHANISM_GROUPS.flatMap((group) => group.mechanisms);

/* ── what each band asks the catalogue for ───────────────────────────────── */

export type Band = "where" | "problem" | "model" | "formula" | "avoid";

export function mechanismLabel(id: MechanismId): string {
  return `ui.mechanism.${id}`;
}

export function mechanismBand(id: MechanismId, band: Band): string {
  return `ui.mechanism.${id}.${band}`;
}

export function mechanismHref(id: MechanismId): string {
  return `/mechanisms#${id}`;
}

export function ruleHref(code: string): string {
  return `/rules#${code}`;
}

export function challengeHref(slug: string): string {
  return `/c/${slug}`;
}

export function scores(sheet: MechanismSheet): boolean {
  return sheet.rules.length > 0;
}
