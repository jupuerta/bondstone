import { describe, expect, it } from "vitest";
import { oklchToHex } from "./oklch";

describe("oklchToHex", () => {
  it("puts the grey axis where the specification puts it", () => {
    expect(oklchToHex(0, 0, 0)).toBe("#000000");
    expect(oklchToHex(1, 0, 0)).toBe("#ffffff");
    expect(oklchToHex(0.5, 0, 0)).toBe("#636363");
  });

  it("turns the palette's own tints into something a raster can carry", () => {
    expect(oklchToHex(0.74, 0.09, 230)).toBe("#6bb6d9");
    expect(oklchToHex(0.74, 0.09, 40)).toBe("#dc9880");
  });

  it("clamps a colour that falls outside sRGB instead of wrapping it", () => {
    expect(oklchToHex(0.74, 0.4, 230)).toMatch(/^#[0-9a-f]{6}$/);
    expect(oklchToHex(1.4, 0.09, 40)).toBe("#ffffff");
  });
});
