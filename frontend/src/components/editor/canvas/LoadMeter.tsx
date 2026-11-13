import type { ComponentLoad, NodeType } from "../../../types/architecture";
import { SATURATION, loadMeter, type LoadTone } from "./loadMeter";

export const METER_SEGMENTS = 10;

const INK: Record<LoadTone, string> = {
  quiet: "bg-panel-ink-3",
  warm: "bg-warm-panel",
  hot: "bg-hot-panel",
};

export function litSegments(fill: number): number {
  return fill <= 0 ? 0 : Math.max(1, Math.round(fill * METER_SEGMENTS));
}

export function LoadMeter({ type, load, gap = 1.5 }: { type: NodeType; load: ComponentLoad | null; gap?: number }) {
  const meter = loadMeter(type, load);
  if (meter === null) return null;

  const lit = litSegments(meter.fill);

  return (
    <span aria-hidden className="absolute inset-x-0 bottom-0 flex h-1" style={{ gap }}>
      {Array.from({ length: METER_SEGMENTS }, (_, index) => (
        <span key={index} className={`min-w-0 flex-1 ${index < lit ? INK[meter.tone] : "bg-wash/10"}`} />
      ))}
      <span className="absolute -top-0.5 -bottom-px w-px bg-panel-ink opacity-80" style={{ left: `${SATURATION * 100}%` }} />
    </span>
  );
}
