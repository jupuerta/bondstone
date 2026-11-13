import { describe, expect, it } from "vitest";
import { diffArchitectures, hasChanges } from "./baseline";
import type { ArchitectureDto, ConnectionDto, NodeDto } from "../types/architecture";

const node = (id: string, name: string, configuration: Record<string, unknown> = {}): NodeDto => ({
  id,
  type: "Service",
  name,
  position: { x: 0, y: 0 },
  configuration,
});

const connection = (id: string, overrides: Partial<ConnectionDto> = {}): ConnectionDto => ({
  id,
  sourceNodeId: "n1",
  targetNodeId: "n2",
  protocol: "Http",
  synchronous: true,
  configuration: {},
  ...overrides,
});

const architecture = (nodes: NodeDto[], connections: ConnectionDto[] = []): ArchitectureDto => ({
  name: "a",
  shareToken: "t",
  version: 1,
  createdAt: "0001-01-01T00:00:00",
  updatedAt: "0001-01-01T00:00:00",
  nodes,
  connections,
});

describe("nothing happened", () => {
  it("the same graph twice has no changes", () => {
    const one = architecture([node("n1", "Api"), node("n2", "Db")], [connection("c1")]);

    expect(hasChanges(diffArchitectures(one, one))).toBe(false);
  });

  it("moving a component is not a change", () => {
    const before = architecture([node("n1", "Api")]);
    const after = architecture([{ ...before.nodes[0], position: { x: 900, y: 40 } }]);

    expect(hasChanges(diffArchitectures(before, after))).toBe(false);
  });

  it("a configuration key set to null is a key that is not there", () => {
    const before = architecture([node("n1", "Api", { replicas: 2 })]);
    const after = architecture([node("n1", "Api", { replicas: 2, tls: null })]);

    expect(hasChanges(diffArchitectures(before, after))).toBe(false);
  });

  it("the order the two graphs arrive in changes nothing", () => {
    const before = architecture([node("n1", "Api"), node("n2", "Db")], [connection("c1")]);
    const after = architecture([node("n2", "Db"), node("n1", "Api")], [connection("c1")]);

    expect(hasChanges(diffArchitectures(before, after))).toBe(false);
  });
});

describe("components", () => {
  it("added and removed are told apart", () => {
    const before = architecture([node("n1", "Api")]);
    const after = architecture([node("n2", "Db")]);
    const diff = diffArchitectures(before, after);

    expect(diff.addedNodes.map((n) => n.id)).toEqual(["n2"]);
    expect(diff.removedNodes.map((n) => n.id)).toEqual(["n1"]);
    expect(diff.changedNodes).toHaveLength(0);
  });

  it("a rename is one component, not two", () => {
    const diff = diffArchitectures(
      architecture([node("n1", "Servicio")]),
      architecture([node("n1", "Pedidos")]),
    );

    expect(diff.addedNodes).toHaveLength(0);
    expect(diff.removedNodes).toHaveLength(0);
    expect(diff.changedNodes).toEqual([
      { before: expect.objectContaining({ name: "Servicio" }), after: expect.objectContaining({ name: "Pedidos" }), renamed: true, reconfigured: false },
    ]);
  });

  it("a reconfiguration is reported without a rename", () => {
    const diff = diffArchitectures(
      architecture([node("n1", "Api", { replicas: 1 })]),
      architecture([node("n1", "Api", { replicas: 4 })]),
    );

    expect(diff.changedNodes[0]).toMatchObject({ renamed: false, reconfigured: true });
  });
});

describe("connections", () => {
  it("added, removed and changed are told apart", () => {
    const before = architecture([], [connection("c1"), connection("c2")]);
    const after = architecture([], [connection("c1", { synchronous: false }), connection("c3")]);
    const diff = diffArchitectures(before, after);

    expect(diff.addedConnections.map((c) => c.id)).toEqual(["c3"]);
    expect(diff.removedConnections.map((c) => c.id)).toEqual(["c2"]);
    expect(diff.changedConnections).toHaveLength(1);
    expect(diff.changedConnections[0]).toMatchObject({ synchronous: true, resilience: false });
  });

  it("the two axes are independent", () => {
    const diff = diffArchitectures(
      architecture([], [connection("c1")]),
      architecture([], [connection("c1", { synchronous: false, configuration: { retries: 3 } })]),
    );

    expect(diff.changedConnections[0]).toMatchObject({ synchronous: true, resilience: true });
  });
});

describe("the lists are ordered", () => {
  it("components come out in the document's order, not the input's", () => {
    const before = architecture([]);
    const after = architecture([
      { ...node("n3", "Zeta"), type: "Database" },
      node("n1", "Beta"),
      node("n2", "Alfa"),
    ]);

    expect(diffArchitectures(before, after).addedNodes.map((n) => n.name)).toEqual(["Zeta", "Alfa", "Beta"]);
  });

  it("a connection between two deleted components is still named", () => {
    const before = architecture(
      [node("n1", "Api"), node("n2", "Db")],
      [connection("c1", { sourceNodeId: "n1", targetNodeId: "n2" })],
    );
    const diff = diffArchitectures(before, architecture([]));

    expect(diff.removedConnections).toHaveLength(1);
    expect(diff.removedNodes.map((n) => n.name)).toEqual(["Api", "Db"]);
  });
});
