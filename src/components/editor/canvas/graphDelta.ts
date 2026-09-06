import type { ArchitectureDto } from "../../../types/architecture";

export type ComparableGraph = Pick<ArchitectureDto, "nodes" | "connections">;

export interface GraphDelta {
  addedNodes: ReadonlySet<string>;
  removedNodes: ReadonlySet<string>;
  changedNodes: ReadonlySet<string>;
  addedCalls: ReadonlySet<string>;
  removedCalls: ReadonlySet<string>;
  changedCalls: ReadonlySet<string>;
}

export function graphDelta(before: ComparableGraph, after: ComparableGraph): GraphDelta {
  const nodesBefore = new Map(before.nodes.map((node) => [node.id, nodePrint(node)]));
  const nodesAfter = new Map(after.nodes.map((node) => [node.id, nodePrint(node)]));

  const callsBefore = new Map(before.connections.map((call) => [call.id, callPrint(call)]));
  const callsAfter = new Map(after.connections.map((call) => [call.id, callPrint(call)]));

  return {
    addedNodes: onlyIn(nodesAfter, nodesBefore),
    removedNodes: onlyIn(nodesBefore, nodesAfter),
    changedNodes: differing(nodesBefore, nodesAfter),
    addedCalls: onlyIn(callsAfter, callsBefore),
    removedCalls: onlyIn(callsBefore, callsAfter),
    changedCalls: differing(callsBefore, callsAfter),
  };
}

export function drawn(delta: GraphDelta): ReadonlySet<string> {
  return new Set([...delta.addedNodes, ...delta.changedNodes, ...delta.addedCalls, ...delta.changedCalls]);
}

export function moved(delta: GraphDelta): number {
  return (
    delta.addedNodes.size +
    delta.removedNodes.size +
    delta.changedNodes.size +
    delta.addedCalls.size +
    delta.removedCalls.size +
    delta.changedCalls.size
  );
}

type Node = ComparableGraph["nodes"][number];
type Call = ComparableGraph["connections"][number];

function nodePrint(node: Node): string {
  return JSON.stringify([node.type, node.name, settings(node.configuration)]);
}

function callPrint(call: Call): string {
  return JSON.stringify([
    call.sourceNodeId,
    call.targetNodeId,
    call.synchronous,
    settings(call.configuration),
  ]);
}

function settings(configuration: object | null | undefined): Array<[string, unknown]> {
  const entries = Object.entries(configuration ?? {}).filter(([, value]) => value !== undefined && value !== null);
  return entries.sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
}

function onlyIn(these: Map<string, string>, those: Map<string, string>): ReadonlySet<string> {
  return new Set([...these.keys()].filter((id) => !those.has(id)));
}

function differing(before: Map<string, string>, after: Map<string, string>): ReadonlySet<string> {
  return new Set([...after].filter(([id, print]) => before.has(id) && before.get(id) !== print).map(([id]) => id));
}
