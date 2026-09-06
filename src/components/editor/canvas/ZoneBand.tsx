import { useT } from "../../../i18n";
import type { ZoneBand as Band } from "./zoneBands";

export function ZoneBand({ band, down }: { band: Band; down: boolean }) {
  const t = useT();
  const zone = band.zone.toUpperCase();

  return (
    <div
      aria-hidden
      className={`pointer-events-none absolute rounded-[10px] border ${down ? "border-hot-panel" : "border-dashed border-panel-line-2"}`}
      style={{ transform: `translate(${band.x}px, ${band.y}px)`, width: band.w, height: band.h }}
    >
      <span
        className={`absolute -top-[9px] left-2.5 bg-canvas px-1.5 font-mono text-[10px] uppercase tracking-[0.1em] ${down ? "font-semibold text-hot-panel" : "text-panel-ink-3"}`}
      >
        {down ? t("ui.canvas.zoneDown", { zone }) : t("ui.canvas.zone", { zone })}
      </span>
    </div>
  );
}
