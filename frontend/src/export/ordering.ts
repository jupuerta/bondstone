import type { ConnectionDto, NodeDto, NodeType } from "../types/architecture";

const TYPE_ORDER: NodeType[] = [
  "Api",
  "Database",
  "Service",
  "Queue",
  "Cache",
  "Gateway",
  "LoadBalancer",
  "Observability",
  "Worker",
  "ObjectStorage",
  "ExternalService",
  "Client",
  "Cdn",
  "Scheduler",
  "IdentityProvider",
];

export function ordinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

export function orderNodes(nodes: readonly NodeDto[]): NodeDto[] {
  return [...nodes].sort(
    (a, b) =>
      TYPE_ORDER.indexOf(a.type) - TYPE_ORDER.indexOf(b.type) ||
      ordinal(a.name, b.name) ||
      ordinal(a.id, b.id),
  );
}

export function orderConnections(
  connections: readonly ConnectionDto[],
  nameOf: (id: string) => string,
): ConnectionDto[] {
  return [...connections].sort(
    (a, b) =>
      ordinal(nameOf(a.sourceNodeId), nameOf(b.sourceNodeId)) ||
      ordinal(nameOf(a.targetNodeId), nameOf(b.targetNodeId)) ||
      ordinal(a.id, b.id),
  );
}
