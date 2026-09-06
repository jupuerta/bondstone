import type { Edge, Node } from "@xyflow/react";
import type { ArchitectureDto, ConnectionDto, NodeDto, NodeType, UpdateArchitectureRequest } from "../../../types/architecture";
import { DEFAULT_CONNECTION_DATA, type ArchNodeData, type ConnectionData } from "./types";

export function dtoNodesToFlow(nodes: readonly NodeDto[]): Node<ArchNodeData>[] {
  return nodes.map((node) => ({
    id: node.id,
    type: node.type,
    position: { x: node.position.x, y: node.position.y },
    data: { name: node.name, configuration: node.configuration ?? {} },
  }));
}

export function dtoConnectionsToFlow(connections: readonly ConnectionDto[]): Edge<ConnectionData>[] {
  return connections.map((connection) => ({
    id: connection.id,
    source: connection.sourceNodeId,
    target: connection.targetNodeId,
    data: {
      protocol: connection.protocol,
      synchronous: connection.synchronous,
      configuration: connection.configuration ?? {},
    },
  }));
}

export function flowToUpdateRequest(
  name: string,
  version: number,
  nodes: readonly Node<ArchNodeData>[],
  edges: readonly Edge<ConnectionData>[],
): UpdateArchitectureRequest {
  return {
    name,
    version,
    nodes: nodes.map((node) => ({
      id: node.id,
      type: node.type as NodeType,
      name: node.data.name,
      position: { x: node.position.x, y: node.position.y },
      configuration: node.data.configuration,
    })),
    connections: edges.map((edge) => {
      const data = edge.data ?? DEFAULT_CONNECTION_DATA;
      return {
        id: edge.id,
        sourceNodeId: edge.source,
        targetNodeId: edge.target,
        protocol: data.protocol,
        synchronous: data.synchronous,
        configuration: data.configuration,
      };
    }),
  };
}

export function flowToArchitecture(
  base: ArchitectureDto,
  name: string,
  version: number,
  nodes: readonly Node<ArchNodeData>[],
  edges: readonly Edge<ConnectionData>[],
): ArchitectureDto {
  const request = flowToUpdateRequest(name, version, nodes, edges);
  return { ...base, name, version, nodes: request.nodes, connections: request.connections };
}
