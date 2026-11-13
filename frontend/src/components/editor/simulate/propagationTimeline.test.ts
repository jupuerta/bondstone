import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import type { ElementState, HealthState, Mechanism, SimulationResult } from "../../../types/architecture";
import { frameIndexAt, propagationFrames } from "./propagationTimeline";

function state(overrides: Partial<ElementState> & { propagationStep: number }): ElementState {
  return {
    type: "Node",
    id: "n1",
    state: "Healthy",
    reason: { key: "sim.node.healthy", args: null },
    mechanism: null,
    causedBy: null,
    ...overrides,
  };
}

function broken(step: number, id: string, health: HealthState = "Down", mechanism: Mechanism | null = null) {
  return state({ propagationStep: step, id, state: health, mechanism });
}

describe("propagationFrames", () => {
  it("has nothing to show for an architecture that held", () => {
    expect(propagationFrames([state({ propagationStep: 0, id: "a" }), state({ propagationStep: 0, id: "b" })])).toEqual(
      [],
    );
  });

  it("leaves the healthy elements out of the frame they arrive in", () => {
    const frames = propagationFrames([
      broken(0, "injected"),
      state({ propagationStep: 0, id: "green" }),
      state({ propagationStep: 0, id: "also-green" }),
    ]);

    expect(frames).toHaveLength(1);
    expect(frames[0].changed.map((element) => element.id)).toEqual(["injected"]);
  });

  it("groups by step and orders the frames by it", () => {
    const frames = propagationFrames([broken(2, "c"), broken(0, "a"), broken(1, "b"), broken(1, "b2")]);

    expect(frames.map((frame) => frame.step)).toEqual([0, 1, 2]);
    expect(frames[1].changed.map((element) => element.id)).toEqual(["b", "b2"]);
  });

  it("keeps the gaps the engine's second phase leaves", () => {
    expect(propagationFrames([broken(0, "a"), broken(1, "b"), broken(3, "c")]).map((f) => f.step)).toEqual([0, 1, 3]);
  });

  it("puts what went down before what merely got worse, and keeps the contract's order on a tie", () => {
    const frames = propagationFrames([
      broken(1, "degraded-first", "Degraded"),
      broken(1, "down-1"),
      broken(1, "down-2"),
    ]);

    expect(frames[0].changed.map((element) => element.id)).toEqual(["down-1", "down-2", "degraded-first"]);
  });

  it("counts the two severities separately, which is what tints the dot", () => {
    const [frame] = propagationFrames([broken(1, "a"), broken(1, "b", "Degraded"), broken(1, "c", "Degraded")]);

    expect(frame.down).toBe(1);
    expect(frame.degraded).toBe(2);
  });

  it("marks the frame where something stopped", () => {
    const frames = propagationFrames([broken(0, "a"), broken(1, "b", "Degraded", "fallback"), broken(2, "c")]);

    expect(frames.map((frame) => frame.contained)).toEqual([false, true, false]);
  });

  it("keeps a containment that cost nothing, which is the one frame worth never losing", () => {
    const frames = propagationFrames([broken(0, "a"), state({ propagationStep: 1, id: "b", mechanism: "queue" })]);

    expect(frames).toHaveLength(2);
    expect(frames[1].contained).toBe(true);
  });

  it("reads the recorded simulation the way the panel will", () => {
    const recorded: SimulationResult = JSON.parse(
      readFileSync(join(__dirname, "../../../export/__fixtures__/minimal-api-db.down.simulation.json"), "utf8"),
    );

    const frames = propagationFrames(recorded.states);

    expect(frames.map((frame) => frame.step)).toEqual([0, 1]);
    expect(frames[0].changed.map((element) => element.reason.key)).toEqual(["sim.node.injected"]);
    expect(frames[1].changed.map((element) => element.reason.key)).toEqual([
      "sim.node.unprotected",
      "sim.edge.unprotected",
    ]);
    expect(frames[1].changed.map((element) => element.type)).toEqual(["Node", "Connection"]);
    expect(frames.every((frame) => frame.contained)).toBe(false);
  });
});

describe("frameIndexAt", () => {
  const frames = propagationFrames([broken(0, "a"), broken(1, "b"), broken(3, "c")]);

  it("follows the wave down the reel", () => {
    expect(frameIndexAt(frames, 0)).toBe(0);
    expect(frameIndexAt(frames, 1)).toBe(1);
    expect(frameIndexAt(frames, 3)).toBe(2);
  });

  it("stays on the last frame that was reached across a gap", () => {
    expect(frameIndexAt(frames, 2)).toBe(1);
  });

  it("clamps rather than answering 'none'", () => {
    expect(frameIndexAt(frames, -1)).toBe(0);
    expect(frameIndexAt(frames, 99)).toBe(2);
  });
});
