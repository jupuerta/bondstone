import { describe, expect, it } from "vitest";
import { BAND_PAD, bandDown, zoneBands, type BandNode } from "./zoneBands";
import { NODE_H, NODE_W } from "./marks";
import type { ElementState } from "../../../types/architecture";

function node(id: string, x: number, y: number, zone?: string, type: BandNode["type"] = "Api"): BandNode {
  return { id, type, position: { x, y }, configuration: zone === undefined ? {} : { zone } };
}

function down(id: string): [string, ElementState] {
  return [
    id,
    { type: "Node", id, state: "Down", reason: { key: "x" }, mechanism: null, propagationStep: 0 },
  ];
}

function healthy(id: string): [string, ElementState] {
  return [
    id,
    { type: "Node", id, state: "Healthy", reason: { key: "x" }, mechanism: null, propagationStep: 0 },
  ];
}

describe("zoneBands", () => {
  it("draws nothing when nobody declares a zone", () => {
    expect(zoneBands([node("1", 0, 0), node("2", 300, 0)])).toEqual([]);
  });

  it("wraps the boxes of a zone without moving them", () => {
    const [band] = zoneBands([node("1", 100, 40, "a")]);

    expect(band.x).toBe(100 - BAND_PAD);
    expect(band.y).toBe(40 - BAND_PAD);
    expect(band.w).toBe(NODE_W + BAND_PAD * 2);
    expect(band.h).toBe(NODE_H + BAND_PAD * 2);
  });

  it("grows to hold every box of the zone, wherever they were drawn", () => {
    const [band] = zoneBands([node("1", 0, 0, "a"), node("2", 400, 200, "a")]);

    expect(band.nodeIds).toEqual(["1", "2"]);
    expect(band.x).toBe(-BAND_PAD);
    expect(band.w).toBe(400 + NODE_W + BAND_PAD * 2);
    expect(band.h).toBe(200 + NODE_H + BAND_PAD * 2);
  });

  it("prefers the measured size of a box over the nominal one", () => {
    const tall = { ...node("1", 0, 0, "a"), width: 220, height: 90 };

    expect(zoneBands([tall])[0].w).toBe(220 + BAND_PAD * 2);
    expect(zoneBands([tall])[0].h).toBe(90 + BAND_PAD * 2);
  });

  it("keeps one band per declared zone and leaves the undeclared out of all of them", () => {
    const bands = zoneBands([node("1", 0, 0, "a"), node("2", 0, 200, "b"), node("3", 0, 400)]);

    expect(bands.map((band) => band.zone).sort()).toEqual(["a", "b"]);
    expect(bands.flatMap((band) => band.nodeIds).sort()).toEqual(["1", "2"]);
  });

  it("ignores a zone written on a type nobody places", () => {
    expect(zoneBands([node("1", 0, 0, "a", "Cdn"), node("2", 0, 0, "a", "Client"), node("3", 0, 0, "a", "IdentityProvider")])).toEqual([]);
  });

  it("ignores a zone outside the closed set", () => {
    expect(zoneBands([node("1", 0, 0, "d")])).toEqual([]);
  });

  it("paints the widest band first so a smaller one on top of it stays visible", () => {
    const bands = zoneBands([
      node("1", 0, 0, "a"),
      node("2", 900, 600, "a"),
      node("3", 100, 100, "b"),
    ]);

    expect(bands.map((band) => band.zone)).toEqual(["a", "b"]);
  });
});

describe("bandDown", () => {
  const [band] = zoneBands([node("1", 0, 0, "a"), node("2", 300, 0, "a")]);

  it("goes out only when everything it holds is down", () => {
    expect(bandDown(band, new Map([down("1"), down("2")]))).toBe(true);
  });

  it("stays lit while one of its boxes is still serving", () => {
    expect(bandDown(band, new Map([down("1"), healthy("2")]))).toBe(false);
  });

  it("stays lit with no simulation behind it", () => {
    expect(bandDown(band, new Map())).toBe(false);
  });
});
