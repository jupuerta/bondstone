import { describe, expect, it } from "vitest";
import {
  HELP_CARD_MAX_HEIGHT,
  TERM_CARD_MAX_HEIGHT,
  TERM_CARD_WIDTH,
  placeHelpCard,
  placeTermCard,
} from "./placement";

const canvas = rect({ left: 236, top: 52, width: 868, height: 848 });

const viewport = { width: 1440, height: 900 };

describe("the component sheet", () => {
  it("anchors to the row that opened it", () => {
    expect(placeHelpCard(canvas, 52 + 180)).toEqual({ top: 180, left: 16 });
  });

  it("never starts above the inset, however high the row", () => {
    expect(placeHelpCard(canvas, 52 + 4)).toEqual({ top: 16, left: 16 });
  });

  it("stops before the bottom rather than following a low row off the canvas", () => {
    const { top } = placeHelpCard(canvas, 52 + 830);
    expect(top).toBe(848 - HELP_CARD_MAX_HEIGHT - 16);
    expect(top + HELP_CARD_MAX_HEIGHT).toBeLessThanOrEqual(848);
  });
});

describe("a glossary term's card", () => {
  const inspector = rect({ left: 1140, top: 90, width: 272, height: 740 });

  it("opens to the right of a word with the screen on its right", () => {
    const word = rect({ left: 300, top: 400, width: 84, height: 15 });

    expect(placeTermCard(word, viewport, 190)).toEqual({ top: 400, left: 384 + 10 });
  });

  it("opens over the canvas, not over the analysis panel", () => {
    const word = rect({ left: 1157, top: 480, width: 137, height: 15 });
    const { left } = placeTermCard(word, viewport, 190, inspector);

    expect(left + TERM_CARD_WIDTH).toBeLessThan(inspector.left);
  });

  it("clears the panel the word is written on, not just the word", () => {
    const word = rect({ left: 1157, top: 480, width: 137, height: 15 });

    expect(placeTermCard(word, viewport, 190, inspector).left).toBe(1140 - 10 - TERM_CARD_WIDTH);
    expect(placeTermCard(word, viewport, 190).left).toBe(1157 - 10 - TERM_CARD_WIDTH);
  });

  it("still starts on the line the word is on", () => {
    const word = rect({ left: 1157, top: 480, width: 137, height: 15 });

    expect(placeTermCard(word, viewport, 190, inspector).top).toBe(480);
  });

  it("never leaves the viewport, above, below or to either side", () => {
    const corners = [
      rect({ left: 4, top: 4, width: 60, height: 15 }),
      rect({ left: 1400, top: 880, width: 36, height: 15 }),
    ];

    for (const word of corners) {
      const { top, left } = placeTermCard(word, viewport, TERM_CARD_MAX_HEIGHT);

      expect(left).toBeGreaterThanOrEqual(16);
      expect(left + TERM_CARD_WIDTH).toBeLessThanOrEqual(viewport.width - 16);
      expect(top).toBeGreaterThanOrEqual(16);
      expect(top + TERM_CARD_MAX_HEIGHT).toBeLessThanOrEqual(viewport.height - 16);
    }
  });

  it("stays with the word it belongs to, instead of reserving the room it might have needed", () => {
    const word = rect({ left: 1160, top: 690, width: 120, height: 15 });

    expect(placeTermCard(word, viewport, 190).top).toBe(690);
    expect(placeTermCard(word, viewport, 340).top).toBe(900 - 340 - 16);
  });

  it("gives up on the inset rather than on being visible, on a viewport shorter than the card", () => {
    const word = rect({ left: 600, top: 120, width: 60, height: 15 });

    expect(placeTermCard(word, { width: 1440, height: 200 }, 300).top).toBe(16);
  });
});

function rect({ left, top, width, height }: { left: number; top: number; width: number; height: number }): DOMRect {
  return { x: left, y: top, left, top, width, height, right: left + width, bottom: top + height, toJSON: () => ({}) };
}
