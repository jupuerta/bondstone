import { describe, expect, it } from "vitest";
import type { ArchitectureDto, ElementState } from "../../../types/architecture";
import { displayEdges, displayNodes, nodeStatusOf, statesOf } from "./display";

const architecture: ArchitectureDto = {
  name: "x",
  shareToken: "t",
  version: 1,
  createdAt: "",
  updatedAt: "",
  nodes: [
    { id: "api", type: "Api", name: "Api", position: { x: 0, y: 0 } },
    { id: "db", type: "Database", name: "Db", position: { x: 300, y: 0 } },
  ],
  connections: [{ id: "call", sourceNodeId: "api", targetNodeId: "db", protocol: "Sql", synchronous: true }],
};

function state(type: ElementState["type"], id: string, health: ElementState["state"], extra: Partial<ElementState> = {}): ElementState {
  return { type, id, state: health, reason: { key: "x" }, mechanism: null, propagationStep: 0, ...extra };
}

describe("nodeStatusOf", () => {
  it("says nothing outside a simulation, so a healthy drawing carries no marks", () => {
    expect(nodeStatusOf("api", new Map(), new Set(["api"]), false)).toBeNull();
  });

  it("tells an injected fault from a component dragged down by it", () => {
    const states = statesOf([state("Node", "db", "Down"), state("Node", "api", "Down", { causedBy: { type: "Node", id: "db" } })], "Node");

    expect(nodeStatusOf("db", states, new Set(["db"]), true)).toEqual({ health: "Down", injected: true, dragged: false, mechanism: null });
    expect(nodeStatusOf("api", states, new Set(["db"]), true)).toEqual({ health: "Down", injected: false, dragged: true, mechanism: null });
  });

  it("reports no response for a component the simulator did not mention", () => {
    expect(nodeStatusOf("api", new Map(), new Set(), true)?.health).toBeNull();
  });
});

describe("displayNodes and displayEdges", () => {
  it("marks the selected element and nothing else", () => {
    const nodes = displayNodes(architecture, new Map(), new Set(), false, { kind: "node", id: "db" });

    expect(nodes.map((node) => node.selected)).toEqual([false, true]);
  });

  it("draws the call into a fallen component as fallen, even when the simulator only spoke about the node", () => {
    const nodeStates = statesOf([state("Node", "db", "Down")], "Node");
    const [edge] = displayEdges(architecture, nodeStates, new Map(), null, null);

    expect(edge.data?.state).toBe("Down");
  });

  it("carries the backend's rps onto the call, and leaves it empty when nobody declared traffic", () => {
    const [loaded] = displayEdges(architecture, new Map(), new Map(), [{ connectionId: "call", rps: 120 }, { connectionId: "other", rps: 400 }], null);
    const [silent] = displayEdges(architecture, new Map(), new Map(), null, null);

    expect(loaded.data?.flow?.rps).toBe(120);
    expect(silent.data?.flow).toBeNull();
  });
});
