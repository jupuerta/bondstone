import { describe, expect, it } from "vitest";
import { capacityScale, decadeLabel } from "./capacityScale";

describe("capacityScale", () => {
  it("snaps to whole decades around the data", () => {
    const scale = capacityScale([200, 300, 5_000, 1_000_000]);

    expect(scale.min).toBe(100);
    expect(scale.max).toBe(1_000_000);
    expect(scale.decades).toEqual([100, 1_000, 10_000, 100_000, 1_000_000]);
  });

  it("gives every ten-fold the same distance", () => {
    const { positionOf } = capacityScale([200, 1_000_000]);
    const decade = positionOf(1_000) - positionOf(100);

    expect(positionOf(10_000) - positionOf(1_000)).toBeCloseTo(decade, 10);
    expect(positionOf(1_000_000) - positionOf(100_000)).toBeCloseTo(decade, 10);
  });

  it("puts the ends of the scale at the ends of the bar", () => {
    const scale = capacityScale([200, 1_000_000]);

    expect(scale.positionOf(scale.min)).toBe(0);
    expect(scale.positionOf(scale.max)).toBe(1);
  });

  it("leaves a decade of room when the largest value is itself a power of ten", () => {
    expect(capacityScale([1_000, 1_000]).max).toBe(10_000);
  });

  it("names a decade the way the ruler prints it", () => {
    expect(decadeLabel(100)).toBe("100");
    expect(decadeLabel(1_000)).toBe("1k");
    expect(decadeLabel(1_000_000)).toBe("1M");
  });
});
