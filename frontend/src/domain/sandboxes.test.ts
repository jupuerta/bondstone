import { describe, expect, it } from "vitest";
import { MECHANISM_IDS } from "./mechanisms";
import { SANDBOXES, sandboxToDto } from "./sandboxes";

describe("the workshop sandboxes", () => {
  it("has one per mechanism, and every call names two of its own components", () => {
    for (const id of MECHANISM_IDS) {
      const sandbox = SANDBOXES[id];
      const names = new Set(sandbox.nodes.map((node) => node.name));
      expect(sandbox.nodes.length).toBeGreaterThan(0);
      for (const call of sandbox.calls) {
        expect(names.has(call.from)).toBe(true);
        expect(names.has(call.to)).toBe(true);
      }
    }
  });

  it("turns into a saveable drawing with derived protocols", () => {
    const { nodes, connections } = sandboxToDto(SANDBOXES.timeout);
    const byId = new Map(nodes.map((node) => [node.id, node]));

    expect(nodes).toHaveLength(3);
    for (const call of connections) {
      expect(byId.has(call.sourceNodeId)).toBe(true);
      expect(byId.has(call.targetNodeId)).toBe(true);
      expect(["Http", "Sql", "Amqp"]).toContain(call.protocol);
    }
  });
});
