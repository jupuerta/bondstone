import { describe, expect, it } from "vitest";
import type { Edge, Node } from "@xyflow/react";
import type { NodeType } from "../../../types/architecture";
import { addNode, connect, connectedEdges, entryPointOf, forgetDeclaredTraffic, importGraph, moveNodes, nextNodeName, patchEdge, removeEdges, removeNodes, setNodeField } from "./graphEdits";
import type { Graph } from "./graphHistory";
import type { ArchNodeData, ConnectionData } from "./types";

const box = (id: string, type: NodeType, configuration: Record<string, unknown> = {}): Node<ArchNodeData> => ({
  id,
  type,
  position: { x: 0, y: 0 },
  data: { name: id, configuration },
});

const call = (source: string, target: string): Edge<ConnectionData> => ({ id: `${source}->${target}`, source, target, data: { synchronous: true, configuration: {} } });

const graph = (nodes: Node<ArchNodeData>[] = [], edges: Edge<ConnectionData>[] = []): Graph => ({ nodes, edges });

describe("the name a dropped component gets", () => {
  it("counts the ones of its type that already exist", () => {
    expect(nextNodeName("Api", [])).toBe("Api 1");
    expect(nextNodeName("Api", [box("a", "Api"), box("b", "Database")])).toBe("Api 2");
  });

  it("is what addNode writes, at the position dropped", () => {
    const next = addNode(graph([box("a", "Api")]), "Api", { x: 40, y: 80 }, "new");
    expect(next.nodes.map((node) => node.id)).toEqual(["a", "new"]);
    expect(next.nodes[1]).toMatchObject({ type: "Api", position: { x: 40, y: 80 }, data: { name: "Api 2", configuration: {} } });
  });
});

describe("connecting two components", () => {
  const two = graph([box("edge", "Gateway", { requestsPerSecond: 100 }), box("api", "Api", { requestsPerSecond: 400, instances: 2 })]);

  it("adds one synchronous, unconfigured call", () => {
    const next = connect(two, "edge", "api", "c");
    expect(next.edges).toEqual([{ id: "c", source: "edge", target: "api", data: { synchronous: true, configuration: {} } }]);
  });

  it("makes the target forget the traffic it declared as an entry point", () => {
    const next = connect(two, "edge", "api", "c");
    expect(next.nodes.find((node) => node.id === "api")?.data.configuration).toEqual({ instances: 2 });
    expect(next.nodes.find((node) => node.id === "edge")?.data.configuration).toEqual({ requestsPerSecond: 100 });
  });

  it("refuses a loop, a missing end and a duplicate", () => {
    expect(connect(two, "api", "api")).toBe(two);
    expect(connect(two, "api", "nobody")).toBe(two);
    const once = connect(two, "edge", "api", "c");
    expect(connect(once, "edge", "api", "again")).toBe(once);
  });

  it("leaves the nodes alone when the observability scrapes", () => {
    const nodes = [box("api", "Api", { requestsPerSecond: 400 }), box("obs", "Observability")];
    expect(forgetDeclaredTraffic(nodes, "obs", "api")).toBe(nodes);
  });
});

describe("removing", () => {
  const three = graph([box("a", "Api"), box("b", "Database"), box("c", "Cache")], [call("a", "b"), call("a", "c")]);

  it("takes the calls of a removed component with it", () => {
    const next = removeNodes(three, new Set(["b"]));
    expect(next.nodes.map((node) => node.id)).toEqual(["a", "c"]);
    expect(next.edges.map((edge) => edge.id)).toEqual(["a->c"]);
  });

  it("removes a call and nothing else", () => {
    const next = removeEdges(three, new Set(["a->b"]));
    expect(next.nodes).toBe(three.nodes);
    expect(next.edges.map((edge) => edge.id)).toEqual(["a->c"]);
  });

  it("returns the same graph when there is nothing to remove", () => {
    expect(removeNodes(three, new Set())).toBe(three);
    expect(removeEdges(three, new Set())).toBe(three);
  });

  it("counts the calls a component would take with it", () => {
    expect(connectedEdges(three, "a")).toBe(2);
    expect(connectedEdges(three, "c")).toBe(1);
  });
});

describe("moving and configuring", () => {
  it("moves only the components named", () => {
    const next = moveNodes(graph([box("a", "Api"), box("b", "Database")]), new Map([["b", { x: 300, y: 10 }]]));
    expect(next.nodes[0].position).toEqual({ x: 0, y: 0 });
    expect(next.nodes[1].position).toEqual({ x: 300, y: 10 });
  });

  it("writes a field and clears it when emptied", () => {
    const one = setNodeField(graph([box("a", "Api")]), "a", "instances", 3);
    expect(one.nodes[0].data.configuration).toEqual({ instances: 3 });
    expect(setNodeField(one, "a", "instances", undefined).nodes[0].data.configuration).toEqual({});
    expect(setNodeField(one, "a", "instances", "").nodes[0].data.configuration).toEqual({});
  });

  it("patches a call without touching the nodes", () => {
    const one = graph([box("a", "Api"), box("b", "Database")], [call("a", "b")]);
    const next = patchEdge(one, "a->b", { synchronous: false, configuration: { timeoutSeconds: 3 } });
    expect(next.nodes).toBe(one.nodes);
    expect(next.edges[0].data).toEqual({ synchronous: false, configuration: { timeoutSeconds: 3 } });
  });
});

describe("what comes from a starter or a sandbox", () => {
  it("becomes a graph the canvas can draw, and its entry point is the box nobody calls", () => {
    const imported = importGraph(
      [
        { id: "lb", type: "LoadBalancer", name: "LB", position: { x: 0, y: 0 } },
        { id: "api", type: "Api", name: "Api", position: { x: 0, y: 150 }, configuration: { instances: 2 } },
      ],
      [{ id: "c", sourceNodeId: "lb", targetNodeId: "api", protocol: "Http", synchronous: true, configuration: { timeoutSeconds: 3 } }],
    );
    expect(imported.nodes[1].data).toEqual({ name: "Api", configuration: { instances: 2 } });
    expect(imported.edges[0]).toMatchObject({ source: "lb", target: "api", data: { synchronous: true, configuration: { timeoutSeconds: 3 } } });
    expect(entryPointOf(imported, "lb")).toBe(true);
    expect(entryPointOf(imported, "api")).toBe(false);
  });
});
