import { describe, expect, it } from "vitest";
import { zoneFault, type ScenarioFault } from "../../../domain/scenarioUrl";
import type { FragilityRow } from "../../../types/architecture";
import { DEFAULT_LATENCY_MS, addFault, faultOn, kindsFor, loadedRow, pruneFaults, removeFault, rowScenario, setLatency, toggleZone } from "./scenario";

const API = "a0000000-0000-0000-0000-000000000002";
const CALL = "c0000000-0000-0000-0000-000000000001";

describe("kindsFor", () => {
  it("offers the three verbs to a node and only two to a call", () => {
    expect(kindsFor("Node")).toEqual(["down", "degraded", "latency"]);
    expect(kindsFor("Connection")).toEqual(["down", "latency"]);
  });
});

describe("addFault and faultOn", () => {
  it("adds a latency with its default value and finds it back", () => {
    const faults = addFault([], "Node", API, "latency");

    expect(faults).toEqual([{ target: "Node", id: API, kind: "latency", value: DEFAULT_LATENCY_MS }]);
    expect(faultOn(faults, "Node", API, "latency")?.value).toBe(DEFAULT_LATENCY_MS);
    expect(faultOn(faults, "Node", API, "down")).toBeNull();
  });

  it("does not add the same fault twice", () => {
    const once = addFault([], "Connection", CALL, "down");

    expect(addFault(once, "Connection", CALL, "down")).toHaveLength(1);
  });

  it("never mistakes a zone for an element", () => {
    expect(faultOn([zoneFault("a")], "Node", "a", "down")).toBeNull();
  });
});

describe("removeFault, setLatency and toggleZone", () => {
  const faults: ScenarioFault[] = [
    { target: "Node", id: API, kind: "down" },
    { target: "Node", id: API, kind: "latency", value: 2000 },
    zoneFault("b"),
  ];

  it("removes exactly the fault asked for", () => {
    expect(removeFault(faults, { target: "Node", id: API, kind: "down" })).toEqual(faults.slice(1));
  });

  it("rewrites the value of a latency and clamps it to a positive whole number", () => {
    const next = setLatency(faults, faults[1], 0.4);

    expect(next[1]).toEqual({ target: "Node", id: API, kind: "latency", value: 1 });
    expect(next[0]).toBe(faults[0]);
  });

  it("takes a zone down and back up", () => {
    expect(toggleZone(faults, "b")).toEqual(faults.slice(0, 2));
    expect(toggleZone(faults, "c")).toEqual([...faults, zoneFault("c")]);
  });
});

describe("pruneFaults", () => {
  it("drops the faults whose element is gone and keeps the zones", () => {
    const faults: ScenarioFault[] = [{ target: "Node", id: API, kind: "down" }, { target: "Connection", id: CALL, kind: "latency", value: 500 }, zoneFault("a")];

    expect(pruneFaults(faults, new Set([API]))).toEqual({ kept: [faults[0], faults[2]], dropped: 1 });
  });

  it("drops nothing when everything is still drawn", () => {
    const faults: ScenarioFault[] = [{ target: "Node", id: API, kind: "down" }];

    expect(pruneFaults(faults, new Set([API, CALL])).dropped).toBe(0);
  });
});

describe("the fragility map and the scenario", () => {
  const row = { nodeId: API } as FragilityRow;

  it("says a row is loaded only when it is the whole scenario", () => {
    expect(loadedRow([{ target: "Node", id: API, kind: "down" }], row)).toBe(true);
    expect(loadedRow([{ target: "Node", id: API, kind: "degraded" }], row)).toBe(false);
    expect(loadedRow([{ target: "Node", id: API, kind: "down" }, { target: "Connection", id: CALL, kind: "down" }], row)).toBe(false);
  });

  it("loads a row as the single fault that produced it", () => {
    expect(rowScenario(row)).toEqual([{ target: "Node", id: API, kind: "down" }]);
  });
});
