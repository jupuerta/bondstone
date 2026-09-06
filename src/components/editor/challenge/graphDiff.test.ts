import { describe, expect, it } from "vitest";
import { compareGraphs, type ComparableGraph } from "./graphDiff";
import type { ConnectionDto, NodeConfiguration, NodeDto, NodeType, ResilienceConfiguration } from "../../../types/architecture";

let ids = 0;
const id = () => `n${(ids += 1)}`;

function node(name: string, type: NodeType = "Service", configuration?: NodeConfiguration): NodeDto {
  return { id: id(), type, name, position: { x: 0, y: 0 }, configuration };
}

function call(
  from: NodeDto,
  to: NodeDto,
  options: { synchronous?: boolean; protocol?: ConnectionDto["protocol"]; configuration?: ResilienceConfiguration } = {},
): ConnectionDto {
  return {
    id: id(),
    sourceNodeId: from.id,
    targetNodeId: to.id,
    protocol: options.protocol ?? "Http",
    synchronous: options.synchronous ?? true,
    configuration: options.configuration,
  };
}

const graph = (nodes: NodeDto[], connections: ConnectionDto[] = []): ComparableGraph => ({ nodes, connections });

const said = (yours: ComparableGraph, theirs: ComparableGraph) =>
  compareGraphs(yours, theirs).map((difference) => `${difference.kind}:${difference.text.key}`);

