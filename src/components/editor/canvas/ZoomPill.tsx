import { useReactFlow, useViewport } from "@xyflow/react";
import { useT } from "../../../i18n";

export function ZoomPill() {
  const t = useT();
  const { zoomIn, zoomOut, fitView } = useReactFlow();
  const { zoom } = useViewport();
  const still = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const duration = () => (still() ? 0 : 150);
  const button = "inline-flex h-7 min-w-7 items-center justify-center rounded-pill text-panel-ink-2 hover:text-panel-ink";

  return (
    <span className="ml-auto inline-flex h-7 shrink-0 items-center gap-1 rounded-pill border border-panel-line px-1.5 font-mono text-[12px]">
      <button type="button" onClick={() => void zoomOut({ duration: duration() })} aria-label={t("editor.a11y.zoomOut")} className={button}>
        −
      </button>
      <button type="button" onClick={() => void fitView({ padding: 0.2, maxZoom: 1, duration: duration() })} aria-label={t("editor.a11y.fitView")} className={`${button} num px-1 text-panel-ink`}>
        {Math.round(zoom * 100)} %
      </button>
      <button type="button" onClick={() => void zoomIn({ duration: duration() })} aria-label={t("editor.a11y.zoomIn")} className={button}>
        +
      </button>
    </span>
  );
}
