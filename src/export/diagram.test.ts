import { describe, expect, it } from "vitest";
import { diagramSource, fitLabel, fitLines, healthOf } from "./diagram";
import type { ArchitectureDto, SimulationResult } from "../types/architecture";

const architecture: ArchitectureDto = {
  name: "Tienda",
  shareToken: "abc",
  version: 3,
  createdAt: "2026-08-26T00:00:00Z",
  updatedAt: "2026-08-26T00:00:00Z",
  nodes: [
    { id: "db", type: "Database", name: "Postgres", position: { x: 600, y: 0 } },
    { id: "api", type: "Api", name: "Pedidos", position: { x: 0, y: 0 } },
  ],
  connections: [{ id: "c1", sourceNodeId: "api", targetNodeId: "db", protocol: "Sql", synchronous: true }],
};

describe("the drawing the export carries", () => {
  it("lists what the Markdown lists, in the order the Markdown lists it", () => {
    const { nodes, calls } = diagramSource(architecture);

    expect(nodes.map((node) => node.name)).toEqual(["Pedidos", "Postgres"]);
    expect(calls).toHaveLength(1);
  });

  it("drops a call whose ends are not both on the drawing", () => {
    const orphan = { ...architecture, connections: [{ ...architecture.connections[0], targetNodeId: "gone" }] };

    expect(diagramSource(orphan).calls).toEqual([]);
  });

  it("reads the health of a simulation off its node states and nothing else", () => {
    const simulation = {
      states: [
        { type: "Node", id: "db", state: "Down" },
        { type: "Connection", id: "c1", state: "Down" },
      ],
    } as unknown as SimulationResult;

    expect([...healthOf(simulation)]).toEqual([["db", "Down"]]);
    expect(healthOf(null).size).toBe(0);
  });
});

describe("fitLines", () => {
  it("leaves a name that fits on one line, and does not invent a second", () => {
    expect(fitLines("Postgres", 200, 12)).toEqual(["Postgres"]);
    expect(fitLines("API Checkout", 88, 13)).toEqual(["API Checkout"]);
  });

  it("breaks a long name at a space instead of cutting it", () => {
    expect(fitLines("Cola de pedidos", 88, 13)).toEqual(["Cola de", "pedidos"]);
    expect(fitLines("Pasarela de pago", 88, 13)).toEqual(["Pasarela de", "pago"]);
  });

  it("cuts the last line when even two are not enough", () => {
    const lines = fitLines("Servicio de recomendaciones personalizadas", 88, 13);

    expect(lines).toHaveLength(2);
    expect(lines[1].endsWith("…")).toBe(true);
  });

  it("cuts rather than wraps when it is only allowed one line", () => {
    expect(fitLines("Cola de pedidos", 88, 13, 1)).toEqual(["Cola de ped…"]);
  });

  it("has nothing to say about an empty name", () => {
    expect(fitLines("   ", 200, 12)).toEqual([]);
    expect(fitLines("Postgres", 2, 12)).toEqual([]);
  });
});

describe("fitLabel", () => {
  it("leaves a name that fits alone", () => {
    expect(fitLabel("Pedidos", 200, 12)).toBe("Pedidos");
  });

  it("cuts a name that does not, and says that it cut it", () => {
    const cut = fitLabel("Servicio de recomendaciones", 60, 12);

    expect(cut.endsWith("…")).toBe(true);
    expect(cut.length).toBeLessThan("Servicio de recomendaciones".length);
  });

  it("gives back nothing rather than overflowing a box with no room", () => {
    expect(fitLabel("Pedidos", 2, 12)).toBe("");
  });
});
