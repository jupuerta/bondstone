import { describe, expect, it } from "vitest";
import type { Edge, Node } from "@xyflow/react";
import { applyFix } from "./applyFix";
import type { Graph } from "./graphHistory";
import type { AppliedFix } from "../../../types/architecture";
import type { ArchNodeData, ConnectionData } from "./types";

const node = (id: string, type: string): Node<ArchNodeData> => ({
  id,
  type,
  position: { x: 0, y: 0 },
  data: { name: id, configuration: {} },
});

const edge = (id: string, source: string, target: string, data: ConnectionData): Edge<ConnectionData> => ({
  id,
  source,
  target,
  data,
});

const fix = (kind: string, elementId: string): AppliedFix => ({
  kind: kind as AppliedFix["kind"],
  element: { type: "Connection", id: elementId },
  label: { key: "fix.MISSING_RESILIENCE.makeAsynchronous" },
});

const graph: Graph = {
  nodes: [node("api", "Api"), node("obs", "Observability"), node("db", "Database")],
  edges: [
    edge("send", "api", "obs", { synchronous: true, configuration: { retries: 2 } }),
    edge("query", "api", "db", { synchronous: true, configuration: { timeoutSeconds: 3 } }),
  ],
};

describe("makeEdgeAsynchronous", () => {
  it("flips the named edge to asynchronous and touches nothing else on it", () => {
    const after = applyFix(graph, fix("makeEdgeAsynchronous", "send"));

    const send = after.edges.find((e) => e.id === "send")!;
    expect(send.data?.synchronous).toBe(false);
    expect(send.data?.configuration).toEqual({ retries: 2 });
  });

  it("leaves every other edge, and the nodes, exactly as they were", () => {
    const after = applyFix(graph, fix("makeEdgeAsynchronous", "send"));

    expect(after.edges.find((e) => e.id === "query")).toEqual(graph.edges[1]);
    expect(after.nodes).toEqual(graph.nodes);
  });

  it("does nothing on an edge that is not there", () => {
    expect(applyFix(graph, fix("makeEdgeAsynchronous", "missing"))).toEqual(graph);
  });
});

describe("insertCache", () => {
  const insert: AppliedFix = {
    kind: "insertCache",
    element: { type: "Node", id: "db" },
    label: { key: "fix.MISSING_CACHE.insert" },
  };

  it("lets the rewired callers fall through to the database, so the cache is not a new hard dependency", () => {
    const after = applyFix(graph, insert);

    const query = after.edges.find((e) => e.id === "query")!;

    expect(query.data?.configuration).toEqual({ timeoutSeconds: 3, fallback: true });
  });

  it("keeps the cache talking to the database itself", () => {
    const after = applyFix(graph, insert);

    const cache = after.nodes.find((n) => n.type === "Cache")!;
    const toDatabase = after.edges.find((e) => e.source === cache.id)!;

    expect(toDatabase.target).toBe("db");
    expect(toDatabase.data).toEqual({ synchronous: true, configuration: { timeoutSeconds: 5, access: "read" } });
  });

  const table: Graph = {
    nodes: [node("orders", "Service"), node("invoicing", "Service"), node("reports", "Service"), node("db", "Database")],
    edges: [
      edge("save", "orders", "db", { synchronous: true, configuration: { timeoutSeconds: 3, access: "write" } }),
      edge("read", "orders", "db", { synchronous: true, configuration: { timeoutSeconds: 3, access: "read" } }),
      edge("both", "invoicing", "db", { synchronous: true, configuration: { timeoutSeconds: 3, access: "both" } }),
      edge("query", "reports", "db", { synchronous: true, configuration: { timeoutSeconds: 3 } }),
    ],
  };

  it("reroutes only the reads, declared or undeclared, and leaves every write on the database", () => {
    const after = applyFix(table, insert);
    const cache = after.nodes.find((n) => n.type === "Cache")!;
    const targetOf = (id: string) => after.edges.find((e) => e.id === id)!.target;

    expect(targetOf("read")).toBe(cache.id);
    expect(targetOf("query")).toBe(cache.id);
    expect(targetOf("save")).toBe("db");
    expect(targetOf("both")).toBe("db");
    expect(after.edges.find((e) => e.id === "save")!.data?.configuration).toEqual({ timeoutSeconds: 3, access: "write" });
  });

  it("does nothing when every call writes, because there is nothing a cache could answer", () => {
    const writers: Graph = { ...table, edges: table.edges.filter((e) => e.id === "save" || e.id === "both") };

    expect(applyFix(writers, insert)).toBe(writers);
  });
});

describe("insertQueue", () => {
  const handoff: Graph = {
    nodes: [
      { ...node("orders", "Service"), position: { x: 0, y: 100 } },
      { ...node("mailer", "Service"), position: { x: 400, y: 100 } },
      node("db", "Database"),
    ],
    edges: [
      edge("notify", "orders", "mailer", { synchronous: false, configuration: {} }),
      edge("save", "orders", "db", { synchronous: true, configuration: { timeoutSeconds: 3 } }),
    ],
  };

  const insert: AppliedFix = {
    kind: "insertQueue",
    element: { type: "Connection", id: "notify" },
    label: { key: "fix.ASYNC_NO_QUEUE.insert" },
  };

  it("puts a queue halfway between the two, with its dead letter queue on so it leaves no warning behind", () => {
    const after = applyFix(handoff, insert);

    const queue = after.nodes.find((n) => n.type === "Queue")!;
    expect(queue.data.name).toBe("mailer queue");
    expect(queue.data.configuration).toEqual({ deadLetterQueue: true });
    expect(queue.position).toEqual({ x: 200, y: 100 });
  });

  it("turns the call into a publish and adds the delivery, neither of them waiting", () => {
    const after = applyFix(handoff, insert);

    const queue = after.nodes.find((n) => n.type === "Queue")!;
    const publish = after.edges.find((e) => e.id === "notify")!;
    const deliver = after.edges.find((e) => e.source === queue.id)!;

    expect(publish.source).toBe("orders");
    expect(publish.target).toBe(queue.id);
    expect(publish.data).toEqual({ synchronous: false, configuration: {} });

    expect(deliver.target).toBe("mailer");
    expect(deliver.data).toEqual({ synchronous: false, configuration: {} });
  });

  it("leaves every other edge, and the nodes it did not add, exactly as they were", () => {
    const after = applyFix(handoff, insert);

    expect(after.edges.find((e) => e.id === "save")).toEqual(handoff.edges[1]);
    expect(after.nodes.slice(0, 3)).toEqual(handoff.nodes);
  });

  it("does nothing on a call that is not there", () => {
    expect(applyFix(handoff, { ...insert, element: { type: "Connection", id: "missing" } })).toBe(handoff);
  });
});

describe("an unknown kind", () => {
  it("returns the graph untouched, so a stale client applies nothing it does not understand", () => {
    expect(applyFix(graph, fix("somethingFromTheFuture", "send"))).toBe(graph);
  });
});
