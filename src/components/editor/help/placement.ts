export const HELP_CARD_WIDTH = 360;

export const HELP_CARD_MAX_HEIGHT = 560;

export const TERM_CARD_WIDTH = 340;
export const TERM_CARD_MAX_HEIGHT = 340;

const INSET = 16;

const TERM_GAP = 10;

export interface HelpPlacement {
  top: number;
  left: number;
}

export function placeHelpCard(canvas: DOMRect, anchorTop: number): HelpPlacement {
  const floor = canvas.height - HELP_CARD_MAX_HEIGHT - INSET;
  const anchored = Math.max(INSET, Math.min(anchorTop - canvas.top, floor));

  return { top: anchored, left: INSET };
}

export interface Viewport {
  width: number;
  height: number;
}

export function placeTermCard(anchor: DOMRect, viewport: Viewport, height: number, panel?: DOMRect): HelpPlacement {
  const nearSide = panel?.left ?? anchor.left;
  const farSide = panel?.right ?? anchor.right;
  const beside =
    viewport.width - farSide >= nearSide ? farSide + TERM_GAP : nearSide - TERM_GAP - TERM_CARD_WIDTH;

  return {
    top: clamp(anchor.top, INSET, viewport.height - height - INSET),
    left: clamp(beside, INSET, viewport.width - TERM_CARD_WIDTH - INSET),
  };
}

function clamp(value: number, low: number, high: number): number {
  return Math.max(low, Math.min(value, high));
}
