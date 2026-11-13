import { describe, expect, it } from "vitest";
import type { ConnectionDto, NodeConfiguration, NodeDto, NodeType } from "../../../types/architecture";
import { declaredTrafficFields, forgetDeclaredTraffic, isEntryPoint } from "./entryPoint";

function box(id: string, type: NodeType, configuration: NodeConfiguration = {}): NodeDto {
  return { id, type, name: id, position: { x: 0, y: 0 }, configuration };
}

function call(source: string, target: string): ConnectionDto {
  return { id: `${source}->${target}`, sourceNodeId: source, targetNodeId: target, protocol: "Http", synchronous: true };
}

const edge = box("edge", "Gateway", { requestsPerSecond: 100 });
const api = box("api", "Api", { requestsPerSecond: 400, instances: 2 });
const db = box("db", "Database");
const obs = box("obs", "Observability");
const users = box("users", "Client", { requestsPerSecond: 300 });

describe("what the inspector calls an entry point, read the way the engine reads it", () => {
  it("is a box nobody calls", () => {
    expect(isEntryPoint("api", [api, db], [call("api", "db")])).toBe(true);
    expect(isEntryPoint("api", [edge, api, db], [call("edge", "api"), call("api", "db")])).toBe(false);
  });

  it("stays an entry point when only the telemetry consults it", () => {
    expect(isEntryPoint("api", [api, db, obs], [call("obs", "api"), call("api", "db")])).toBe(true);
    expect(isEntryPoint("api", [api, obs], [call("api", "obs")])).toBe(true);
  });
});

describe("the number a box declared while it was an entry point", () => {
  it("is cleared when a call reaches it", () => {
    const after = forgetDeclaredTraffic([edge, api, db], "edge", "api");

    expect(after.find((node) => node.id === "api")?.configuration).toEqual({ instances: 2 });
    expect(after.find((node) => node.id === "edge")?.configuration).toEqual({ requestsPerSecond: 100 });
  });

  it("survives a scrape from the observability", () => {
    const nodes = [api, obs];
    expect(forgetDeclaredTraffic(nodes, "obs", "api")).toBe(nodes);
  });

  it("is never taken from a client, which is the entrance whoever calls it back", () => {
    const nodes = [api, users];
    expect(forgetDeclaredTraffic(nodes, "api", "users")).toBe(nodes);
  });

  it("leaves the array alone when there is nothing to clear, so nothing re-renders", () => {
    const nodes = [edge, db];
    expect(forgetDeclaredTraffic(nodes, "edge", "db")).toBe(nodes);
    expect(forgetDeclaredTraffic(nodes, "edge", "nobody")).toBe(nodes);
  });

  it("is the field the inspector only shows on entry points, and only that one", () => {
    expect(declaredTrafficFields("Api")).toEqual(["requestsPerSecond"]);
    expect(declaredTrafficFields("Gateway")).toEqual(["requestsPerSecond"]);
    expect(declaredTrafficFields("LoadBalancer")).toEqual(["requestsPerSecond"]);
    expect(declaredTrafficFields("Client")).toEqual([]);
    expect(declaredTrafficFields("Database")).toEqual([]);
  });
});
