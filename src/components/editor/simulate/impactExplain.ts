import type { FindingElement, SimulationMetrics } from "../../../types/architecture";
import type { Translator } from "../../../i18n";

export type ImpactMetric =
  | "blastRadius"
  | "requestsAffected"
  | "componentsDown"
  | "componentsDegraded"
  | "entryPoints"
  | "latency"
  | "amplification"
  | "loadAmplification"
  | "backlog"
  | "bottleneck";

export const IMPACT_METRICS: readonly ImpactMetric[] = [
  "blastRadius",
  "requestsAffected",
  "componentsDown",
  "componentsDegraded",
  "entryPoints",
  "latency",
  "amplification",
  "loadAmplification",
  "backlog",
  "bottleneck",
];

export interface MetricArithmetic {
  formula: string;
  why: string;
}

export function metricArithmetic(
  metric: ImpactMetric,
  metrics: SimulationMetrics,
  t: Translator,
  nameOf: (element: FindingElement) => string,
): MetricArithmetic | null {
  const line = (formula: string): MetricArithmetic => ({
    formula,
    why: t(`ui.impact.formula.${metric}.why`),
  });

  const key = `ui.impact.formula.${metric}`;
  const total = metrics.componentsTotal;

  switch (metric) {
    case "blastRadius":
      if (total === 0) return null;
      return line(
        t(key, {
          down: metrics.componentsDown,
          degraded: metrics.componentsDegraded,
          total,
          value: metrics.blastRadius,
        }),
      );

    case "requestsAffected":
      return line(t(key, { served: 1 - metrics.requestsAffected, value: metrics.requestsAffected }));

    case "componentsDown":
      if (total === 0) return null;
      return line(t(key, { value: metrics.componentsDown, total, share: metrics.componentsDown / total }));

    case "componentsDegraded":
      if (total === 0) return null;
      return line(t(key, { value: metrics.componentsDegraded, total, share: metrics.componentsDegraded / total }));

    case "entryPoints":
      if (metrics.entryPointsTotal === 0) return null;
      return line(
        t(key, {
          value: metrics.entryPointsAffected,
          total: metrics.entryPointsTotal,
          share: metrics.entryPointsAffected / metrics.entryPointsTotal,
        }),
      );

    case "latency":
      return line(t(key));

    case "amplification":
      return line(t(key, { value: metrics.requestAmplification }));

    case "loadAmplification":
      return line(t(key, { value: metrics.loadAmplification }));

    case "backlog":
      return line(t(key, { value: metrics.backlogRps }));

    case "bottleneck":
      if (!metrics.bottleneck) return null;
      return line(t(key, { name: nameOf(metrics.bottleneck) }));
  }
}
