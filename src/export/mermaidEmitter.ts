import type { ConnectionDto, HealthState, NodeDto } from "../types/architecture";
import { mermaidIdentifier, mermaidLabel } from "./markdownEscape";

export function emitMermaid(
  nodes: readonly NodeDto[],
  connections: readonly ConnectionDto[],
  states?: ReadonlyMap<string, HealthState>,
): string {
  const identifiers = identifiersFor(nodes);
  const lines: string[] = ["```mermaid", "flowchart LR"];

  for (const node of nodes) {
    lines.push(`  ${identifiers.get(node.id)}["${mermaidLabel(`${node.type}: ${node.name}`)}"]`);
  }

  if (connections.length > 0) lines.push("");

  for (const connection of connections) {
    const source = identifiers.get(connection.sourceNodeId);
    const target = identifiers.get(connection.targetNodeId);
    if (source === undefined || target === undefined) continue;

    lines.push(`  ${source} ${connection.synchronous ? "-->" : "-.->"}|${connection.protocol}| ${target}`);
  }

  lines.push(...stateLines(nodes, identifiers, states));
  lines.push("```");

  return lines.join("\n");
}

function stateLines(
  nodes: readonly NodeDto[],
  identifiers: ReadonlyMap<string, string>,
  states?: ReadonlyMap<string, HealthState>,
): string[] {
  if (states === undefined) return [];

  const tinted = nodes
    .filter((n) => {
      const state = states.get(n.id);
      return state !== undefined && state !== "Healthy";
    })
    .map((n) => ({ identifier: identifiers.get(n.id)!, state: states.get(n.id)! }));

  if (tinted.length === 0) return [];

  const lines = [""];

  if (tinted.some((t) => t.state === "Down")) {
    lines.push("  classDef down fill:#fee2e2,stroke:#dc2626,color:#7f1d1d");
  }
  if (tinted.some((t) => t.state === "Degraded")) {
    lines.push("  classDef degraded fill:#fef3c7,stroke:#d97706,color:#78350f");
  }

  for (const { identifier, state } of tinted) {
    lines.push(`  class ${identifier} ${state === "Down" ? "down" : "degraded"}`);
  }

  return lines;
}

function identifiersFor(nodes: readonly NodeDto[]): Map<string, string> {
  const identifiers = new Map<string, string>();
  const taken = new Set<string>();

  for (const node of nodes) {
    const candidate = mermaidIdentifier(node.name);
    let unique = candidate;
    let suffix = 1;

    while (taken.has(unique)) {
      suffix++;
      unique = `${candidate}_${suffix}`;
    }

    taken.add(unique);
    identifiers.set(node.id, unique);
  }

  return identifiers;
}
