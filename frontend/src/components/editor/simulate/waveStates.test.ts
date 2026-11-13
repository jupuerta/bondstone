import { describe, expect, it } from "vitest";
import type { ElementState, SimulationResult } from "../../../types/architecture";
import { tintStates, waveStates } from "./waveStates";

function state(id: string, health: ElementState["state"], step: number, extra: Partial<ElementState> = {}): ElementState {
  return { type: "Node", id, state: health, reason: { key: "x" }, mechanism: null, propagationStep: step, ...extra };
}

const result = {
  states: [state("db", "Down", 0), state("api", "Down", 1, { causedBy: { type: "Node", id: "db" }, mechanism: "fallback" }), state("edge", "Degraded", 2)],
} as SimulationResult;

describe("waveStates", () => {
  it("shows nothing without a result", () => {
    expect(waveStates(null, 3)).toBeNull();
  });

  it("keeps every element on screen and reveals each one at its step", () => {
    const at0 = waveStates(result, 0)!;
    const at1 = waveStates(result, 1)!;

    expect(at0.map((entry) => entry.state)).toEqual(["Down", "Healthy", "Healthy"]);
    expect(at0[1]).toMatchObject({ mechanism: null, causedBy: null });
    expect(at1.map((entry) => entry.state)).toEqual(["Down", "Down", "Healthy"]);
    expect(at1[1]).toBe(result.states[1]);
  });

  it("is the whole result once the wave has finished", () => {
    expect(waveStates(result, 2)).toEqual(result.states);
  });
});

describe("tintStates", () => {
  it("paints the tinted elements and leaves the rest healthy", () => {
    const tinted = new Map([["db", state("db", "Down", 0)]]);
    const states = tintStates(tinted, [
      { type: "Node", id: "db" },
      { type: "Node", id: "api" },
      { type: "Connection", id: "call" },
    ]);

    expect(states.map((entry) => `${entry.id}:${entry.state}`)).toEqual(["db:Down", "api:Healthy", "call:Healthy"]);
    expect(states[2].reason.key).toBe("sim.edge.healthy");
  });
});
