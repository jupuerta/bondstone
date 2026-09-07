import type { PatternId } from "../patterns/patterns";
import type { MechanismId } from "../../../domain/mechanisms";

export type TermId =
  | "synchronous"
  | "timeout"
  | "retries"
  | "circuitBreaker"
  | "fallback"
  | "entryPoint"
  | "deadLetterQueue"
  | "hitRatio"
  | "rateLimit"
  | "readReplicas"
  | "instances"
  | "loadAmplification"
  | "partition";

export interface Term {
  id: TermId;
  pattern?: PatternId;
  mechanism?: MechanismId;
}

export const GLOSSARY: Record<TermId, Term> = {
  synchronous: { id: "synchronous", pattern: "queue-between", mechanism: "queue" },
  timeout: { id: "timeout", pattern: "bounded-call", mechanism: "timeout" },
  retries: { id: "retries", pattern: "retried-call", mechanism: "retries" },
  circuitBreaker: { id: "circuitBreaker", pattern: "breaker-on-call", mechanism: "circuitBreaker" },
  fallback: { id: "fallback", pattern: "external-fallback", mechanism: "fallback" },
  entryPoint: { id: "entryPoint", pattern: "gate-in-front" },
  deadLetterQueue: { id: "deadLetterQueue", pattern: "dead-letter-branch", mechanism: "deadLetterQueue" },
  hitRatio: { id: "hitRatio", pattern: "cache-in-front", mechanism: "cache" },
  rateLimit: { id: "rateLimit", pattern: "capped-entry", mechanism: "rateLimit" },
  readReplicas: { id: "readReplicas", pattern: "read-replicas", mechanism: "readReplicas" },
  instances: { id: "instances", pattern: "balanced-instances", mechanism: "instances" },
  loadAmplification: { id: "loadAmplification", pattern: "retried-call" },
  partition: { id: "partition" },
};

export const TERM_IDS = Object.keys(GLOSSARY) as TermId[];

export function termLabel(id: TermId): string {
  return `ui.term.${id}`;
}

export function termDefinition(id: TermId): string {
  return `ui.term.${id}.definition`;
}

