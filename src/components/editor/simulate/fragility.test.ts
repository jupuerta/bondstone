import { describe, expect, it } from "vitest";
import type { FindingElement, FragilityMap, FragilityRow } from "../../../types/architecture";
import { containedBar, fragilityBars, fragilityTint, worstBar } from "./fragility";

function row(overrides: Partial<FragilityRow> & { nodeId: string }): FragilityRow {
  return {
    componentsDown: 1,
    componentsDegraded: 0,
    blastRadius: 0,
    entryPointsAffected: 0,
    requestsAffected: 0,
    faultedLatencyMs: null,
    down: [],
    degraded: [],
    ...overrides,
  };
}

function node(id: string): FindingElement {
  return { type: "Node", id };
}

function edge(id: string): FindingElement {
  return { type: "Connection", id };
}

const RESILIENT_ASYNC: FragilityMap = {
  componentsTotal: 7,
  entryPointsTotal: 1,
  nominalLatencyMs: 39,
  rows: [
    row({ nodeId: "LB", componentsDown: 5, componentsDegraded: 2, blastRadius: 1, entryPointsAffected: 1, requestsAffected: 1, faultedLatencyMs: null }),
    row({ nodeId: "Queue", componentsDown: 1, componentsDegraded: 3, blastRadius: 0.5714, faultedLatencyMs: 39 }),
    row({ nodeId: "Db", componentsDown: 1, componentsDegraded: 2, blastRadius: 0.4286, faultedLatencyMs: 17 }),
    row({ nodeId: "Cache", componentsDown: 1, componentsDegraded: 2, blastRadius: 0.4286, faultedLatencyMs: 1011 }),
    row({ nodeId: "Api1", componentsDown: 1, componentsDegraded: 1, blastRadius: 0.2857, entryPointsAffected: 1, requestsAffected: 1, faultedLatencyMs: 39 }),
  ],
};

describe("the bar of a row", () => {
  it("splits the blast radius into what fell and what is limping", () => {
    const bars = fragilityBars(RESILIENT_ASYNC);
    const queue = bars[1];

    expect(queue.downShare).toBeCloseTo(1 / 7, 6);
    expect(queue.degradedShare).toBeCloseTo(3 / 7, 6);
    expect(queue.downShare + queue.degradedShare).toBeCloseTo(queue.row.blastRadius, 4);
  });

  it("draws nothing at all for a map with no components", () => {
    expect(fragilityBars({ componentsTotal: 0, entryPointsTotal: 0, nominalLatencyMs: 0, rows: [] })).toEqual([]);
    expect(fragilityBars(null)).toEqual([]);
  });
});

describe("the latency of a row", () => {
  it("says there is no route left when the whole architecture stops answering", () => {
    expect(fragilityBars(RESILIENT_ASYNC)[0].latency).toEqual({ kind: "noRoute" });
  });

  it("carries both figures when breaking it makes the drawing slower", () => {
    expect(fragilityBars(RESILIENT_ASYNC)[3].latency).toEqual({ kind: "slower", from: 39, to: 1011 });
  });

  it("says nothing when the fault does not cost a millisecond, nor when failing fast is faster", () => {
    expect(fragilityBars(RESILIENT_ASYNC)[1].latency).toBeNull();
    expect(fragilityBars(RESILIENT_ASYNC)[2].latency).toBeNull();
  });
});

describe("the lesson the short bars carry", () => {
  it("is the widest row that costs no request and no entry point", () => {
    const contained = containedBar(fragilityBars(RESILIENT_ASYNC));

    expect(contained?.row.nodeId).toBe("Queue");
    expect(contained?.row.blastRadius).toBe(0.5714);
    expect(contained?.row.requestsAffected).toBe(0);
  });

  it("is nobody when every component takes requests down with it", () => {
    const everyoneHurts: FragilityMap = {
      componentsTotal: 5,
      entryPointsTotal: 1,
      nominalLatencyMs: 57,
      rows: [
        row({ nodeId: "Db", componentsDown: 5, blastRadius: 1, entryPointsAffected: 1, requestsAffected: 1, faultedLatencyMs: 5005 }),
        row({ nodeId: "Gateway", componentsDown: 5, blastRadius: 1, entryPointsAffected: 1, requestsAffected: 1 }),
      ],
    };

    expect(containedBar(fragilityBars(everyoneHurts))).toBeNull();
    expect(worstBar(fragilityBars(everyoneHurts))?.row.nodeId).toBe("Db");
  });

  it("is not the component that falls alone without dragging anything down", () => {
    const standsAlone = fragilityBars({
      componentsTotal: 4,
      entryPointsTotal: 1,
      nominalLatencyMs: 10,
      rows: [row({ nodeId: "Observability", componentsDown: 0, blastRadius: 0 })],
    });

    expect(containedBar(standsAlone)).toBeNull();
  });
});

describe("the tint the canvas gets while the mouse is on a row", () => {
  it("paints the nodes and the arrows the row carries, down over degraded", () => {
    const tinted = fragilityTint(
      row({
        nodeId: "Cache",
        down: [node("Cache"), edge("Cache→Db")],
        degraded: [node("Api1"), node("Api2"), edge("Api1→Cache")],
      }),
      "Cache",
    );

    expect(tinted.size).toBe(5);
    expect(tinted.get("Cache")?.state).toBe("Down");
    expect(tinted.get("Cache→Db")?.state).toBe("Down");
    expect(tinted.get("Api1")?.state).toBe("Degraded");
    expect(tinted.get("Api1→Cache")?.type).toBe("Connection");
  });

  it("blames the component the row is about, and blames nobody for the component itself", () => {
    const tinted = fragilityTint(row({ nodeId: "Cache", down: [node("Cache"), node("Orders")] }), "Cache");

    expect(tinted.get("Cache")?.causedBy).toBeNull();
    expect(tinted.get("Orders")?.causedBy).toEqual({ type: "Node", id: "Cache" });
    expect(tinted.get("Orders")?.reason).toEqual({ key: "ui.fragility.tint", args: { name: "Cache" } });
  });
});
