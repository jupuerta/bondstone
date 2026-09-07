import type { RuleFinding } from "../../../types/architecture";
import type { PatternId } from "../patterns/patterns";

export const PATTERN_FOR_SUGGESTION: Record<string, PatternId> = {
  "finding.SPOF.suggestion.instances": "balanced-instances",
  "finding.SPOF.suggestion.entryPoint": "balanced-instances",
  "finding.UNDERSIZED_COMPONENT.suggestion.addInstances": "balanced-instances",
  "finding.UNDERSIZED_COMPONENT.suggestion.shareWork": "balanced-instances",

  "finding.SYNC_CHAIN.long.suggestion": "queue-between",
  "finding.ASYNC_NO_QUEUE.unbuffered.suggestion": "queue-between",
  "finding.MISSING_RESILIENCE.telemetryPush.suggestion": "queue-between",
  "finding.MISSING_RESILIENCE.exposedToSpof.suggestion.write": "queue-between",
  "finding.MISSING_RESILIENCE.noTimeoutAndSpof.suggestion.write": "queue-between",

  "finding.SYNC_CHAIN.long.suggestion.read": "cache-in-front",
  "finding.MISSING_CACHE.direct.suggestion": "cache-in-front",
  "finding.UNDERSIZED_COMPONENT.suggestion.database": "cache-in-front",

  "finding.EXTERNAL_NO_FALLBACK.unprotected.suggestion": "external-fallback",
  "finding.EXTERNAL_NO_FALLBACK.noTimeout.suggestion": "external-fallback",
  "finding.MISSING_RESILIENCE.exposedToSpof.suggestion": "external-fallback",
  "finding.MISSING_RESILIENCE.noTimeoutAndSpof.suggestion": "external-fallback",
  "finding.SPOF.suggestion.external": "external-fallback",
  "finding.SPOF.suggestion.identityProvider": "external-fallback",

  "finding.EXPOSED_DATABASE.reachable.suggestion": "gate-in-front",
  "finding.UNAUTHENTICATED_ENTRY.unguarded.suggestion": "gate-in-front",
  "finding.UNAUTHENTICATED_ENTRY.open.suggestion": "gate-in-front",

  "finding.MISSING_RESILIENCE.noTimeout.suggestion": "bounded-call",

  "finding.QUEUE_NO_DLQ.missing.suggestion": "dead-letter-branch",

  "finding.NO_RATE_LIMIT.unlimited.suggestion": "capped-entry",
  "finding.NO_RATE_LIMIT.unlimited.suggestion.api": "capped-entry",
};

export const NO_PATTERN: string[] = [
  "finding.SPOF.suggestion.database",
  "finding.SPOF.suggestion.queue",
  "finding.SPOF.suggestion.topic",
  "finding.SPOF.suggestion.objectStorage",
  "finding.SPOF.suggestion.cache",
  "finding.SPOF.suggestion.sameZone",
  "finding.UNDERSIZED_COMPONENT.suggestion.queue",
  "finding.UNDERSIZED_COMPONENT.suggestion.objectStorage",
  "finding.UNDERSIZED_COMPONENT.suggestion.databaseWrites",
  "finding.UNAUTHENTICATED_ENTRY.undeclared.suggestion",
  "finding.UNAUTHENTICATED_ENTRY.contradicted.suggestion",

  "finding.TIMEOUT_TOO_SHORT.expires.suggestion",
  "finding.TIMEOUT_TOO_LONG.unbounded.suggestion",
  "finding.RETRY_AMPLIFICATION.suggestion",
  "finding.QUEUE_NO_CONSUMER.unconsumed.suggestion",
  "finding.QUEUE_NO_CONSUMER.unconsumed.suggestion.topic",
  "finding.QUEUE_NO_CONSUMER.backwards.suggestion",
  "finding.QUEUE_NO_CONSUMER.backwards.suggestion.topic",
  "finding.CACHE_AS_STORE.noOrigin.suggestion",
  "finding.NO_RATE_LIMIT.unlimited.suggestion.service",
  "finding.TRAFFIC_REJECTED.shedding.suggestion",
];

export function patternForFinding(finding: RuleFinding): PatternId | null {
  if (!finding.suggestion) return null;

  return PATTERN_FOR_SUGGESTION[finding.suggestion.key] ?? null;
}