describe("compareGraphs · what it says", () => {
  it("says nothing about two drawings that are the same graph", () => {
    const mineApi = node("Storefront", "Api");
    const mineDb = node("Db", "Database");
    const theirApi = node("Storefront", "Api");
    const theirDb = node("Db", "Database");

    const differences = compareGraphs(
      graph([mineApi, mineDb], [call(mineApi, mineDb, { protocol: "Sql" })]),
      graph([theirApi, theirDb], [call(theirApi, theirDb, { protocol: "Sql" })]),
    );

    expect(differences).toEqual([]);
  });

  it("collapses a cache put between two boxes you already had into one sentence", () => {
    const mineApi = node("Storefront", "Api");
    const mineDb = node("Db", "Database");

    const theirApi = node("Storefront", "Api");
    const theirCache = node("Cache", "Cache");
    const theirDb = node("Db", "Database");

    const differences = compareGraphs(
      graph([mineApi, mineDb], [call(mineApi, mineDb, { protocol: "Sql" })]),
      graph([theirApi, theirCache, theirDb], [call(theirApi, theirCache), call(theirCache, theirDb, { protocol: "Sql" })]),
    );

    expect(differences).toHaveLength(1);
    expect(differences[0].kind).toBe("componentInterposed");
    expect(differences[0].text).toEqual({
      key: "ui.compare.diff.componentInterposed",
      args: { name: "Cache", type: { key: "ui.node.Cache" }, from: "Storefront", to: "Db" },
    });
  });

  it("reads a call the reference stopped waiting for as one difference, not two", () => {
    const mineOrders = node("Orders");
    const mineMailer = node("Mailer");
    const theirOrders = node("Orders");
    const theirMailer = node("Mailer");

    const differences = compareGraphs(
      graph([mineOrders, mineMailer], [call(mineOrders, mineMailer, { synchronous: true, protocol: "Http" })]),
      graph([theirOrders, theirMailer], [call(theirOrders, theirMailer, { synchronous: false, protocol: "Amqp" })]),
    );

    expect(differences.map((difference) => difference.kind)).toEqual(["callAsync"]);
    expect(differences[0].text.args).toEqual({ from: "Orders", to: "Mailer" });
  });

  it("reports the resilience the reference put on a call you both have", () => {
    const mineA = node("Checkout");
    const mineB = node("Payments", "ExternalService");
    const theirA = node("Checkout");
    const theirB = node("Payments", "ExternalService");

    const differences = compareGraphs(
      graph([mineA, mineB], [call(mineA, mineB, { configuration: { timeoutSeconds: 5 } })]),
      graph(
        [theirA, theirB],
        [call(theirA, theirB, { configuration: { timeoutSeconds: 1, circuitBreaker: true, fallback: true } })],
      ),
    );

    expect(differences.map((difference) => difference.kind)).toEqual(["callSetting", "callSetting", "callSetting"]);
    expect(differences[0].text.args).toMatchObject({
      setting: { key: "ui.inspector.timeoutSeconds" },
      yours: { key: "ui.compare.value", args: { value: 5 } },
      theirs: { key: "ui.compare.value", args: { value: 1 } },
    });
    expect(differences[1].text.args).toMatchObject({
      setting: { key: "ui.inspector.circuitBreaker" },
      yours: { key: "ui.config.tristate.no" },
      theirs: { key: "ui.config.tristate.yes" },
    });
  });

  it("treats an unticked box and an absent one as the same off", () => {
    const mineA = node("A");
    const mineB = node("B");
    const theirA = node("A");
    const theirB = node("B");

    expect(
      said(
        graph([mineA, mineB], [call(mineA, mineB, { configuration: { fallback: false, circuitBreaker: false } })]),
        graph([theirA, theirB], [call(theirA, theirB, { configuration: {} })]),
      ),
    ).toEqual([]);
  });

  it("tells a number that was never declared from one that was set to zero", () => {
    const mineA = node("A");
    const mineB = node("B");
    const theirA = node("A");
    const theirB = node("B");

    const differences = compareGraphs(
      graph([mineA, mineB], [call(mineA, mineB, { configuration: { retries: 0 } })]),
      graph([theirA, theirB], [call(theirA, theirB, { configuration: {} })]),
    );

    expect(differences).toHaveLength(1);
    expect(differences[0].text.args).toMatchObject({
      yours: { key: "ui.compare.value", args: { value: 0 } },
      theirs: { key: "ui.config.tristate.undeclared" },
    });
  });

  it("reports a component setting the reference declared and you did not", () => {
    const mine = node("Db", "Database");
    const theirs = node("Db", "Database", { readReplicas: 3 });

    const differences = compareGraphs(graph([mine]), graph([theirs]));

    expect(differences).toHaveLength(1);
    expect(differences[0].kind).toBe("componentSetting");
    expect(differences[0].text.args).toEqual({
      name: "Db",
      setting: { key: "ui.config.readReplicas" },
      yours: { key: "ui.config.tristate.undeclared" },
      theirs: { key: "ui.compare.value", args: { value: 3 } },
    });
  });

  it("says nothing about a stored field the inspector does not offer", () => {
    const mine = node("A", "Service", { bulkhead: true });
    const theirs = node("A", "Service", {});

    expect(said(graph([mine]), graph([theirs]))).toEqual([]);
  });

  it("says which type the reference chose for a box you both drew", () => {
    const mine = node("Shipping", "Service");
    const theirs = node("Shipping", "Worker");

    const differences = compareGraphs(graph([mine]), graph([theirs]));

    expect(differences.map((difference) => difference.kind)).toEqual(["componentRetyped"]);
    expect(differences[0].text.args).toEqual({
      name: "Shipping",
      type: { key: "ui.node.Worker" },
      yourType: { key: "ui.node.Service" },
    });
  });
});

