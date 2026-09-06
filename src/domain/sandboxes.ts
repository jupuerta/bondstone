import type { ConnectionDto, NodeConfiguration, NodeDto, NodeType, PositionDto, ResilienceConfiguration } from "../types/architecture";
import type { MechanismId } from "./mechanisms";
import { protocolOf } from "./protocol";


export interface SandboxNode {
  type: NodeType;
  name: string;
  position: PositionDto;
  configuration?: NodeConfiguration;
}

export interface SandboxCall {
  from: string;
  to: string;
  synchronous: boolean;
  configuration?: ResilienceConfiguration;
}

export interface Sandbox {
  nodes: readonly SandboxNode[];
  calls: readonly SandboxCall[];
}

const at = (x: number, y: number): PositionDto => ({ x, y });

const BOUND: ResilienceConfiguration = { timeoutSeconds: 3 };

export const SANDBOXES: Record<MechanismId, Sandbox> = {
  timeout: {
    nodes: [
      { type: "Client", name: "Web", position: at(240, 0), configuration: { requestsPerSecond: 100 } },
      { type: "Api", name: "Api", position: at(240, 170) },
      { type: "ExternalService", name: "Pagos", position: at(240, 340) },
    ],
    calls: [
      { from: "Web", to: "Api", synchronous: true, configuration: BOUND },
      { from: "Api", to: "Pagos", synchronous: true },
    ],
  },

  retries: {
    nodes: [
      { type: "Client", name: "Web", position: at(240, 0), configuration: { requestsPerSecond: 300 } },
      { type: "Api", name: "Api", position: at(240, 170) },
      { type: "Service", name: "Pedidos", position: at(240, 340) },
    ],
    calls: [
      { from: "Web", to: "Api", synchronous: true, configuration: BOUND },
      { from: "Api", to: "Pedidos", synchronous: true, configuration: BOUND },
    ],
  },

  circuitBreaker: {
    nodes: [
      { type: "Client", name: "Web", position: at(240, 0), configuration: { requestsPerSecond: 100 } },
      { type: "Service", name: "Pedidos", position: at(240, 170) },
      { type: "ExternalService", name: "Pagos", position: at(240, 340) },
    ],
    calls: [
      { from: "Web", to: "Pedidos", synchronous: true, configuration: BOUND },
      { from: "Pedidos", to: "Pagos", synchronous: true, configuration: { timeoutSeconds: 3, retries: 2 } },
    ],
  },

  fallback: {
    nodes: [
      { type: "Client", name: "Web", position: at(240, 0), configuration: { requestsPerSecond: 100 } },
      { type: "Service", name: "Pedidos", position: at(240, 170) },
      { type: "ExternalService", name: "Pagos", position: at(240, 340) },
    ],
    calls: [
      { from: "Web", to: "Pedidos", synchronous: true, configuration: BOUND },
      { from: "Pedidos", to: "Pagos", synchronous: true, configuration: BOUND },
    ],
  },

  queue: {
    nodes: [
      { type: "Api", name: "Api", position: at(240, 0), configuration: { requestsPerSecond: 100 } },
      { type: "Service", name: "Pedidos", position: at(240, 170) },
      { type: "Worker", name: "Emails", position: at(240, 340) },
    ],
    calls: [
      { from: "Api", to: "Pedidos", synchronous: true, configuration: BOUND },
      { from: "Pedidos", to: "Emails", synchronous: true, configuration: BOUND },
    ],
  },

  deadLetterQueue: {
    nodes: [
      { type: "Api", name: "Api", position: at(240, 0), configuration: { requestsPerSecond: 100 } },
      { type: "Queue", name: "Pedidos", position: at(240, 170), configuration: { maxRetries: 3 } },
      { type: "Worker", name: "Facturas", position: at(240, 340) },
    ],
    calls: [
      { from: "Api", to: "Pedidos", synchronous: false },
      { from: "Pedidos", to: "Facturas", synchronous: false },
    ],
  },

  cache: {
    nodes: [
      { type: "Api", name: "Catalogo", position: at(0, 0), configuration: { requestsPerSecond: 200 } },
      { type: "Api", name: "Pedidos", position: at(480, 0), configuration: { requestsPerSecond: 150 } },
      { type: "Database", name: "Db", position: at(240, 220) },
    ],
    calls: [
      { from: "Catalogo", to: "Db", synchronous: true, configuration: { timeoutSeconds: 3, access: "read" } },
      { from: "Pedidos", to: "Db", synchronous: true, configuration: { timeoutSeconds: 3, access: "read" } },
    ],
  },

  readReplicas: {
    nodes: [
      { type: "Client", name: "Web", position: at(240, 0), configuration: { requestsPerSecond: 300 } },
      { type: "Api", name: "Api", position: at(240, 170) },
      { type: "Database", name: "Db", position: at(240, 340) },
    ],
    calls: [
      { from: "Web", to: "Api", synchronous: true, configuration: BOUND },
      { from: "Api", to: "Db", synchronous: true, configuration: { timeoutSeconds: 3, access: "read" } },
    ],
  },

  rateLimit: {
    nodes: [
      { type: "Client", name: "Web", position: at(240, 0), configuration: { requestsPerSecond: 900 } },
      { type: "Api", name: "Api", position: at(240, 170) },
      { type: "Database", name: "Db", position: at(240, 340) },
    ],
    calls: [
      { from: "Web", to: "Api", synchronous: true, configuration: BOUND },
      { from: "Api", to: "Db", synchronous: true, configuration: BOUND },
    ],
  },

  healthCheck: {
    nodes: [
      { type: "LoadBalancer", name: "Lb", position: at(240, 0), configuration: { requestsPerSecond: 200 } },
      { type: "Api", name: "Api1", position: at(0, 220) },
      { type: "Api", name: "Api2", position: at(480, 220) },
    ],
    calls: [
      { from: "Lb", to: "Api1", synchronous: true, configuration: BOUND },
      { from: "Lb", to: "Api2", synchronous: true, configuration: BOUND },
    ],
  },

  instances: {
    nodes: [
      { type: "Client", name: "Web", position: at(240, 0), configuration: { requestsPerSecond: 700 } },
      { type: "LoadBalancer", name: "Lb", position: at(240, 170) },
      { type: "Api", name: "Api", position: at(240, 340), configuration: { instances: 1 } },
    ],
    calls: [
      { from: "Web", to: "Lb", synchronous: true, configuration: BOUND },
      { from: "Lb", to: "Api", synchronous: true, configuration: BOUND },
    ],
  },
};

export function sandboxToDto(sandbox: Sandbox): { nodes: NodeDto[]; connections: ConnectionDto[] } {
  const ids = new Map(sandbox.nodes.map((node) => [node.name, crypto.randomUUID()]));

  const typeOf = (name: string) => sandbox.nodes.find((node) => node.name === name)?.type;

  return {
    nodes: sandbox.nodes.map((node) => ({
      id: ids.get(node.name)!,
      type: node.type,
      name: node.name,
      position: { ...node.position },
      configuration: node.configuration ?? {},
    })),
    connections: sandbox.calls.map((call) => ({
      id: crypto.randomUUID(),
      sourceNodeId: ids.get(call.from)!,
      targetNodeId: ids.get(call.to)!,
      protocol: protocolOf(typeOf(call.from), typeOf(call.to)),
      synchronous: call.synchronous,
      configuration: call.configuration ?? {},
    })),
  };
}
