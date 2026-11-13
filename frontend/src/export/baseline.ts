import type { ArchitectureDto, ConnectionDto, NodeDto } from "../types/architecture";
import { orderConnections, orderNodes, ordinal } from "./ordering";

export interface ArchitectureDiff {
  addedNodes: NodeDto[];
  removedNodes: NodeDto[];
  changedNodes: NodeChange[];
  addedConnections: ConnectionDto[];
  removedConnections: ConnectionDto[];
  changedConnections: ConnectionChange[];
}

export interface NodeChange {
  before: NodeDto;
  after: NodeDto;
  renamed: boolean;
  reconfigured: boolean;
}

export interface ConnectionChange {
  before: ConnectionDto;
  after: ConnectionDto;
  synchronous: boolean;
  resilience: boolean;
}

export function diffArchitectures(before: ArchitectureDto, after: ArchitectureDto): ArchitectureDiff {
  const nodesBefore = new Map(before.nodes.map((node) => [node.id, node]));
  const nodesAfter = new Map(after.nodes.map((node) => [node.id, node]));

  const changedNodes: NodeChange[] = [];

  for (const node of orderNodes(after.nodes)) {
    const previous = nodesBefore.get(node.id);
    if (previous === undefined) continue;

    const renamed = previous.name !== node.name;
    const reconfigured = !sameValue(previous.configuration, node.configuration);

    if (renamed || reconfigured) changedNodes.push({ before: previous, after: node, renamed, reconfigured });
  }

  const nameOf = nameResolver(before, after);

  const connectionsBefore = new Map(before.connections.map((connection) => [connection.id, connection]));
  const connectionsAfter = new Map(after.connections.map((connection) => [connection.id, connection]));

  const changedConnections: ConnectionChange[] = [];

  for (const connection of orderConnections(after.connections, nameOf)) {
    const previous = connectionsBefore.get(connection.id);
    if (previous === undefined) continue;

    const synchronous = previous.synchronous !== connection.synchronous;
    const resilience = !sameValue(previous.configuration, connection.configuration);

    if (synchronous || resilience) {
      changedConnections.push({ before: previous, after: connection, synchronous, resilience });
    }
  }

  return {
    addedNodes: orderNodes(after.nodes.filter((node) => !nodesBefore.has(node.id))),
    removedNodes: orderNodes(before.nodes.filter((node) => !nodesAfter.has(node.id))),
    changedNodes,
    addedConnections: orderConnections(
      after.connections.filter((connection) => !connectionsBefore.has(connection.id)),
      nameOf,
    ),
    removedConnections: orderConnections(
      before.connections.filter((connection) => !connectionsAfter.has(connection.id)),
      nameOf,
    ),
    changedConnections,
  };
}

export function nameResolver(
  before: ArchitectureDto,
  after: ArchitectureDto,
): (id: string) => string {
  const names = new Map<string, string>();
  for (const node of before.nodes) names.set(node.id, node.name);
  for (const node of after.nodes) names.set(node.id, node.name);

  return (id) => names.get(id) ?? "";
}

export function hasChanges(diff: ArchitectureDiff): boolean {
  return (
    diff.addedNodes.length > 0 ||
    diff.removedNodes.length > 0 ||
    diff.changedNodes.length > 0 ||
    diff.addedConnections.length > 0 ||
    diff.removedConnections.length > 0 ||
    diff.changedConnections.length > 0
  );
}

function sameValue(a: unknown, b: unknown): boolean {
  return canonical(a) === canonical(b);
}

function canonical(value: unknown): string {
  if (value === null || value === undefined) return "null";
  if (Array.isArray(value)) return `[${value.map(canonical).join(",")}]`;

  if (typeof value === "object") {
    const entries = Object.keys(value as Record<string, unknown>)
      .filter((key) => (value as Record<string, unknown>)[key] != null)
      .sort(ordinal)
      .map((key) => `${JSON.stringify(key)}:${canonical((value as Record<string, unknown>)[key])}`);

    return `{${entries.join(",")}}`;
  }

  return JSON.stringify(value) ?? "null";
}
