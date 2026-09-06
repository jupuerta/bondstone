import type { ElementState, NodeConfiguration, NodeType, ResilienceConfiguration } from "../../../types/architecture";
import type { CallFlow } from "./callFlow";

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
  [key: string]: unknown;
}

export interface ConnectionData {
  synchronous: boolean;
  configuration: ResilienceConfiguration;
  flow?: CallFlow | null;
  state?: ElementState["state"] | null;
  [key: string]: unknown;
}

export const DEFAULT_CONNECTION_DATA: ConnectionData = {
  synchronous: true,
  configuration: {},
};

export type Selection = { kind: "node"; id: string } | { kind: "edge"; id: string };

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
