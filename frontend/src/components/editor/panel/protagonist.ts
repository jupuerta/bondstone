import type { FindingElement } from "../../../types/architecture";

export interface CallEnds {
  readonly source: string;
  readonly target: string;
}

export type CallLookup = ReadonlyMap<string, CallEnds>;

export function callLookup(connections: ReadonlyArray<{ id: string; sourceNodeId: string; targetNodeId: string }>): CallLookup {
  return new Map(connections.map((call) => [call.id, { source: call.sourceNodeId, target: call.targetNodeId }]));
}

function touched(element: FindingElement, calls: CallLookup): string[] {
  if (element.type === "Node") return [element.id];

  const ends = calls.get(element.id);
  return ends ? [ends.source, ends.target] : [];
}

export function protagonistOf(finding: { elements: readonly FindingElement[] }, calls: CallLookup): string | null {
  if (finding.elements.length === 0) return null;

  let candidates: string[] | null = null;

  for (const element of finding.elements) {
    const nodes = touched(element, calls);
    if (nodes.length === 0) continue;

    candidates = candidates === null ? nodes : candidates.filter((id) => nodes.includes(id));
    if (candidates.length === 0) return null;
  }

  if (candidates === null || candidates.length === 0) return null;
  if (candidates.length === 1) return candidates[0];

  const [first] = finding.elements;
  return first.type === "Connection" ? (calls.get(first.id)?.target ?? null) : null;
}

export interface ProtagonistGroup<T> {
  readonly protagonist: string | null;
  readonly findings: readonly T[];
}

export function groupByProtagonist<T extends { elements: readonly FindingElement[] }>(
  findings: readonly T[],
  calls: CallLookup,
): ProtagonistGroup<T>[] {
  const groups: ProtagonistGroup<T>[] = [];
  const byProtagonist = new Map<string, T[]>();

  for (const finding of findings) {
    const protagonist = protagonistOf(finding, calls);
    if (protagonist === null) continue;

    const existing = byProtagonist.get(protagonist);
    if (existing) existing.push(finding);
    else byProtagonist.set(protagonist, [finding]);
  }

  const placed = new Set<T>();

  for (const finding of findings) {
    if (placed.has(finding)) continue;

    const protagonist = protagonistOf(finding, calls);
    const shared = protagonist === null ? undefined : byProtagonist.get(protagonist);

    if (shared && shared.length > 1) {
      shared.forEach((member) => placed.add(member));
      groups.push({ protagonist, findings: shared });
    } else {
      placed.add(finding);
      groups.push({ protagonist: null, findings: [finding] });
    }
  }

  return groups;
}
