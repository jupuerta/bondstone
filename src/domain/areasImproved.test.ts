import { describe, expect, it } from "vitest";
import { areasImproved, bestGains } from "./areasImproved";
import type { Area, ArchitectureSnapshot } from "../types/architecture";

let version = 0;

function point(areas: Partial<Record<Area, number>> | null, scoringVersion = "v8"): ArchitectureSnapshot {
  version += 1;

  return {
    version,
    scoringVersion,
    score: 70,
    summary: null,
    createdAt: new Date(2026, 7, 22, 0, version).toISOString(),
    areas,
  };
}

describe("areasImproved", () => {
  it("reads the climb between the first measurement and the last", () => {
    const gains = areasImproved([
      point({ Availability: 70, Resilience: 52 }),
      point({ Availability: 70, Resilience: 86 }),
    ]);

    expect(gains).toEqual([{ area: "Resilience", from: 52, to: 86, gain: 34 }]);
  });

  it("ignores the route between the ends", () => {
    const gains = areasImproved([
      point({ Coupling: 60 }),
      point({ Coupling: 30 }),
      point({ Coupling: 74 }),
    ]);

    expect(gains).toEqual([{ area: "Coupling", from: 60, to: 74, gain: 14 }]);
  });

  it("says nothing about an area that went down or stood still", () => {
    const gains = areasImproved([
      point({ Availability: 90, Security: 40 }),
      point({ Availability: 90, Security: 20 }),
    ]);

    expect(gains).toEqual([]);
  });

  it("never subtracts across scoring versions", () => {
    const gains = areasImproved([
      point({ Resilience: 20 }, "v7"),
      point({ Resilience: 80 }),
      point({ Resilience: 86 }),
    ]);

    expect(gains).toEqual([{ area: "Resilience", from: 80, to: 86, gain: 6 }]);
  });

  it("needs both ends to have measured the area", () => {
    const gains = areasImproved([point({ Availability: 70 }), point({ Availability: 70, Security: 74 })]);

    expect(gains).toEqual([]);
  });

  it("ignores snapshots with no breakdown at all", () => {
    expect(areasImproved([point(null), point({}), point({ Coupling: 50 })])).toEqual([]);
    expect(areasImproved([point(null), point({ Coupling: 50 }), point({ Coupling: 62 })])).toEqual([
      { area: "Coupling", from: 50, to: 62, gain: 12 },
    ]);
  });

  it("has nothing to say about a history with one measurement or none", () => {
    expect(areasImproved([])).toEqual([]);
    expect(areasImproved([point({ Coupling: 50 })])).toEqual([]);
  });

  it("puts the biggest climb first", () => {
    const gains = areasImproved([
      point({ Availability: 60, Resilience: 50, Coupling: 40 }),
      point({ Availability: 70, Resilience: 90, Coupling: 60 }),
    ]);

    expect(gains.map((gain) => gain.area)).toEqual(["Resilience", "Coupling", "Availability"]);
  });
});

describe("bestGains", () => {
  it("keeps the best single climb per area and never the sum", () => {
    const gains = bestGains([
      [point({ Resilience: 50 }), point({ Resilience: 70 })],
      [point({ Resilience: 20 }), point({ Resilience: 55 })],
    ]);

    expect(gains).toEqual([{ area: "Resilience", from: 20, to: 55, gain: 35 }]);
  });

  it("merges different areas from different architectures", () => {
    const gains = bestGains([
      [point({ Security: 40 }), point({ Security: 90 })],
      [point({ Coupling: 60 }), point({ Coupling: 74 })],
      [],
    ]);

    expect(gains.map((gain) => gain.area)).toEqual(["Security", "Coupling"]);
  });
});
