import { describe, expect, it } from "vitest";
import { layoutOf, segmentOf, NODE_H, NODE_W } from "./layout";
import type { ArchitectureDto } from "../../types/architecture";

const arch: ArchitectureDto = {
  name: "t",
  shareToken: "s",
  version: 1,
  createdAt: "",
  updatedAt: "",
  nodes: [
    { id: "a", type: "Api", name: "Api", position: { x: 0, y: 0 } },
    { id: "b", type: "Database", name: "Db", position: { x: 300, y: 0 } },
  ],
  connections: [{ id: "c", sourceNodeId: "a", targetNodeId: "b", protocol: "Sql", synchronous: true }],
};

describe("el encuadre del diagrama", () => {
  it("abarca todos los nodos con un margen", () => {
    const layout = layoutOf(arch);
    expect(layout.width).toBe(300 + NODE_W + 56);
    expect(layout.height).toBeGreaterThanOrEqual(NODE_H + 56);
  });

  it("dibuja la flecha de borde a borde, no de centro a centro", () => {
    const layout = layoutOf(arch);
    const segment = segmentOf(arch.connections[0], layout.boxes)!;
    expect(segment.x1).toBe(NODE_W);
    expect(segment.x2).toBe(300);
    expect(segment.y1).toBe(NODE_H / 2);
  });
});
