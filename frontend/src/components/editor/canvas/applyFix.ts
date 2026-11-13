import type { Edge, Node } from "@xyflow/react";
import type { AppliedFix } from "../../../types/architecture";
import type { Graph } from "./graphHistory";
import { DEFAULT_CONNECTION_DATA, type ArchNodeData, type ConnectionData } from "./types";

export function applyFix(graph: Graph, fix: AppliedFix): Graph {
  switch (fix.kind) {
    case "setEdgeResilience":
      return setEdgeResilience(graph, fix);
    case "setNodeFlag":
      return setNodeFlag(graph, fix);
    case "insertCache":
      return insertCache(graph, fix);
    case "removeNode":
      return removeNode(graph, fix);
    case "connectTo":
      return connectTo(graph, fix);
    case "makeEdgeAsynchronous":
      return makeEdgeAsynchronous(graph, fix);
    case "insertQueue":
      return insertQueue(graph, fix);
    default:
      return graph;
  }
}

function setEdgeResilience(graph: Graph, fix: AppliedFix): Graph {
  return {
    ...graph,
    edges: graph.edges.map((edge) => {
      if (edge.id !== fix.element.id) return edge;

      const data = edge.data ?? DEFAULT_CONNECTION_DATA;
      return {
        ...edge,
        data: {
          ...data,
          configuration: {
            ...data.configuration,
            ...(fix.timeoutSeconds != null ? { timeoutSeconds: fix.timeoutSeconds } : {}),
            ...(fix.circuitBreaker != null ? { circuitBreaker: fix.circuitBreaker } : {}),
            ...(fix.fallback != null ? { fallback: fix.fallback } : {}),
          },
        },
      };
    }),
  };
}

function setNodeFlag(graph: Graph, fix: AppliedFix): Graph {
  if (!fix.configurationKey) return graph;

  return {
    ...graph,
    nodes: graph.nodes.map((node) =>
      node.id === fix.element.id
        ? {
            ...node,
            data: {
              ...node.data,
              configuration: { ...node.data.configuration, [fix.configurationKey!]: fix.configurationValue ?? true },
            },
          }
        : node,
    ),
  };
}

function insertCache(graph: Graph, fix: AppliedFix): Graph {
  const database = graph.nodes.find((node) => node.id === fix.element.id);
  if (!database) return graph;

  const callers = graph.edges.filter((edge) => edge.target === database.id && (edge.data?.synchronous ?? true) && !declaresWrite(edge));
  if (callers.length === 0) return graph;

  const cacheId = crypto.randomUUID();
  const cache: Node<ArchNodeData> = {
    id: cacheId,
    type: "Cache",
    position: { x: database.position.x - 180, y: database.position.y },
    data: { name: `${database.data.name} cache`, configuration: {} },
  };

  const rewired = graph.edges.map((edge) => {
    if (!callers.some((caller) => caller.id === edge.id)) return edge;

    const data = edge.data ?? DEFAULT_CONNECTION_DATA;

    return { ...edge, target: cacheId, data: { ...data, configuration: { ...data.configuration, fallback: true } } };
  });

  const cacheToDatabase: Edge<ConnectionData> = {
    id: crypto.randomUUID(),
    source: cacheId,
    target: database.id,
    data: { ...DEFAULT_CONNECTION_DATA, configuration: { timeoutSeconds: 5, access: "read" } },
  };

  return { nodes: [...graph.nodes, cache], edges: [...rewired, cacheToDatabase] };
}

function declaresWrite(edge: Edge<ConnectionData>): boolean {
  const access = edge.data?.configuration?.access;
  return access === "write" || access === "both";
}

function insertQueue(graph: Graph, fix: AppliedFix): Graph {
  const call = graph.edges.find((edge) => edge.id === fix.element.id);
  if (!call) return graph;

  const source = graph.nodes.find((node) => node.id === call.source);
  const target = graph.nodes.find((node) => node.id === call.target);
  if (!source || !target) return graph;

  const queueId = crypto.randomUUID();
  const queue: Node<ArchNodeData> = {
    id: queueId,
    type: "Queue",
    position: { x: (source.position.x + target.position.x) / 2, y: (source.position.y + target.position.y) / 2 },
    data: { name: `${target.data.name} queue`, configuration: { deadLetterQueue: true } },
  };

  const publish: Edge<ConnectionData> = { ...call, target: queueId, data: { ...DEFAULT_CONNECTION_DATA, synchronous: false, configuration: {} } };

  const deliver: Edge<ConnectionData> = {
    id: crypto.randomUUID(),
    source: queueId,
    target: call.target,
    data: { ...DEFAULT_CONNECTION_DATA, synchronous: false, configuration: {} },
  };

  return {
    nodes: [...graph.nodes, queue],
    edges: [...graph.edges.map((edge) => (edge.id === call.id ? publish : edge)), deliver],
  };
}

function removeNode(graph: Graph, fix: AppliedFix): Graph {
  return {
    nodes: graph.nodes.filter((node) => node.id !== fix.element.id),
    edges: graph.edges.filter((edge) => edge.source !== fix.element.id && edge.target !== fix.element.id),
  };
}

function connectTo(graph: Graph, fix: AppliedFix): Graph {
  const target = fix.targetNodeId;
  if (!target) return graph;

  const source = graph.nodes.find((node) => node.id === fix.element.id);
  if (!source || !graph.nodes.some((node) => node.id === target)) return graph;

  const alreadyWired = graph.edges.some(
    (edge) => (edge.source === source.id && edge.target === target) || (edge.source === target && edge.target === source.id),
  );
  if (alreadyWired) return graph;

  const wire: Edge<ConnectionData> = {
    id: crypto.randomUUID(),
    source: source.id,
    target,
    data: { ...DEFAULT_CONNECTION_DATA, synchronous: false, configuration: {} },
  };

  return { ...graph, edges: [...graph.edges, wire] };
}

function makeEdgeAsynchronous(graph: Graph, fix: AppliedFix): Graph {
  return {
    ...graph,
    edges: graph.edges.map((edge) =>
      edge.id === fix.element.id ? { ...edge, data: { ...(edge.data ?? DEFAULT_CONNECTION_DATA), synchronous: false } } : edge,
    ),
  };
}
