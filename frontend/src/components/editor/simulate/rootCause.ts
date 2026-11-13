import type { ElementState, FindingElement, Mechanism } from "../../../types/architecture";

export interface CauseGroup {
  key: string;
  cause: FindingElement | null;
  members: ElementState[];
}

export function groupByRootCause(states: readonly ElementState[]): CauseGroup[] {
  const byKey = new Map(states.map((state) => [`${state.type}:${state.id}`, state]));
  const groups = new Map<string, CauseGroup>();

  for (const state of states) {
    if (state.state === "Healthy") continue;

    let current = state;
    for (let hop = 0; current.causedBy && hop <= states.length; hop++) {
      const next = byKey.get(`${current.causedBy.type}:${current.causedBy.id}`);
      if (!next) break;
      current = next;
    }

    const cause: FindingElement | null = current === state ? null : { type: current.type, id: current.id };
    const key = cause ? `${cause.type}:${cause.id}` : "injected";

    const group = groups.get(key) ?? { key, cause, members: [] };
    group.members.push(state);
    groups.set(key, group);
  }

  return [...groups.values()].sort((a, b) => b.members.length - a.members.length);
}

export type ContainedState = ElementState & { mechanism: Mechanism };

export function containedStates(states: readonly ElementState[]): ContainedState[] {
  return states.filter((state): state is ContainedState => state.mechanism !== null);
}
