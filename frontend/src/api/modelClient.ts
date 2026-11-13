import type { NodeType, ProtocolType } from "../types/architecture";
import { API_URL, parseErrorResponse } from "./architectureClient";

export interface ComponentModel {
  type: NodeType;
  capacityPerInstance: number;
  processingMs: number;
  costPerInstance: number;
}

export interface NetworkHop {
  protocol: ProtocolType;
  ms: number;
}

export interface QueueingPoint {
  utilization: number;
  factor: number;
}

export interface QueueingCurve {
  cap: number;
  points: QueueingPoint[];
}

export interface ModelReference {
  components: ComponentModel[];
  network: NetworkHop[];
  saturationWarning: number;
  unboundedWaitMs: number;
  defaultHitRatio: number;
  defaultCdnHitRatio: number;
  queueing: QueueingCurve;
}

export async function getModel(): Promise<ModelReference> {
  const response = await fetch(`${API_URL}/model`);
  if (!response.ok) return parseErrorResponse(response);
  return response.json();
}
