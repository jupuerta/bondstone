import type { ArchitectureDto, ConnectionDto, NodeDto } from "../../types/architecture";
import type { Selection } from "./canvas/types";

export type SelectedElement = { kind: "node"; node: NodeDto } | { kind: "edge"; connection: ConnectionDto };

export function elementOf(architecture: Pick<ArchitectureDto, "nodes" | "connections">, selection: Selection): SelectedElement | null {
  if (selection.kind === "node") {
    const node = architecture.nodes.find((candidate) => candidate.id === selection.id);
    return node ? { kind: "node", node } : null;
  }
  const connection = architecture.connections.find((candidate) => candidate.id === selection.id);
  return connection ? { kind: "edge", connection } : null;
}

export function endsOf(architecture: Pick<ArchitectureDto, "nodes">, connection: ConnectionDto): { source: NodeDto | undefined; target: NodeDto | undefined } {
  return {
    source: architecture.nodes.find((node) => node.id === connection.sourceNodeId),
    target: architecture.nodes.find((node) => node.id === connection.targetNodeId),
  };
}

export function titleOf(selected: SelectedElement, architecture: Pick<ArchitectureDto, "nodes">, t: (key: string, args?: Record<string, string | number>) => string): string {
  if (selected.kind === "node") return selected.node.name;
  const { source, target } = endsOf(architecture, selected.connection);
  return t("editor.inspector.callBetween", { source: source?.name ?? "?", target: target?.name ?? "?" });
}
