import type { Edge, Node } from "@xyflow/react";
import type { ConnectionDto, NodeConfiguration, NodeDto, NodeType, PositionDto } from "../../../types/architecture";
import { isEntryPoint, staleTrafficFields } from "./entryPoint";
import type { Graph } from "./graphHistory";
import { dtoConnectionsToFlow, dtoNodesToFlow } from "./mapping";
import { DEFAULT_CONNECTION_DATA, type ArchNodeData, type ConnectionData } from "./types";

export function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  return target.isContentEditable || target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement;
}

export function nextNodeName(type: NodeType, existing: readonly Node<ArchNodeData>[]): string {
  const count = existing.filter((node) => node.type === type).length + 1;
  return `${type} ${count}`;
}

export function addNode(graph: Graph, type: NodeType, position: PositionDto, id: string = crypto.randomUUID()): Graph {
  const node: Node<ArchNodeData> = { id, type, position: { x: position.x, y: position.y }, data: { name: nextNodeName(type, graph.nodes), configuration: {} } };
  return { nodes: [...graph.nodes, node], edges: graph.edges };
}

export function connect(graph: Graph, source: string, target: string, id: string = crypto.randomUUID()): Graph {
  if (source === target) return graph;
  if (!graph.nodes.some((node) => node.id === source) || !graph.nodes.some((node) => node.id === target)) return graph;
  if (graph.edges.some((edge) => edge.source === source && edge.target === target)) return graph;

  const edge: Edge<ConnectionData> = { id, source, target, data: { ...DEFAULT_CONNECTION_DATA } };
  return { nodes: forgetDeclaredTraffic(graph.nodes, source, target), edges: [...graph.edges, edge] };
}

export function forgetDeclaredTraffic(nodes: Node<ArchNodeData>[], source: string, target: string): Node<ArchNodeData>[] {
  const from = nodes.find((node) => node.id === source);
  const to = nodes.find((node) => node.id === target);
  if (!from || !to) return nodes;

  const stale = staleTrafficFields(from.type as NodeType, to.type as NodeType, to.data.configuration);
  if (stale.length === 0) return nodes;

  const kept = Object.fromEntries(Object.entries(to.data.configuration).filter(([key]) => !stale.includes(key)));
  return nodes.map((node) => (node.id === target ? { ...node, data: { ...node.data, configuration: kept } } : node));
}

export function removeNodes(graph: Graph, ids: ReadonlySet<string>): Graph {
  if (ids.size === 0) return graph;
  return {
    nodes: graph.nodes.filter((node) => !ids.has(node.id)),
    edges: graph.edges.filter((edge) => !ids.has(edge.source) && !ids.has(edge.target)),
  };
}

export function removeEdges(graph: Graph, ids: ReadonlySet<string>): Graph {
  if (ids.size === 0) return graph;
  return { nodes: graph.nodes, edges: graph.edges.filter((edge) => !ids.has(edge.id)) };
}

export function moveNodes(graph: Graph, positions: ReadonlyMap<string, PositionDto>): Graph {
  if (positions.size === 0) return graph;
  return {
    nodes: graph.nodes.map((node) => {
      const position = positions.get(node.id);
      return position ? { ...node, position: { x: position.x, y: position.y } } : node;
    }),
    edges: graph.edges,
  };
}

export function patchNode(graph: Graph, id: string, patch: Partial<Pick<ArchNodeData, "name" | "configuration">>): Graph {
  return { nodes: graph.nodes.map((node) => (node.id === id ? { ...node, data: { ...node.data, ...patch } } : node)), edges: graph.edges };
}

export function setNodeField(graph: Graph, id: string, key: string, value: unknown): Graph {
  const node = graph.nodes.find((candidate) => candidate.id === id);
  if (!node) return graph;
  const configuration: NodeConfiguration = { ...node.data.configuration };
  if (value === undefined || value === null || value === "") delete configuration[key];
  else configuration[key] = value;
  return patchNode(graph, id, { configuration });
}

export function patchEdge(graph: Graph, id: string, patch: Partial<Pick<ConnectionData, "synchronous" | "configuration">>): Graph {
  return {
    nodes: graph.nodes,
    edges: graph.edges.map((edge) => (edge.id === id ? { ...edge, data: { ...(edge.data ?? DEFAULT_CONNECTION_DATA), ...patch } } : edge)),
  };
}

export function importGraph(nodes: readonly NodeDto[], connections: readonly ConnectionDto[]): Graph {
  return { nodes: dtoNodesToFlow(nodes), edges: dtoConnectionsToFlow(connections) };
}

export function entryPointOf(graph: Graph, nodeId: string): boolean {
  const nodes = graph.nodes.map((node) => ({ id: node.id, type: node.type as NodeType, name: node.data.name, position: node.position }));
  const calls = graph.edges.map((edge) => ({ id: edge.id, sourceNodeId: edge.source, targetNodeId: edge.target, protocol: "Http" as const, synchronous: true }));
  return isEntryPoint(nodeId, nodes, calls);
}

export function connectedEdges(graph: Graph, nodeId: string): number {
  return graph.edges.filter((edge) => edge.source === nodeId || edge.target === nodeId).length;
}