describe("compareGraphs · the pairing", () => {
  it("matches by name however it was typed", () => {
    const mine = node(" storefront ", "Api");
    const theirs = node("Storefront", "Api");

    expect(said(graph([mine]), graph([theirs]))).toEqual([]);
  });

  it("pairs a renamed box by type when there is exactly one candidate, and says which rule it applied", () => {
    const mineApi = node("Storefront", "Api");
    const mineDb = node("Base de datos", "Database");
    const theirApi = node("Storefront", "Api");
    const theirDb = node("Db", "Database");

    const differences = compareGraphs(
      graph([mineApi, mineDb], [call(mineApi, mineDb, { protocol: "Sql" })]),
      graph([theirApi, theirDb], [call(theirApi, theirDb, { protocol: "Sql" })]),
    );

    expect(differences.map((difference) => difference.kind)).toEqual(["componentPairedByType"]);
    expect(differences[0].text.args).toEqual({
      yourName: "Base de datos",
      name: "Db",
      type: { key: "ui.node.Database" },
    });
  });

  it("refuses to guess when a type has two homeless candidates", () => {
    const mineA = node("Alpha");
    const mineB = node("Beta");
    const theirA = node("Uno");
    const theirB = node("Dos");

    expect(said(graph([mineA, mineB]), graph([theirA, theirB]))).toEqual([
      "componentAdded:ui.compare.diff.componentAdded",
      "componentAdded:ui.compare.diff.componentAdded",
      "componentRemoved:ui.compare.diff.componentRemoved",
      "componentRemoved:ui.compare.diff.componentRemoved",
    ]);
  });

  it("does not pair on a name that is not unique on both sides", () => {
    const mineOne = node("Worker", "Worker");
    const mineTwo = node("Worker", "Worker");
    const theirs = node("Worker", "Worker");

    expect(said(graph([mineOne, mineTwo]), graph([theirs]))).toEqual([
      "componentAdded:ui.compare.diff.componentAdded",
      "componentRemoved:ui.compare.diff.componentRemoved",
      "componentRemoved:ui.compare.diff.componentRemoved",
    ]);
  });

  it("says nothing about the calls of a component it could not pair", () => {
    const mineApi = node("Storefront", "Api");
    const theirApi = node("Storefront", "Api");
    const theirCache = node("Cache", "Cache");
    const theirDb = node("Db", "Database");

    expect(
      said(
        graph([mineApi]),
        graph([theirApi, theirCache, theirDb], [call(theirApi, theirCache), call(theirCache, theirDb)]),
      ),
    ).toEqual([
      "componentAdded:ui.compare.diff.componentAdded",
      "componentAdded:ui.compare.diff.componentAdded",
    ]);
  });

  it("drops both of two calls between the same pair rather than picking one", () => {
    const mineA = node("A");
    const mineB = node("B");
    const theirA = node("A");
    const theirB = node("B");

    expect(
      said(
        graph([mineA, mineB], [call(mineA, mineB), call(mineA, mineB, { protocol: "Sql" })]),
        graph([theirA, theirB], [call(theirA, theirB, { synchronous: false })]),
      ),
    ).toEqual([]);
  });

  it("reports a call each way round when only one of them exists", () => {
    const mineA = node("A");
    const mineB = node("B");
    const theirA = node("A");
    const theirB = node("B");

    expect(
      said(graph([mineA, mineB], [call(mineB, mineA)]), graph([theirA, theirB], [call(theirA, theirB)])),
    ).toEqual([
      "callAdded:ui.compare.diff.callAdded",
      "callRemoved:ui.compare.diff.callRemoved",
    ]);
  });

  it("names your box by your name and theirs by theirs", () => {
    const mineA = node("Tienda", "Api");
    const mineB = node("Db", "Database");
    const theirA = node("Storefront", "Api");
    const theirB = node("Db", "Database");

    const differences = compareGraphs(
      graph([mineA, mineB], [call(mineA, mineB, { protocol: "Sql" })]),
      graph([theirA, theirB]),
    );

    const removed = differences.find((difference) => difference.kind === "callRemoved");
    expect(removed?.text.args).toEqual({ from: "Tienda", to: "Db" });
  });

  it("survives an empty attempt", () => {
    const theirs = node("Storefront", "Api");

    expect(said(graph([]), graph([theirs]))).toEqual(["componentAdded:ui.compare.diff.componentAdded"]);
  });
});

describe("compareGraphs · what it leaves out", () => {
  it("does not count a call the reference stopped waiting for four times over", () => {
    const mineA = node("Orders");
    const mineB = node("Mailer");
    const theirA = node("Orders");
    const theirB = node("Mailer");

    expect(
      said(
        graph([mineA, mineB], [call(mineA, mineB, { configuration: { timeoutSeconds: 5 } })]),
        graph([theirA, theirB], [call(theirA, theirB, { synchronous: false, configuration: {} })]),
      ),
    ).toEqual(["callAsync:ui.compare.diff.callAsync"]);
  });
});
