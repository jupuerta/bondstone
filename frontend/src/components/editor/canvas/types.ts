import type { ComponentLoad, ElementState, NodeConfiguration, NodeType, ResilienceConfiguration } from "../../../types/architecture";
import type { CallFlow } from "./callFlow";
import type { Problem } from "./lens";

export interface NodeStatus {
  health: ElementState["state"] | null;
  injected: boolean;
  dragged: boolean;
  mechanism: ElementState["mechanism"];
}

export interface ArchNodeData {
  name: string;
  configuration: NodeConfiguration;
  status?: NodeStatus | null;
  highlighted?: boolean;
  badge?: Problem | null;
  load?: ComponentLoad | null;
  loadScale?: number;
  [key: string]: unknown;
}

export interface ConnectionData {
  synchronous: boolean;
  configuration: ResilienceConfiguration;
  flow?: CallFlow | null;
  state?: ElementState["state"] | null;
  highlighted?: boolean;
  [key: string]: unknown;
}

export const DEFAULT_CONNECTION_DATA: ConnectionData = {
  synchronous: true,
  configuration: {},
};

export type Selection = { kind: "node"; id: string } | { kind: "edge"; id: string };

export type EditorMode = "design" | "simulate";

export interface CanvasMarks {
  highlighted: ReadonlySet<string>;
  dimmed: ReadonlySet<string>;
  badges: ReadonlyMap<string, Problem>;
  loads: ReadonlyMap<string, ComponentLoad>;
  loadScale: number;
}

export const NO_MARKS: CanvasMarks = { highlighted: new Set(), dimmed: new Set(), badges: new Map(), loads: new Map(), loadScale: 1 };

export const NODE_TYPES: NodeType[] = [
  "Client",
  "ExternalService",
  "IdentityProvider",
  "Gateway",
  "LoadBalancer",
  "Cdn",
  "Api",
  "Service",
  "Worker",
  "Database",
  "Cache",
  "ObjectStorage",
  "Queue",
  "Topic",
  "Scheduler",
  "Observability",
];
