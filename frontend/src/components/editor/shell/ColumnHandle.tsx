import { useState, type KeyboardEvent, type PointerEvent } from "react";
import { beginColumnResize, endColumnResize } from "../../../domain/columnResize";

const STEP = 16;

export type ColumnSide = "left" | "right";

interface ColumnHandleProps {
  side: ColumnSide;
  width: number;
  min: number;
  max: number;
  clamp: (width: number) => number;
  label: string;
  onChange: (width: number) => void;
}

export function ColumnHandle({ side, width, min, max, clamp, label, onChange }: ColumnHandleProps) {
  const [dragging, setDragging] = useState(false);
  const grows = side === "left" ? 1 : -1;

  const onPointerDown = (event: PointerEvent) => {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = width;
    const move = (moved: globalThis.PointerEvent) => onChange(clamp(startWidth + grows * (moved.clientX - startX)));
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      endColumnResize();
      setDragging(false);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    beginColumnResize();
    setDragging(true);
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
    event.preventDefault();
    beginColumnResize();
    onChange(clamp(width + grows * (event.key === "ArrowLeft" ? -STEP : STEP)));
    window.setTimeout(endColumnResize, 250);
  };

  return (
    <>
      <div
        role="separator"
        aria-orientation="vertical"
        aria-label={label}
        aria-valuenow={width}
        aria-valuemin={min}
        aria-valuemax={max}
        tabIndex={0}
        onPointerDown={onPointerDown}
        onKeyDown={onKeyDown}
        className={`group absolute top-0 bottom-0 z-10 w-2 cursor-col-resize outline-none ${side === "left" ? "-right-1" : "-left-1"}`}
      >
        <span
          aria-hidden
          className={`absolute top-1/2 left-[1.5px] h-[34px] w-[5px] -translate-y-1/2 rounded-[3px] transition-colors ${
            dragging ? "bg-panel-ink-2" : "bg-panel-line-2 group-hover:bg-panel-ink-3 group-focus-visible:bg-panel-focus"
          }`}
        />
      </div>
      {dragging && (
        <span className={`num pointer-events-none absolute top-1/2 z-10 -translate-y-1/2 rounded-pill border border-panel-line bg-panel-2 px-2 py-0.5 text-meta text-panel-ink ${side === "left" ? "-right-16" : "-left-16"}`}>{width} px</span>
      )}
    </>
  );
}
