import { describe, expect, it } from "vitest";
import { drawn, graphDelta, moved } from "./graphDelta";
import type { ComparableGraph } from "./graphDelta";
import type { ConnectionDto, NodeDto, NodeType } from "../../../types/architecture";
import { applyFix } from "./applyFix";
import type { Graph } from "./graphHistory";
import type { AppliedFix } from "../../../types/architecture";
import { flowToUpdateRequest } from "./mapping";
import type { Node as FlowNode, Edge as FlowEdge } from "@xyflow/react";
import type { ArchNodeData, ConnectionData } from "./types";

function node(id: string, name: string, type: NodeType = "Service", configuration?: Record<string, unknown>): NodeDto {
  return { id, type, name, position: { x: 0, y: 0 }, configuration };
}

function call(
  id: string,
  from: string,
  to: string,
  options: { synchronous?: boolean; protocol?: ConnectionDto["protocol"]; configuration?: object } = {},
): ConnectionDto {
  return {
    id,
    sourceNodeId: from,
    targetNodeId: to,
    protocol: options.protocol ?? "Http",
    synchronous: options.synchronous ?? true,
    configuration: options.configuration as ConnectionDto["configuration"],
  };
}

const graph = (nodes: NodeDto[], connections: ConnectionDto[] = []): ComparableGraph => ({ nodes, connections });

describe("graphDelta", () => {
  it("says nothing about a graph that did not change", () => {
    const before = graph([node("a", "Api", "Api"), node("b", "Db", "Database")], [call("e", "a", "b")]);
    const after = graph([node("a", "Api", "Api"), node("b", "Db", "Database")], [call("e", "a", "b")]);

    expect(moved(graphDelta(before, after))).toBe(0);
    expect(drawn(graphDelta(before, after)).size).toBe(0);
  });

  it("keeps a box that only moved out of the delta", () => {
    const before = graph([node("a", "Api", "Api")]);
    const after = graph([{ ...node("a", "Api", "Api"), position: { x: 400, y: 90 } }]);

    expect(moved(graphDelta(before, after))).toBe(0);
  });

  it("separates what was added from what was retuned", () => {
    const before = graph(
      [node("a", "Api", "Api"), node("b", "Db", "Database")],
      [call("e", "a", "b", { protocol: "Sql" })],
    );
    const after = graph(
      [node("a", "Api", "Api"), node("b", "Db", "Database"), node("c", "Db cache", "Cache")],
      [call("e", "a", "c", { protocol: "Sql" }), call("f", "c", "b", { protocol: "Sql" })],
    );

    const delta = graphDelta(before, after);

    expect([...delta.addedNodes]).toEqual(["c"]);
    expect([...delta.addedCalls]).toEqual(["f"]);
    expect([...delta.changedCalls]).toEqual(["e"]);
    expect([...delta.removedNodes]).toEqual([]);
  });

  it("reads a configuration that grew a key as a change and not as a new call", () => {
    const before = graph([node("a", "Api", "Api"), node("b", "Db", "Database")], [call("e", "a", "b")]);
    const after = graph(
      [node("a", "Api", "Api"), node("b", "Db", "Database")],
      [call("e", "a", "b", { configuration: { timeoutSeconds: 5 } })],
    );

    const delta = graphDelta(before, after);

    expect([...delta.changedCalls]).toEqual(["e"]);
    expect(delta.addedCalls.size).toBe(0);
  });

  it("treats an absent configuration and an empty one as the same call", () => {
    const before = graph([node("a", "Api", "Api")], [call("e", "a", "a", { configuration: undefined })]);
    const after = graph([node("a", "Api", "Api")], [call("e", "a", "a", { configuration: {} })]);

    expect(moved(graphDelta(before, after))).toBe(0);
  });

  it("does not care in which order a configuration was written", () => {
    const before = graph([node("a", "Api", "Api", { instances: 2, rateLimit: 100 })]);
    const after = graph([node("a", "Api", "Api", { rateLimit: 100, instances: 2 })]);

    expect(moved(graphDelta(before, after))).toBe(0);
  });

  it("sees a component that left", () => {
    const before = graph([node("a", "Api", "Api"), node("b", "Orphan", "Worker")]);
    const after = graph([node("a", "Api", "Api")]);

    const delta = graphDelta(before, after);

    expect([...delta.removedNodes]).toEqual(["b"]);
    expect(drawn(delta).size).toBe(0);
  });
});

