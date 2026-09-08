import type { Edge, Node } from "@xyflow/react";
import type { ArchitectureDto, ConnectionLoad, ElementState, ElementType } from "../../../types/architecture";
import { callFlows } from "./callFlow";
import { dtoConnectionsToFlow, dtoNodesToFlow } from "./mapping";
import { NODE_H, NODE_H_SIMULATE, NODE_W } from "./marks";
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

export interface StatusWord {
  key: string;
  upper: boolean;
}

export interface StatusTone {
  color: string | null;
  filled: boolean;
  state: StatusWord | null;
  detail: string[];
}

export function statusTone(status: NodeStatus | null): StatusTone {
  if (!status) return { color: null, filled: false, state: null, detail: [] };

  const mechanism = status.mechanism === null ? [] : [`ui.mechanism.${status.mechanism}`];

  if (status.health === "Down") {
    const cause = status.injected ? ["web.engine.injected"] : status.dragged ? ["web.engine.dragged"] : [];
    return { color: "var(--af-hot-panel)", filled: true, state: { key: "web.engine.state.Down", upper: true }, detail: [...cause, ...mechanism] };
  }

  if (status.health === "Degraded") {
    return { color: "var(--af-warm-panel)", filled: false, state: { key: "web.engine.state.Degraded", upper: false }, detail: mechanism };
  }

  if (status.health === null) {
    return { color: null, filled: false, state: { key: "web.engine.state.none", upper: false }, detail: [] };
  }

  return status.mechanism === null
    ? { color: null, filled: false, state: null, detail: [] }
    : { color: "var(--af-warm-panel)", filled: false, state: { key: `ui.mechanism.${status.mechanism}`, upper: false }, detail: [] };
}

export function nodeHeight(simulated: boolean): number {
  return simulated ? NODE_H_SIMULATE : NODE_H;
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
    height: nodeHeight(simulated),
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

export type EdgeLabel = (source: string, target: string) => string;

export function displayEdges(
  architecture: ArchitectureDto,
  nodeStates: ReadonlyMap<string, ElementState>,
  connectionStates: ReadonlyMap<string, ElementState>,
  callLoad: readonly ConnectionLoad[] | null | undefined,
  selection: Selection | null,
  label: EdgeLabel,
  marks: CanvasMarks = NO_MARKS,
): Edge<ConnectionData>[] {
  const flows = callFlows(callLoad);
  const names = new Map(architecture.nodes.map((node) => [node.id, node.name]));
  return dtoConnectionsToFlow(architecture.connections).map((edge) => {
    const own = connectionStates.get(edge.id)?.state ?? null;
    const targetDown = nodeStates.get(edge.target)?.state === "Down";
    return {
      ...edge,
      ariaLabel: label(names.get(edge.source) ?? "?", names.get(edge.target) ?? "?"),
      selected: selection?.kind === "edge" && selection.id === edge.id,
      className: marks.dimmed.has(edge.id) ? DIM : undefined,
      data: { ...edge.data!, flow: flows.get(edge.id) ?? null, state: own ?? (targetDown ? "Down" : null), highlighted: marks.highlighted.has(edge.id) },
    };
  });
}
