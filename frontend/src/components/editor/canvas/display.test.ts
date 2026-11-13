import { describe, expect, it } from "vitest";
import type { ArchitectureDto, ElementState } from "../../../types/architecture";
import { displayEdges, displayNodes, nodeHeight, nodeStatusOf, statesOf, statusTone } from "./display";

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

const label = (source: string, target: string) => `Llamada de ${source} a ${target}`;

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
    const [edge] = displayEdges(architecture, nodeStates, new Map(), null, null, label);

    expect(edge.data?.state).toBe("Down");
  });

  it("names the call by its two components, so a screen reader does not read two ids", () => {
    const [edge] = displayEdges(architecture, new Map(), new Map(), null, null, label);

    expect(edge.ariaLabel).toBe("Llamada de Api a Db");
  });

  it("carries the backend's rps onto the call, and leaves it empty when nobody declared traffic", () => {
    const [loaded] = displayEdges(architecture, new Map(), new Map(), [{ connectionId: "call", rps: 120 }, { connectionId: "other", rps: 400 }], null, label);
    const [silent] = displayEdges(architecture, new Map(), new Map(), null, null, label);

    expect(loaded.data?.flow?.rps).toBe(120);
    expect(silent.data?.flow).toBeNull();
  });
});

describe("statusTone", () => {
  const status = (health: ElementState["state"] | null, extra: Partial<{ injected: boolean; dragged: boolean; mechanism: ElementState["mechanism"] }> = {}) => ({
    health,
    injected: false,
    dragged: false,
    mechanism: null,
    ...extra,
  });

  it("says nothing about a component outside a simulation", () => {
    expect(statusTone(null)).toEqual({ color: null, filled: false, state: null, detail: [] });
  });

  it("puts the state on the first line and the cause on the second", () => {
    expect(statusTone(status("Down", { injected: true }))).toEqual({
      color: "var(--af-hot-panel)",
      filled: true,
      state: { key: "web.engine.state.Down", upper: true },
      detail: ["web.engine.injected"],
    });
    expect(statusTone(status("Down", { dragged: true })).detail).toEqual(["web.engine.dragged"]);
  });

  it("leaves the second line empty when nothing else is to be said", () => {
    expect(statusTone(status("Down")).detail).toEqual([]);
    expect(statusTone(status("Degraded"))).toMatchObject({ filled: false, state: { key: "web.engine.state.Degraded", upper: false }, detail: [] });
  });

  it("names the mechanism on the second line of a degraded component", () => {
    expect(statusTone(status("Degraded", { mechanism: "circuitBreaker" })).detail).toEqual(["ui.mechanism.circuitBreaker"]);
  });

  it("raises the mechanism to the first line when the component is healthy, which is the only thing it has to say", () => {
    expect(statusTone(status("Healthy", { mechanism: "fallback" }))).toEqual({
      color: "var(--af-warm-panel)",
      filled: false,
      state: { key: "ui.mechanism.fallback", upper: false },
      detail: [],
    });
    expect(statusTone(status("Healthy")).state).toBeNull();
  });

  it("says 'no response' about a component the simulator did not mention", () => {
    expect(statusTone(status(null)).state).toEqual({ key: "web.engine.state.none", upper: false });
  });
});

describe("nodeHeight", () => {
  it("grows the node in Simulate, where the state needs a line of its own", () => {
    expect(nodeHeight(false)).toBe(62);
    expect(nodeHeight(true)).toBe(78);
  });

  it("gives every node the same height in a simulation, so the wave does not resize the graph", () => {
    const states = statesOf([state("Node", "db", "Down")], "Node");
    const heights = displayNodes(architecture, states, new Set(["db"]), true, null).map((node) => node.height);

    expect(heights).toEqual([78, 78]);
    expect(displayNodes(architecture, new Map(), new Set(), false, null).map((node) => node.height)).toEqual([62, 62]);
  });
});
