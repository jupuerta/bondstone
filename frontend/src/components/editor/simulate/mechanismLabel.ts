import type { Mechanism } from "../../../types/architecture";

export const MECHANISM_LABEL: Record<Mechanism, string> = {
  fallback: "ui.inspector.fallback",
  circuitBreaker: "ui.inspector.circuitBreaker",
  queue: "ui.node.Queue",
  healthCheck: "ui.config.healthCheck",
};
