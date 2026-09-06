import type { ConnectionDto, NodeConfiguration, NodeDto, NodeType, PositionDto, ResilienceConfiguration } from "../../../types/architecture";
import { protocolOf } from "../../../domain/protocol";


export interface StarterArchitecture {
  slug: string;
  label: string;
  lesson: string;
  score: number;
  criticals: number;
  nodes: readonly StarterNode[];
  connections: readonly StarterConnection[];
}

interface StarterNode {
  type: NodeType;
  name: string;
  position: PositionDto;
  configuration?: NodeConfiguration;
}

interface StarterConnection {
  from: string;
  to: string;
  synchronous: boolean;
  configuration?: ResilienceConfiguration;
}

const at = (x: number, y: number): PositionDto => ({ x, y });

export const STARTER_ARCHITECTURES: readonly StarterArchitecture[] = [
  {
    slug: "minimal-api-db",
    label: "ui.starter.minimalApiDb",
    lesson: "ui.starter.minimalApiDb.lesson",
    score: 99,
    criticals: 0,
    nodes: [
      { type: "Api", name: "Api", position: at(0, 0) },
      { type: "Database", name: "Db", position: at(0, 160) },
    ],
    connections: [{ from: "Api", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5 } }],
  },
  {
    slug: "resilient-async",
    label: "ui.starter.resilientAsync",
    lesson: "ui.starter.resilientAsync.lesson",
    score: 96,
    criticals: 1,
    nodes: [
      { type: "LoadBalancer", name: "LB", position: at(260, 0) },
      { type: "Api", name: "Api1", position: at(40, 150) },
      { type: "Api", name: "Api2", position: at(480, 150) },
      { type: "Cache", name: "Cache", position: at(40, 310) },
      { type: "Queue", name: "Queue", position: at(480, 310) },
      { type: "Service", name: "Orders", position: at(480, 460) },
      { type: "Database", name: "Db", position: at(260, 620) },
    ],
    connections: [
      { from: "LB", to: "Api1", synchronous: true, configuration: { timeoutSeconds: 3, circuitBreaker: true } },
      { from: "LB", to: "Api2", synchronous: true, configuration: { timeoutSeconds: 3, circuitBreaker: true } },
      { from: "Api1", to: "Cache", synchronous: true, configuration: { timeoutSeconds: 1, fallback: true } },
      { from: "Api2", to: "Cache", synchronous: true, configuration: { timeoutSeconds: 1, fallback: true } },
      { from: "Cache", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5, circuitBreaker: true, fallback: true } },
      { from: "Api1", to: "Queue", synchronous: false },
      { from: "Api2", to: "Queue", synchronous: false },
      { from: "Queue", to: "Orders", synchronous: false },
      { from: "Orders", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5, circuitBreaker: true, fallback: true } },
    ],
  },
  {
    slug: "gateway-microservices",
    label: "ui.starter.gatewayMicroservices",
    lesson: "ui.starter.gatewayMicroservices.lesson",
    score: 87,
    criticals: 6,
    nodes: [
      { type: "Gateway", name: "Gateway", position: at(280, 0) },
      { type: "Service", name: "Users", position: at(0, 170) },
      { type: "Service", name: "Orders", position: at(280, 170) },
      { type: "Service", name: "Payments", position: at(560, 170) },
      { type: "Database", name: "Db", position: at(280, 350) },
    ],
    connections: [
      { from: "Gateway", to: "Users", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Gateway", to: "Orders", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Gateway", to: "Payments", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Orders", to: "Payments", synchronous: true, configuration: { retries: 3 } },
      { from: "Users", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Orders", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Payments", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5 } },
    ],
  },
  {
    slug: "worst-case",
    label: "ui.starter.worstCase",
    lesson: "ui.starter.worstCase.lesson",
    score: 76,
    criticals: 12,
    nodes: [
      { type: "Gateway", name: "Gateway", position: at(600, 0) },
      { type: "Service", name: "Orders", position: at(600, 150) },
      { type: "Service", name: "Payments", position: at(0, 320) },
      { type: "Service", name: "Shipping", position: at(240, 320) },
      { type: "Service", name: "Notifications", position: at(480, 320) },
      { type: "Service", name: "Users", position: at(720, 320) },
      { type: "Service", name: "Billing", position: at(960, 320) },
      { type: "Service", name: "Search", position: at(1200, 320) },
      { type: "Service", name: "Inventory", position: at(0, 470) },
      { type: "Database", name: "Db", position: at(600, 620) },
    ],
    connections: [
      { from: "Gateway", to: "Orders", synchronous: true, configuration: { timeoutSeconds: 5, retries: 2 } },
      { from: "Orders", to: "Payments", synchronous: true },
      { from: "Orders", to: "Shipping", synchronous: true, configuration: { timeoutSeconds: 5, retries: 3 } },
      { from: "Orders", to: "Notifications", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Orders", to: "Users", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Orders", to: "Billing", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Orders", to: "Search", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Orders", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Payments", to: "Inventory", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Inventory", to: "Orders", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Shipping", to: "Notifications", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Notifications", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Users", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5 } },
      { from: "Billing", to: "Db", synchronous: true, configuration: { timeoutSeconds: 5 } },
    ],
  },
];

export function starterToDto(starter: StarterArchitecture): { nodes: NodeDto[]; connections: ConnectionDto[] } {
  const ids = new Map(starter.nodes.map((node) => [node.name, crypto.randomUUID()]));

  const typeOf = (name: string) => starter.nodes.find((node) => node.name === name)?.type;

  return {
    nodes: starter.nodes.map((node) => ({
      id: ids.get(node.name)!,
      type: node.type,
      name: node.name,
      position: { ...node.position },
      configuration: node.configuration ?? {},
    })),
    connections: starter.connections.map((connection) => ({
      id: crypto.randomUUID(),
      sourceNodeId: ids.get(connection.from)!,
      targetNodeId: ids.get(connection.to)!,
      protocol: protocolOf(typeOf(connection.from), typeOf(connection.to)),
      synchronous: connection.synchronous,
      configuration: connection.configuration ?? {},
    })),
  };
}
