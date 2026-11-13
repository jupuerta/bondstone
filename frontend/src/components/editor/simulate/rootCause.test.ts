import { describe, expect, it } from "vitest";
import type { ElementState } from "../../../types/architecture";
import { containedStates, groupByRootCause } from "./rootCause";

function state(id: string, health: ElementState["state"], extra: Partial<ElementState> = {}): ElementState {
  return { type: "Node", id, state: health, reason: { key: "x" }, mechanism: null, propagationStep: 0, ...extra };
}

describe("groupByRootCause", () => {
  it("walks causedBy up to the injected fault and groups the fallen under it", () => {
    const groups = groupByRootCause([
      state("db", "Down"),
      state("api", "Down", { causedBy: { type: "Node", id: "db" }, propagationStep: 1 }),
      state("edge", "Down", { causedBy: { type: "Node", id: "api" }, propagationStep: 2 }),
      state("cache", "Healthy"),
    ]);

    expect(groups).toHaveLength(2);
    expect(groups[0].cause).toEqual({ type: "Node", id: "db" });
    expect(groups[0].members.map((member) => member.id)).toEqual(["api", "edge"]);
    expect(groups[1]).toMatchObject({ key: "injected", cause: null });
    expect(groups[1].members.map((member) => member.id)).toEqual(["db"]);
  });

  it("puts the biggest group first and ignores the healthy", () => {
    const groups = groupByRootCause([
      state("a", "Down"),
      state("b", "Down"),
      state("c", "Degraded", { causedBy: { type: "Node", id: "b" } }),
      state("d", "Degraded", { causedBy: { type: "Node", id: "b" } }),
      state("e", "Degraded", { causedBy: { type: "Node", id: "b" } }),
      state("f", "Healthy", { causedBy: { type: "Node", id: "b" } }),
    ]);

    expect(groups.map((group) => group.key)).toEqual(["Node:b", "injected"]);
    expect(groups[0].members).toHaveLength(3);
  });

  it("treats a cause the simulator did not describe, and a cycle, as what you broke", () => {
    const groups = groupByRootCause([
      state("a", "Down", { causedBy: { type: "Node", id: "ghost" } }),
      state("b", "Down", { causedBy: { type: "Node", id: "c" } }),
      state("c", "Down", { causedBy: { type: "Node", id: "b" } }),
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0].key).toBe("injected");
    expect(groups[0].members.map((member) => member.id)).toEqual(["a", "b", "c"]);
  });
});

describe("containedStates", () => {
  it("keeps only the elements a mechanism held", () => {
    const held = containedStates([state("a", "Degraded", { mechanism: "fallback" }), state("b", "Down"), state("c", "Healthy", { mechanism: "queue" })]);

    expect(held.map((entry) => `${entry.id}:${entry.mechanism}`)).toEqual(["a:fallback", "c:queue"]);
  });
});
