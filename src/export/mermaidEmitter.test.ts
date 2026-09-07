import { describe, expect, it } from "vitest";
import { emitMermaid } from "./mermaidEmitter";
import type { ConnectionDto, NodeDto, NodeType, ProtocolType } from "../types/architecture";

let counter = 0;

function node(name: string, type: NodeType, id?: string): NodeDto {
  return { id: id ?? `a0000000-0000-0000-0000-${String(++counter).padStart(12, "0")}`, type, name, position: { x: 0, y: 0 } };
}

function edge(source: NodeDto, target: NodeDto, protocol: ProtocolType = "Http", synchronous = true): ConnectionDto {
  return {
    id: `c0000000-0000-0000-0000-${String(++counter).padStart(12, "0")}`,
    sourceNodeId: source.id,
    targetNodeId: target.id,
    protocol,
    synchronous,
  };
}

describe("the mermaid block", () => {
  it("draws a synchronous edge as a solid arrow", () => {
    const api = node("Api", "Api");
    const db = node("Db", "Database");

    expect(emitMermaid([api, db], [edge(api, db, "Sql")])).toContain("Api -->|Sql| Db");
  });

  it("draws an asynchronous edge as a dotted arrow", () => {
    const api = node("Api", "Api");
    const queue = node("Events", "Queue");

    expect(emitMermaid([api, queue], [edge(api, queue, "Amqp", false)])).toContain("Api -.->|Amqp| Events");
  });

  it("carries the type in the node label", () => {
    expect(emitMermaid([node("Payments", "Service")], [])).toContain('Payments["Service: Payments"]');
  });

  it("gives two nodes with the same name distinct identifiers", () => {
    const first = node("Api", "Api", "aaaaaaaa-0000-0000-0000-000000000001");
    const second = node("Api", "Api", "aaaaaaaa-0000-0000-0000-000000000002");

    const diagram = emitMermaid([first, second], []);

    expect(diagram).toContain('Api["Api: Api"]');
    expect(diagram).toContain('Api_2["Api: Api"]');
  });

  it("assigns the collision suffix by the order it is given", () => {
    const first = node("Api", "Api", "aaaaaaaa-0000-0000-0000-000000000001");
    const second = node("Api", "Api", "aaaaaaaa-0000-0000-0000-000000000002");
    const between = edge(first, second);

    expect(emitMermaid([first, second], [between])).toContain("Api -->|Http| Api_2");
    expect(emitMermaid([second, first], [between])).toContain("Api_2 -->|Http| Api");
  });

  it("survives a name that would break the block", () => {
    const diagram = emitMermaid([node("Orders | Payments [v2]", "Service")], []);

    expect(diagram).toContain('OrdersPaymentsv2["Service: Orders / Payments (v2)"]');
    expect(diagram.slice(diagram.indexOf("["))).not.toContain("|");
  });

  it("drops an edge pointing at a node that is not there, without taking the diagram with it", () => {
    const api = node("Api", "Api");
    const ghost = node("Ghost", "Service");

    const diagram = emitMermaid([api], [edge(api, ghost)]);

    expect(diagram).toContain('Api["Api: Api"]');
    expect(diagram).not.toContain("-->");
  });

  it("carries not one line of styling without a simulation", () => {
    expect(emitMermaid([node("Api", "Api")], [])).not.toContain("classDef");
  });

  it("tints only what is not healthy", () => {
    const api = node("Api", "Api");
    const db = node("Db", "Database");

    const diagram = emitMermaid([api, db], [], new Map([[api.id, "Healthy"], [db.id, "Down"]] as const));

    expect(diagram).toContain("classDef down");
    expect(diagram).not.toContain("classDef degraded");
    expect(diagram).toContain(`class Db down`);
    expect(diagram).not.toContain("class Api");
  });
});
