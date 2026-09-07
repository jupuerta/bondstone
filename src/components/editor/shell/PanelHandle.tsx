import { useState, type KeyboardEvent, type PointerEvent } from "react";
import { PANEL_WIDTH_MAX, PANEL_WIDTH_MIN, clampPanelWidth } from "../../../domain/editorPrefs";
import { useT } from "../../../i18n";

const STEP = 16;

interface PanelHandleProps {
  width: number;
  onChange: (width: number) => void;
}

export function PanelHandle({ width, onChange }: PanelHandleProps) {
  const t = useT();
  const [dragging, setDragging] = useState(false);

  const onPointerDown = (event: PointerEvent) => {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = width;
    const move = (moved: globalThis.PointerEvent) => onChange(clampPanelWidth(startWidth - (moved.clientX - startX)));
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      document.body.classList.remove("af-col-resizing");
      setDragging(false);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    document.body.classList.add("af-col-resizing");
    setDragging(true);
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    event.preventDefault();
    onChange(clampPanelWidth(width + (event.key === "ArrowLeft" ? STEP : -STEP)));
  };

  return (
    <>
      <div
        role="separator"
        aria-orientation="vertical"
        aria-label={t("ui.panel.resize")}
        aria-valuenow={width}
        aria-valuemin={PANEL_WIDTH_MIN}
        aria-valuemax={PANEL_WIDTH_MAX}
        tabIndex={0}
        onPointerDown={onPointerDown}
        onKeyDown={onKeyDown}
        className="group absolute top-0 bottom-0 -left-1 z-10 w-2 cursor-col-resize outline-none"
      >
        <span
          aria-hidden
          className={`absolute top-1/2 left-[1.5px] h-[34px] w-[5px] -translate-y-1/2 rounded-[3px] transition-colors ${
            dragging ? "bg-panel-ink-2" : "bg-panel-line-2 group-hover:bg-panel-ink-3 group-focus-visible:bg-panel-focus"
          }`}
        />
      </div>
      {dragging && (
        <span className="num pointer-events-none absolute top-1/2 -left-16 z-10 -translate-y-1/2 rounded-pill border border-panel-line bg-panel-2 px-2 py-0.5 text-meta text-panel-ink">{width} px</span>
      )}
    </>
  );
}
