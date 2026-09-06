import { describe, expect, it } from "vitest";
import { STARTER_ARCHITECTURES, starterToDto } from "./starterArchitectures";

describe("the four starting architectures", () => {
  it("are four, each with a score and a lesson", () => {
    expect(STARTER_ARCHITECTURES.map((starter) => starter.slug)).toEqual(["minimal-api-db", "resilient-async", "gateway-microservices", "worst-case"]);
    for (const starter of STARTER_ARCHITECTURES) {
      expect(starter.score).toBeGreaterThan(0);
      expect(starter.label).toMatch(/^ui\.starter\./);
      expect(starter.lesson).toMatch(/\.lesson$/);
    }
  });

  it("name every end of every call", () => {
    for (const starter of STARTER_ARCHITECTURES) {
      const names = new Set(starter.nodes.map((node) => node.name));
      for (const connection of starter.connections) {
        expect(names.has(connection.from)).toBe(true);
        expect(names.has(connection.to)).toBe(true);
      }
    }
  });
});

describe("importing a starter", () => {
  it("gives every component a fresh id and wires the calls by name", () => {
    const starter = STARTER_ARCHITECTURES[2];
    const { nodes, connections } = starterToDto(starter);
    const ids = new Set(nodes.map((node) => node.id));

    expect(ids.size).toBe(starter.nodes.length);
    expect(connections).toHaveLength(starter.connections.length);
    for (const connection of connections) {
      expect(ids.has(connection.sourceNodeId)).toBe(true);
      expect(ids.has(connection.targetNodeId)).toBe(true);
    }
  });

  it("derives the protocol from the two ends and keeps the declared resilience", () => {
    const { nodes, connections } = starterToDto(STARTER_ARCHITECTURES[0]);
    const byId = new Map(nodes.map((node) => [node.id, node]));
    const [call] = connections;

    expect(byId.get(call.sourceNodeId)?.type).toBe("Api");
    expect(byId.get(call.targetNodeId)?.type).toBe("Database");
    expect(call.protocol).toBe("Sql");
    expect(call.configuration).toEqual({ timeoutSeconds: 5 });
  });

  it("makes new ids on every import, so two imports never collide", () => {
    const first = starterToDto(STARTER_ARCHITECTURES[0]);
    const second = starterToDto(STARTER_ARCHITECTURES[0]);
    expect(first.nodes[0].id).not.toBe(second.nodes[0].id);
  });
});
