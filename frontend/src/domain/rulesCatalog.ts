import type { AnalysisResult, Area, LocalizedText, RuleState } from "../types/architecture";

/** Las 25 reglas del analizador, en el orden de las áreas. Copia del catálogo del frontend actual. */
export interface CatalogRule {
  code: string;
  area: Area;
  fix: boolean;
}

export const RULES: CatalogRule[] = [
  { code: "SPOF", area: "Availability", fix: false },
  { code: "TRAFFIC_REJECTED", area: "Availability", fix: false },

  { code: "MISSING_RESILIENCE", area: "Resilience", fix: true },
  { code: "EXTERNAL_NO_FALLBACK", area: "Resilience", fix: true },
  { code: "TIMEOUT_TOO_SHORT", area: "Resilience", fix: false },
  { code: "TIMEOUT_TOO_LONG", area: "Resilience", fix: true },
  { code: "RETRY_AMPLIFICATION", area: "Resilience", fix: false },
  { code: "QUEUE_NO_DLQ", area: "Resilience", fix: true },
  { code: "CACHE_AS_STORE", area: "Resilience", fix: false },
  { code: "SYNC_CHAIN", area: "Resilience", fix: false },
  { code: "ASYNC_NO_QUEUE", area: "Resilience", fix: true },
  { code: "IMPLAUSIBLE_DEPENDENCY", area: "Resilience", fix: false },

  { code: "CYCLE", area: "Coupling", fix: false },
  { code: "HIGH_COUPLING", area: "Coupling", fix: false },
  { code: "ORPHAN_NODE", area: "Coupling", fix: true },
  { code: "QUEUE_NO_CONSUMER", area: "Coupling", fix: false },
  { code: "SHARED_DATABASE", area: "Coupling", fix: false },

  { code: "UNDERSIZED_COMPONENT", area: "Scalability", fix: false },
  { code: "MISSING_CACHE", area: "Scalability", fix: true },
  { code: "MISSING_CDN", area: "Scalability", fix: false },

  { code: "UNAUTHENTICATED_ENTRY", area: "Security", fix: false },
  { code: "EXPOSED_DATABASE", area: "Security", fix: false },
  { code: "NO_RATE_LIMIT", area: "Security", fix: false },

  { code: "NO_OBSERVABILITY", area: "Observability", fix: true },

  { code: "OVERPROVISIONED", area: "Cost", fix: false },
];

export interface RuleReading extends CatalogRule {
  state: RuleState | null;
  findings: number;
  reason: LocalizedText | null;
}

/** Cruza el catálogo con un análisis: qué hizo cada regla sobre esa arquitectura. */
export function readRules(analysis: AnalysisResult | null): RuleReading[] {
  const coverage = new Map((analysis?.coverage ?? []).map((entry) => [entry.code, entry]));
  const fired = new Map<string, number>();
  for (const finding of analysis?.findings ?? []) {
    if (finding.severity === "Good") continue;
    fired.set(finding.code, (fired.get(finding.code) ?? 0) + 1);
  }

  return RULES.map((rule) => {
    const entry = coverage.get(rule.code);
    const state = entry?.state ?? null;
    return {
      ...rule,
      state,
      findings: state === "Fired" ? (fired.get(rule.code) ?? 0) : 0,
      reason: state === "NotApplicable" ? (entry?.reason ?? null) : null,
    };
  });
}
