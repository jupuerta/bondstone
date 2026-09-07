import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { describe, expect, it } from "vitest";
import { ACCENT_OKLCH, POSTCARD_PALETTE, SCREEN_PALETTE, TINT_OKLCH, stateColor } from "./palette";
import { NODE_TYPES } from "../components/editor/canvas/types";

const css = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "..", "index.css"), "utf8");

function declared(variable: string): [number, number, number] {
  const match = css.match(new RegExp(`${variable}:\\s*oklch\\(([\\d.]+)\\s+([\\d.]+)\\s+([\\d.]+)\\)`));
  if (match === null) throw new Error(`'${variable}' is not declared as an oklch triple in index.css`);

  return [Number(match[1]), Number(match[2]), Number(match[3])];
}

function declaredHex(variable: string): string {
  const match = css.match(new RegExp(`${variable}:\\s*(#[0-9a-f]{6})`));
  if (match === null) throw new Error(`'${variable}' is not declared as a hex colour in index.css`);

  return match[1];
}

describe("the postcard's palette", () => {
  it("carries the same tint the screen carries, for every one of the sixteen types", () => {
    for (const type of NODE_TYPES) {
      expect(TINT_OKLCH[type], type).toEqual(declared(`--af-t-${type}`));
    }
  });

  it("carries the panel the editor is drawn on, and its accent", () => {
    expect(POSTCARD_PALETTE.paper).toBe(declaredHex("--af-panel"));
    expect(POSTCARD_PALETTE.plate).toBe(declaredHex("--af-panel-2"));
    expect(POSTCARD_PALETTE.ink).toBe(declaredHex("--af-panel-ink"));
    expect(POSTCARD_PALETTE.hot).toBe(declaredHex("--af-hot-panel"));
    expect(POSTCARD_PALETTE.warm).toBe(declaredHex("--af-warm-panel"));
    expect(POSTCARD_PALETTE.ok).toBe(declaredHex("--af-ok-panel"));
    expect(ACCENT_OKLCH).toEqual(declared("--af-accent-panel"));
  });

  it("resolves to literal colours, because a raster cannot read a custom property", () => {
    for (const type of NODE_TYPES) {
      expect(POSTCARD_PALETTE.tintOf(type), type).toMatch(/^#[0-9a-f]{6}$/);
    }

    for (const colour of [POSTCARD_PALETTE.paper, POSTCARD_PALETTE.plate, POSTCARD_PALETTE.ink, POSTCARD_PALETTE.line, POSTCARD_PALETTE.accent]) {
      expect(colour).toMatch(/^#[0-9a-f]{6}$/);
    }
  });

  it("keeps the screen on custom properties, so the preview follows the tokens", () => {
    expect(SCREEN_PALETTE.tintOf("Api")).toBe("var(--af-t-Api)");
    expect(SCREEN_PALETTE.paper).toBe("var(--af-canvas)");
  });

  it("paints a fallen component in the colour the canvas paints it", () => {
    expect(stateColor("Down", POSTCARD_PALETTE, "Api")).toBe(POSTCARD_PALETTE.hot);
    expect(stateColor("Degraded", POSTCARD_PALETTE, "Api")).toBe(POSTCARD_PALETTE.warm);
    expect(stateColor("Healthy", POSTCARD_PALETTE, "Api")).toBe(POSTCARD_PALETTE.tintOf("Api"));
  });
});
