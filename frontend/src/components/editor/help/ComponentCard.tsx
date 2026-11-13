import { useMemo, type CSSProperties } from "react";
import type { NodeType } from "../../../types/architecture";
import { ComponentSheet } from "./ComponentSheet";
import { HELP_CARD_MAX_HEIGHT, HELP_CARD_WIDTH, placeHelpCard } from "./placement";

export const HELP_CARD_ID = "editor-component-card";

interface ComponentCardProps {
  type: NodeType;
  canvas: HTMLElement | null;
  anchorTop: number;
  onMouseEnter: () => void;
  onMouseLeave: () => void;
}

export function ComponentCard({ type, canvas, anchorTop, onMouseEnter, onMouseLeave }: ComponentCardProps) {
  const placement = useMemo(() => (canvas === null ? { top: 16, left: 16 } : placeHelpCard(canvas.getBoundingClientRect(), anchorTop)), [canvas, anchorTop]);
  const style: CSSProperties = { ...placement, width: HELP_CARD_WIDTH, maxHeight: HELP_CARD_MAX_HEIGHT };

  return (
    <div id={HELP_CARD_ID} role="tooltip" onMouseEnter={onMouseEnter} onMouseLeave={onMouseLeave} style={style} className="absolute z-30 overflow-y-auto rounded-panel border border-panel-line-2 bg-panel-2 p-3.5 text-panel-ink shadow-[0_30px_60px_-30px_rgba(0,0,0,.8)]">
      <ComponentSheet type={type} />
    </div>
  );
}
