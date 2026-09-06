import { beforeEach, describe, expect, it } from "vitest";
import { rememberStartingPoint, startingPointOf, type ComparableGraph } from "./startingPoint";

const graph: ComparableGraph = {
  nodes: [{ id: "api", type: "Api", name: "Api", position: { x: 10, y: 20 }, configuration: { instances: 1 } }],
  connections: [{ id: "c", sourceNodeId: "api", targetNodeId: "db", protocol: "Sql", synchronous: true, configuration: { timeoutSeconds: 2 } }],
};

describe("the starting point of a challenge", () => {
  beforeEach(() => window.localStorage.clear());

  it("is nothing until somebody starts the challenge", () => {
    expect(startingPointOf("single-point-of-failure")).toBeNull();
    expect(startingPointOf(null)).toBeNull();
  });

  it("remembers the graph as it was handed, node by node and call by call", () => {
    rememberStartingPoint("single-point-of-failure", graph);

    expect(startingPointOf("single-point-of-failure")).toEqual(graph);
  });

  it("keeps one entry per challenge, the latest start", () => {
    rememberStartingPoint("s", graph);
    rememberStartingPoint("s", { nodes: [], connections: [] });

    expect(startingPointOf("s")).toEqual({ nodes: [], connections: [] });
    expect(JSON.parse(window.localStorage.getItem("archforge.challengeStarts") ?? "[]")).toHaveLength(1);
  });

  it("drops the oldest past fifteen, so the key cannot grow without end", () => {
    for (let index = 0; index < 20; index += 1) rememberStartingPoint(`slug-${index}`, graph);

    expect(JSON.parse(window.localStorage.getItem("archforge.challengeStarts") ?? "[]")).toHaveLength(15);
    expect(startingPointOf("slug-19")).not.toBeNull();
    expect(startingPointOf("slug-0")).toBeNull();
  });

  it("ignores whatever else somebody wrote under the key", () => {
    window.localStorage.setItem("archforge.challengeStarts", JSON.stringify([{ slug: "x" }, "junk"]));

    expect(startingPointOf("x")).toBeNull();
  });
});
