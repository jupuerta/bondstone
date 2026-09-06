import type { Edge, Node } from "@xyflow/react";
import type { ArchNodeData, ConnectionData } from "./types";

export interface Graph {
  nodes: Node<ArchNodeData>[];
  edges: Edge<ConnectionData>[];
}

export interface GraphHistory {
  readonly past: readonly Graph[];
  readonly future: readonly Graph[];
}

export const EMPTY_HISTORY: GraphHistory = { past: [], future: [] };

export const HISTORY_LIMIT = 50;

export function fingerprint({ nodes, edges }: Graph): string {
  return JSON.stringify({
    n: nodes.map((node) => [
      node.id,
      node.type,
      node.data?.name,
      node.data?.configuration ?? {},
      Math.round(node.position.x),
      Math.round(node.position.y),
    ]),
    e: edges.map((edge) => [
      edge.id,
      edge.source,
      edge.target,
      edge.data?.synchronous,
      edge.data?.configuration ?? {},
    ]),
  });
}

export function sameGraph(a: Graph, b: Graph): boolean {
  return fingerprint(a) === fingerprint(b);
}

export function record(history: GraphHistory, left: Graph, limit = HISTORY_LIMIT): GraphHistory {
  return { past: [...history.past, left].slice(-limit), future: [] };
}

export function canUndo(history: GraphHistory): boolean {
  return history.past.length > 0;
}

export function canRedo(history: GraphHistory): boolean {
  return history.future.length > 0;
}

export function undo(history: GraphHistory, current: Graph): { history: GraphHistory; graph: Graph } | null {
  if (history.past.length === 0) return null;

  const graph = history.past[history.past.length - 1];

  return {
    graph,
    history: { past: history.past.slice(0, -1), future: [current, ...history.future] },
  };
}

export function redo(history: GraphHistory, current: Graph): { history: GraphHistory; graph: Graph } | null {
  if (history.future.length === 0) return null;

  const [graph, ...rest] = history.future;

  return { graph, history: { past: [...history.past, current], future: rest } };
}
