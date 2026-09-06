import type { ArchitectureDto } from "../types/architecture";

const KEY = "archforge.challengeStarts";

const LIMIT = 15;

export type ComparableGraph = Pick<ArchitectureDto, "nodes" | "connections">;

interface Stored {
  slug: string;
  at: number;
  graph: ComparableGraph;
}

export function rememberStartingPoint(slug: string, graph: ComparableGraph): void {
  const kept = read().filter((entry) => entry.slug !== slug);
  write([{ slug, at: Date.now(), graph: trim(graph) }, ...kept]);
}

export function startingPointOf(slug: string | null): ComparableGraph | null {
  if (slug === null) return null;
  return read().find((entry) => entry.slug === slug)?.graph ?? null;
}

function trim(graph: ComparableGraph): ComparableGraph {
  return {
    nodes: graph.nodes.map((node) => ({
      id: node.id,
      type: node.type,
      name: node.name,
      position: { x: node.position.x, y: node.position.y },
      configuration: node.configuration ?? undefined,
    })),
    connections: graph.connections.map((call) => ({
      id: call.id,
      sourceNodeId: call.sourceNodeId,
      targetNodeId: call.targetNodeId,
      protocol: call.protocol,
      synchronous: call.synchronous,
      configuration: call.configuration ?? undefined,
    })),
  };
}

function read(): Stored[] {
  try {
    const raw = window.localStorage.getItem(KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(isStored);
  } catch {
    return [];
  }
}

function write(entries: Stored[]): void {
  try {
    window.localStorage.setItem(KEY, JSON.stringify(entries.slice(0, LIMIT)));
  } catch {
    return;
  }
}

function isStored(value: unknown): value is Stored {
  if (value === null || typeof value !== "object") return false;
  const entry = value as Partial<Stored>;
  return (
    typeof entry.slug === "string" &&
    typeof entry.at === "number" &&
    typeof entry.graph === "object" &&
    entry.graph !== null &&
    Array.isArray(entry.graph.nodes) &&
    Array.isArray(entry.graph.connections)
  );
}
