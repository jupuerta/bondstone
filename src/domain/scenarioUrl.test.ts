import { describe, expect, it } from "vitest";
import type { Fault } from "../types/architecture";
import {
  canonical,
  downZones,
  faultToParam,
  isZoneFault,
  nodeFaults,
  parseFaultParam,
  scenarioFromSearch,
  scenarioKey,
  sameFault,
  searchWithScenario,
  zoneFault,
} from "./scenarioUrl";

const API = "a0000000-0000-0000-0000-000000000002";
const EDGE = "c0000000-0000-0000-0000-000000000001";

describe("links that already exist", () => {
  it("still reads a node fault written the old way", () => {
    expect(parseFaultParam(`node:${API.replace(/-/g, "")}:down`)).toEqual({
      target: "Node",
      id: API,
      kind: "down",
    });
  });

  it("still reads a connection fault and a latency with its value", () => {
    expect(parseFaultParam(`connection:${EDGE.replace(/-/g, "")}:latency:8000`)).toEqual({
      target: "Connection",
      id: EDGE,
      kind: "latency",
      value: 8000,
    });
  });

  it("survives a whole round trip of a link written before zones existed", () => {
    const search = `?fault=node:${API.replace(/-/g, "")}:down&fault=connection:${EDGE.replace(/-/g, "")}:latency:8000`;
    const parsed = scenarioFromSearch(search);

    expect(parsed).toHaveLength(2);
    expect(scenarioFromSearch(searchWithScenario(search, parsed))).toEqual(canonical(parsed));
  });
});

describe("the zone in the link", () => {
  it("writes itself as zone:letter:down", () => {
    expect(faultToParam(zoneFault("a"))).toBe("zone:a:down");
    expect(faultToParam(zoneFault("c"))).toBe("zone:c:down");
  });

  it("reads itself back", () => {
    expect(parseFaultParam("zone:b:down")).toEqual({ target: "Zone", id: "b", kind: "down" });
  });

  it("refuses a letter outside the closed set", () => {
    expect(parseFaultParam("zone:d:down")).toBeNull();
    expect(parseFaultParam("zone:A:down")).toBeNull();
    expect(parseFaultParam("zone::down")).toBeNull();
  });

  it("refuses anything but down, because a zone has nothing else to lose", () => {
    expect(parseFaultParam("zone:a:degraded")).toBeNull();
    expect(parseFaultParam("zone:a:latency:2000")).toBeNull();
  });

  it("travels through a search string beside the element faults", () => {
    const faults = [
      { target: "Node", id: API, kind: "down" } as Fault,
      zoneFault("b"),
      zoneFault("a"),
    ];

    const search = searchWithScenario("", faults);

    expect(search).toContain("fault=zone%3Aa%3Adown");
    expect(search).toContain("fault=zone%3Ab%3Adown");
    expect(scenarioFromSearch(search)).toEqual(canonical(faults));
  });

  it("keeps whatever else the query string was carrying", () => {
    const search = searchWithScenario("?lens=availability", [zoneFault("a")]);

    expect(search).toContain("lens=availability");
    expect(scenarioFromSearch(search)).toEqual([zoneFault("a")]);
  });

  it("drops a malformed zone instead of taking the whole link down with it", () => {
    expect(scenarioFromSearch("?fault=zone:d:down&fault=zone:a:down")).toEqual([zoneFault("a")]);
  });
});

describe("splitting the scenario for the wire", () => {
  const scenario = [
    { target: "Node", id: API, kind: "down" } as Fault,
    zoneFault("c"),
    { target: "Connection", id: EDGE, kind: "latency", value: 8000 } as Fault,
    zoneFault("a"),
  ];

  it("hands the elements to faults and the letters to zones", () => {
    expect(nodeFaults(scenario)).toEqual([scenario[0], scenario[2]]);
    expect(downZones(scenario)).toEqual(["a", "c"]);
  });

  it("says which half a fault belongs to", () => {
    expect(isZoneFault(zoneFault("a"))).toBe(true);
    expect(isZoneFault(scenario[0])).toBe(false);
  });

  it("loses nothing on the way", () => {
    expect(nodeFaults(scenario).length + downZones(scenario).length).toBe(scenario.length);
  });
});

describe("identity", () => {
  it("orders zones after the elements and among themselves", () => {
    const mixed = [zoneFault("c"), { target: "Node", id: API, kind: "down" } as Fault, zoneFault("a")];

    expect(canonical(mixed).map(faultToParam)).toEqual([
      `node:${API.replace(/-/g, "")}:down`,
      "zone:a:down",
      "zone:c:down",
    ]);
  });

  it("gives the same key however the scenario was written", () => {
    const one = [zoneFault("b"), { target: "Node", id: API, kind: "down" } as Fault];
    const other = [{ target: "Node", id: API, kind: "down" } as Fault, zoneFault("b")];

    expect(scenarioKey(one)).toBe(scenarioKey(other));
  });

  it("tells two zones apart and a zone from a node", () => {
    expect(sameFault(zoneFault("a"), zoneFault("a"))).toBe(true);
    expect(sameFault(zoneFault("a"), zoneFault("b"))).toBe(false);
    expect(sameFault(zoneFault("a"), { target: "Node", id: API, kind: "down" })).toBe(false);
  });
});
