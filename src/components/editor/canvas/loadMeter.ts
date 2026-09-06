import type { ComponentLoad, NodeType } from "../../../types/architecture";

export const SATURATION = 0.8;

export const NOT_YOURS_TO_SIZE = new Set<NodeType>([
  "Client",
  "Cdn",
  "ExternalService",
  "IdentityProvider",
  "Scheduler",
  "Observability",
]);

export type LoadTone = "quiet" | "warm" | "hot";

export interface LoadMeter {
  fill: number;
  tone: LoadTone;
}

export function loadMeter(type: NodeType, load: ComponentLoad | null | undefined): LoadMeter | null {
  if (!load) return null;
  if (NOT_YOURS_TO_SIZE.has(type)) return null;
  if (load.rps <= 0 || load.capacityRps <= 0) return null;

  return {
    fill: Math.min(Math.max(load.utilization, 0), 1),
    tone: load.utilization > 1 ? "hot" : load.utilization >= SATURATION ? "warm" : "quiet",
  };
}