describe("graphDelta · over the six fixes the panel can apply", () => {
  const flowNode = (id: string, type: NodeType, name: string, x = 0): FlowNode<ArchNodeData> => ({
    id,
    type,
    position: { x, y: 0 },
    data: { name, configuration: {} },
  });

  const flowEdge = (id: string, source: string, target: string): FlowEdge<ConnectionData> => ({
    id,
    source,
    target,
    data: { synchronous: true, configuration: {} },
  });

  const comparable = (g: Graph): ComparableGraph => flowToUpdateRequest("x", 1, g.nodes, g.edges);

  const around = (before: Graph, fix: AppliedFix) => graphDelta(comparable(before), comparable(applyFix(before, fix)));

  it("lights the call a timeout was written onto, and nothing else", () => {
    const before: Graph = {
      nodes: [flowNode("a", "Api", "Api"), flowNode("b", "Database", "Db", 300)],
      edges: [flowEdge("e", "a", "b")],
    };

    const delta = around(before, {
      kind: "setEdgeResilience",
      element: { type: "Connection", id: "e" },
      label: { key: "x" },
      timeoutSeconds: 5,
    } as AppliedFix);

    expect([...delta.changedCalls]).toEqual(["e"]);
    expect(delta.addedNodes.size + delta.addedCalls.size + delta.removedNodes.size).toBe(0);
  });

  it("lights the cache and its new call, and the rewired one", () => {
    const before: Graph = {
      nodes: [flowNode("a", "Api", "Api"), flowNode("b", "Database", "Db", 300)],
      edges: [flowEdge("e", "a", "b")],
    };

    const delta = around(before, {
      kind: "insertCache",
      element: { type: "Node", id: "b" },
      label: { key: "x" },
    } as AppliedFix);

    expect(delta.addedNodes.size).toBe(1);
    expect(delta.addedCalls.size).toBe(1);
    expect([...delta.changedCalls]).toEqual(["e"]);
    expect(drawn(delta).size).toBe(3);
  });

  it("counts a removed component without asking the drawing to light it", () => {
    const before: Graph = {
      nodes: [flowNode("a", "Api", "Api"), flowNode("b", "Worker", "Orphan", 300)],
      edges: [],
    };

    const delta = around(before, {
      kind: "removeNode",
      element: { type: "Node", id: "b" },
      label: { key: "x" },
    } as AppliedFix);

    expect([...delta.removedNodes]).toEqual(["b"]);
    expect(moved(delta)).toBe(1);
    expect(drawn(delta).size).toBe(0);
  });

  it("lights the wire a connectTo drew", () => {
    const before: Graph = {
      nodes: [flowNode("a", "Api", "Api"), flowNode("o", "Observability", "Telemetry", 300)],
      edges: [],
    };

    const delta = around(before, {
      kind: "connectTo",
      element: { type: "Node", id: "a" },
      targetNodeId: "o",
      label: { key: "x" },
    } as AppliedFix);

    expect(delta.addedCalls.size).toBe(1);
    expect(delta.addedNodes.size).toBe(0);
  });

  it("lights the call an async fix turned around", () => {
    const before: Graph = {
      nodes: [flowNode("a", "Api", "Api"), flowNode("w", "Worker", "Worker", 300)],
      edges: [flowEdge("e", "a", "w")],
    };

    const delta = around(before, {
      kind: "makeEdgeAsynchronous",
      element: { type: "Connection", id: "e" },
      label: { key: "x" },
    } as AppliedFix);

    expect([...delta.changedCalls]).toEqual(["e"]);
  });

  it("lights the box a flag was written onto", () => {
    const before: Graph = {
      nodes: [flowNode("a", "Api", "Api")],
      edges: [],
    };

    const delta = around(before, {
      kind: "setNodeFlag",
      element: { type: "Node", id: "a" },
      configurationKey: "authentication",
      configurationValue: true,
      label: { key: "x" },
    } as AppliedFix);

    expect([...delta.changedNodes]).toEqual(["a"]);
  });
});
