import type { Edge, Node } from "@xyflow/react";
import type { ArchitectureDto, ConnectionLoad, ElementState, ElementType } from "../../../types/architecture";
import { callFlows } from "./callFlow";
import { dtoConnectionsToFlow, dtoNodesToFlow } from "./mapping";
import { NODE_H, NODE_W } from "./marks";
import { NO_MARKS, type ArchNodeData, type CanvasMarks, type ConnectionData, type NodeStatus, type Selection } from "./types";

const DIM = "af-dim";

export function statesOf(states: readonly ElementState[] | null, type: ElementType): ReadonlyMap<string, ElementState> {
  const map = new Map<string, ElementState>();
  for (const state of states ?? []) if (state.type === type) map.set(state.id, state);
  return map;
}

export function nodeStatusOf(
  nodeId: string,
  nodeStates: ReadonlyMap<string, ElementState>,
  faultIds: ReadonlySet<string>,
  simulated: boolean,
): NodeStatus | null {
  if (!simulated) return null;
  const state = nodeStates.get(nodeId) ?? null;
  const injected = faultIds.has(nodeId);
  return {
    health: state?.state ?? null,
    injected,
    dragged: state?.causedBy != null && !injected,
    mechanism: state?.mechanism ?? null,
  };
}

export function displayNodes(
  architecture: ArchitectureDto,
  nodeStates: ReadonlyMap<string, ElementState>,
  faultIds: ReadonlySet<string>,
  simulated: boolean,
  selection: Selection | null,
  marks: CanvasMarks = NO_MARKS,
): Node<ArchNodeData>[] {
  return dtoNodesToFlow(architecture.nodes).map((node) => ({
    ...node,
    width: NODE_W,
    height: NODE_H,
    selected: selection?.kind === "node" && selection.id === node.id,
    className: marks.dimmed.has(node.id) ? DIM : undefined,
    data: {
      ...node.data,
      status: nodeStatusOf(node.id, nodeStates, faultIds, simulated),
      highlighted: marks.highlighted.has(node.id),
      badge: marks.badges.get(node.id) ?? null,
      load: simulated ? null : (marks.loads.get(node.id) ?? null),
      loadScale: marks.loadScale,
    },
  }));
}

export function displayEdges(
  architecture: ArchitectureDto,
  nodeStates: ReadonlyMap<string, ElementState>,
  connectionStates: ReadonlyMap<string, ElementState>,
  callLoad: readonly ConnectionLoad[] | null | undefined,
  selection: Selection | null,
  marks: CanvasMarks = NO_MARKS,
): Edge<ConnectionData>[] {
  const flows = callFlows(callLoad);
  return dtoConnectionsToFlow(architecture.connections).map((edge) => {
    const own = connectionStates.get(edge.id)?.state ?? null;
    const targetDown = nodeStates.get(edge.target)?.state === "Down";
    return {
      ...edge,
      selected: selection?.kind === "edge" && selection.id === edge.id,
      className: marks.dimmed.has(edge.id) ? DIM : undefined,
      data: { ...edge.data!, flow: flows.get(edge.id) ?? null, state: own ?? (targetDown ? "Down" : null), highlighted: marks.highlighted.has(edge.id) },
    };
  });
}
