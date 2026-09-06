import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { AnalysisResult, ArchitectureDto, FindingElement } from "../../../types/architecture";
import { callLookup, groupByProtagonist, protagonistOf } from "./protagonist";

const calls = callLookup([
  { id: "c-billing-db", sourceNodeId: "billing", targetNodeId: "db" },
  { id: "c-orders-db", sourceNodeId: "orders", targetNodeId: "db" },
  { id: "c-orders-pay", sourceNodeId: "orders", targetNodeId: "payments" },
  { id: "c-pay-inv", sourceNodeId: "payments", targetNodeId: "inventory" },
  { id: "c-inv-orders", sourceNodeId: "inventory", targetNodeId: "orders" },
]);

const node = (id: string): FindingElement => ({ type: "Node", id });
const call = (id: string): FindingElement => ({ type: "Connection", id });
const finding = (...elements: FindingElement[]) => ({ elements });

describe("who a finding is about", () => {
  it("is the node a finding names on its own", () => {
    expect(protagonistOf(finding(node("db")), calls)).toBe("db");
  });

  it("is the callee when the finding is one call, because that is what is being depended on", () => {
    expect(protagonistOf(finding(call("c-billing-db")), calls)).toBe("db");
  });

  it("is the component all its calls have in common", () => {
    const sharedDatabase = finding(node("db"), call("c-billing-db"), call("c-orders-db"));

    expect(protagonistOf(sharedDatabase, calls)).toBe("db");
  });

  it("is the caller when the calls all leave from it", () => {
    const highCoupling = finding(node("orders"), call("c-orders-db"), call("c-orders-pay"));

    expect(protagonistOf(highCoupling, calls)).toBe("orders");
  });

  it("is nobody for a cycle, which is a property of the loop and not of a component", () => {
    const cycle = finding(
      node("orders"), call("c-orders-pay"),
      node("payments"), call("c-pay-inv"),
      node("inventory"), call("c-inv-orders"),
    );

    expect(protagonistOf(cycle, calls)).toBeNull();
  });

  it("is nobody for a finding that names every component at once", () => {
    expect(protagonistOf(finding(node("orders"), node("db")), calls)).toBeNull();
  });

  it("is nobody for a finding with nothing to point at", () => {
    expect(protagonistOf(finding(), calls)).toBeNull();
  });

  it("ignores a call the graph has not caught up with rather than giving up", () => {
    expect(protagonistOf(finding(node("db"), call("c-unknown")), calls)).toBe("db");
  });
});

describe("grouping", () => {
  it("collects a component's findings where the first of them was", () => {
    const spof = finding(node("db"));
    const other = finding(node("orders"));
    const cache = finding(node("db"), call("c-orders-db"));

    const groups = groupByProtagonist([spof, other, cache], calls);

    expect(groups).toHaveLength(2);
    expect(groups[0].protagonist).toBe("db");
    expect(groups[0].findings).toEqual([spof, cache]);
    expect(groups[1].findings).toEqual([other]);
  });

  it("does not put a heading over a component with one finding", () => {
    const groups = groupByProtagonist([finding(node("db")), finding(node("orders"))], calls);

    expect(groups.map((group) => group.protagonist)).toEqual([null, null]);
  });

  it("leaves a finding about a shape on its own", () => {
    const cycle = finding(node("orders"), call("c-orders-pay"), node("payments"));
    const groups = groupByProtagonist([cycle], calls);

    expect(groups).toEqual([{ protagonist: null, findings: [cycle] }]);
  });

  it("keeps every finding exactly once", () => {
    const all = [
      finding(node("db")),
      finding(call("c-billing-db")),
      finding(node("orders")),
      finding(call("c-orders-pay")),
      finding(node("db"), call("c-orders-db")),
    ];

    const groups = groupByProtagonist(all, calls);

    expect(groups.flatMap((group) => group.findings)).toHaveLength(all.length);
    expect(new Set(groups.flatMap((group) => group.findings)).size).toBe(all.length);
  });
});

describe("against the recorded worst case, which is where the complaint came from", () => {
  const fixture = (name: string) =>
    JSON.parse(readFileSync(join(__dirname, "..", "..", "..", "export", "__fixtures__", name), "utf8"));

  const architecture = fixture("worst-case.architecture.json") as ArchitectureDto;
  const analysis = fixture("worst-case.analysis.json") as AnalysisResult;
  const lookup = callLookup(architecture.connections);
  const nameOf = new Map(architecture.nodes.map((node) => [node.id, node.name]));

  it("finds the database behind seven of the twenty-three findings", () => {
    const db = architecture.nodes.find((node) => node.name === "Db")!;
    const about = analysis.findings.filter((f) => protagonistOf(f, lookup) === db.id);

    expect(about.map((f) => f.code).sort()).toEqual([
      "MISSING_CACHE",
      "MISSING_RESILIENCE",
      "MISSING_RESILIENCE",
      "MISSING_RESILIENCE",
      "MISSING_RESILIENCE",
      "SHARED_DATABASE",
      "SPOF",
    ]);
  });

  it("leaves the findings about a shape without a protagonist", () => {
    const shapes = analysis.findings.filter((f) => ["CYCLE", "SYNC_CHAIN", "RETRY_AMPLIFICATION"].includes(f.code));

    expect(shapes).not.toHaveLength(0);
    for (const shape of shapes) expect(protagonistOf(shape, lookup)).toBeNull();
  });

  it("turns twenty-three cards into a shorter list without losing one", () => {
    const groups = groupByProtagonist(analysis.findings, lookup);

    expect(groups.flatMap((group) => group.findings)).toHaveLength(analysis.findings.length);
    expect(groups.length).toBeLessThan(analysis.findings.length);

    const named = groups
      .filter((group) => group.protagonist !== null)
      .map((group) => `${nameOf.get(group.protagonist!)}: ${group.findings.length}`);

    expect(named).toContain("Db: 7");
  });
});
