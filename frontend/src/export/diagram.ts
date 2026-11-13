import type { ArchitectureDto, HealthState, NodeType, PositionDto, SimulationResult } from "../types/architecture";
import { orderConnections, orderNodes } from "./ordering";

export interface DiagramNode {
  id: string;
  type: NodeType;
  name: string;
  position: PositionDto;
}

export interface DiagramCall {
  id: string;
  from: PositionDto;
  to: PositionDto;
  synchronous: boolean;
}

export function diagramSource(architecture: ArchitectureDto): { nodes: DiagramNode[]; calls: DiagramCall[] } {
  const nodes = orderNodes(architecture.nodes);
  const namesById = new Map(nodes.map((node) => [node.id, node.name]));
  const places = new Map(nodes.map((node) => [node.id, node.position]));
  const nameOf = (id: string) => namesById.get(id) ?? id;

  const calls: DiagramCall[] = [];

  for (const connection of orderConnections(architecture.connections, nameOf)) {
    const from = places.get(connection.sourceNodeId);
    const to = places.get(connection.targetNodeId);
    if (from === undefined || to === undefined) continue;

    calls.push({ id: connection.id, from, to, synchronous: connection.synchronous });
  }

  return {
    nodes: nodes.map((node) => ({ id: node.id, type: node.type, name: node.name, position: node.position })),
    calls,
  };
}

export function healthOf(simulation?: SimulationResult | null): Map<string, HealthState> {
  if (!simulation) return new Map();

  return new Map(simulation.states.filter((state) => state.type === "Node").map((state) => [state.id, state.state]));
}

export function fitLines(text: string, width: number, fontSize: number, maxLines = 2): string[] {
  const room = Math.floor(width / (fontSize * 0.56));
  if (room <= 0) return [];

  const words = text.split(/\s+/).filter((word) => word.length > 0);
  if (words.length === 0) return [];

  const lines: string[] = [];

  for (const word of words) {
    const last = lines[lines.length - 1];
    if (last === undefined || `${last} ${word}`.length > room) lines.push(word);
    else lines[lines.length - 1] = `${last} ${word}`;
  }

  const fitted =
    lines.length <= maxLines ? lines : [...lines.slice(0, maxLines - 1), lines.slice(maxLines - 1).join(" ")];

  return fitted.map((line) => fitLabel(line, width, fontSize)).filter((line) => line.length > 0);
}

export function fitLabel(text: string, width: number, fontSize: number): string {
  const room = Math.floor(width / (fontSize * 0.56));
  if (room <= 0) return "";
  if (text.length <= room) return text;
  if (room === 1) return "…";

  return `${text.slice(0, room - 1).trimEnd()}…`;
}
