import type { ElementState, FindingElement, SimulationResult } from "../../../types/architecture";

export function waveStates(result: SimulationResult | null, visibleStep: number): ElementState[] | null {
  if (!result) return null;
  return result.states.map((state) => (state.propagationStep <= visibleStep ? state : { ...state, state: "Healthy", mechanism: null, causedBy: null }));
}

export function tintStates(tinted: ReadonlyMap<string, ElementState>, elements: readonly FindingElement[]): ElementState[] {
  return elements.map(
    (element) =>
      tinted.get(element.id) ?? {
        type: element.type,
        id: element.id,
        state: "Healthy",
        reason: { key: element.type === "Node" ? "sim.node.healthy" : "sim.edge.healthy" },
        mechanism: null,
        causedBy: null,
        propagationStep: 0,
      },
  );
}
