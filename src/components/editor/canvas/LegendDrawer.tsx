import type { ReactElement } from "react";
import { useT } from "../../../i18n";
import type { NodeType } from "../../../types/architecture";
import { TypeIcon } from "../palette/TypeIcons";
import { legendSections, type BoxState, type LegendMark, type LegendMode, type LegendRow } from "./legend";
import { SATURATION } from "./loadMeter";
import { ASYNC_DASH, BUSIEST_OPACITY, EDGE_STROKE_WIDTH, SYNC_OPACITY } from "./marks";

export const LEGEND_WIDTH = 300;

interface LegendDrawerProps {
  mode: LegendMode;
  open: boolean;
  onClose: () => void;
  narrow: boolean;
}

export function LegendDrawer({ mode, open, onClose, narrow }: LegendDrawerProps) {
  const t = useT();

  return (
    <aside
      aria-label={t("ui.legend.title")}
      aria-hidden={!open}
      inert={!open}
      className={`af-legend absolute inset-y-0 right-0 z-20 flex max-w-full flex-col border-l border-panel-line-2 bg-panel ${open ? "translate-x-0" : "translate-x-full"}`}
      style={{ width: narrow ? "100%" : LEGEND_WIDTH }}
    >
      <div className="flex h-11 shrink-0 items-center justify-between border-b border-panel-line px-3">
        <span className="font-display text-[15px] leading-[1.2] font-semibold text-panel-ink">{t("ui.legend.title")}</span>
        <button type="button" onClick={onClose} aria-label={t("ui.legend.collapse")} title={t("ui.legend.collapse")} className={`inline-flex items-center justify-center rounded-pill text-panel-ink-3 hover:text-panel-ink ${narrow ? "size-11" : "size-7"}`}>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden>
            <path d="M6 6l12 12M18 6L6 18" />
          </svg>
        </button>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-3 pt-1 pb-3">
        {legendSections(mode).map((section) => (
          <section key={section.title}>
            <div className="eyebrow pt-3 pb-1.5 text-panel-ink-3">{t(section.title)}</div>
            <ul className="flex flex-col gap-2">
              {section.rows.map((row) => (
                <Row key={row.label} row={row} />
              ))}
            </ul>
            {section.note && <p className="mt-2.5 text-[11px] leading-[1.45] text-panel-ink-3">{t(section.note)}</p>}
          </section>
        ))}
      </div>
    </aside>
  );
}

function Row({ row }: { row: LegendRow }) {
  const t = useT();

  if (row.mark.kind === "family") {
    return (
      <li className="flex items-center gap-3">
        <span className="shrink-0 text-[12px] font-medium text-panel-ink">{t(row.label)}</span>
        <span className="flex flex-1 justify-end">
          <Mark mark={row.mark} />
        </span>
      </li>
    );
  }

  if (row.mark.kind === "unsized") {
    return (
      <li className="flex flex-col gap-1.5">
        <Sentence row={row} />
        <Mark mark={row.mark} />
      </li>
    );
  }

  return (
    <li className="flex items-start gap-2.5">
      <span className="flex w-12 shrink-0 justify-center pt-px">
        <Mark mark={row.mark} />
      </span>
      <Sentence row={row} />
    </li>
  );
}

function Sentence({ row }: { row: LegendRow }) {
  const t = useT();

  return (
    <span className="min-w-0 flex-1">
      <span className="block text-[12px] font-medium text-panel-ink">{t(row.label)}</span>
      {row.note && <span className="mt-0.5 block text-[11px] leading-[1.45] text-panel-ink-2">{t(row.note, row.args)}</span>}
    </span>
  );
}

function Mark({ mark }: { mark: LegendMark }): ReactElement {
  const t = useT();

  switch (mark.kind) {
    case "family":
    case "unsized":
      return (
        <span className="flex flex-wrap gap-1">
          {mark.types.map((type) => (
            <span key={type} title={t(`ui.node.${type}`)} className="inline-flex size-5 items-center justify-center rounded-[5px] border border-panel-line" style={{ background: `color-mix(in oklab, var(--af-t-${type}) 9%, var(--af-panel-2))` }}>
              <TypeIcon type={type} size={11} />
            </span>
          ))}
        </span>
      );

    case "call":
      return <Arrow dash={mark.asynchronous ? ASYNC_DASH : undefined} opacity={mark.asynchronous ? SYNC_OPACITY : SYNC_OPACITY} />;

    case "flow":
      return (
        <span className="flex flex-col gap-1">
          <Arrow opacity={SYNC_OPACITY} />
          <Arrow opacity={BUSIEST_OPACITY} />
        </span>
      );

    case "meter":
      return (
        <Box>
          <span aria-hidden className="absolute inset-x-0 bottom-0 flex h-[3px] gap-px">
            {Array.from({ length: 10 }, (_, index) => (
              <span key={index} className={`min-w-0 flex-1 ${index < 7 ? "bg-panel-ink-3" : "bg-white/10"}`} />
            ))}
            <span className="absolute -top-0.5 -bottom-px w-px bg-panel-ink opacity-80" style={{ left: `${SATURATION * 100}%` }} />
          </span>
        </Box>
      );

    case "severities":
      return (
        <span className="flex items-center gap-1.5">
          {mark.order.map((severity) => (
            <span key={severity} className={`size-[9px] rounded-full ${severity === "Critical" ? "bg-hot-panel" : "border-2 border-warm-panel"}`} />
          ))}
        </span>
      );

    case "zone":
      return (
        <span className={`flex h-[26px] w-[46px] shrink-0 items-start rounded-md border p-[3px] ${mark.down ? "border-hot-panel" : "border-dashed border-panel-line-2"}`}>
          <span className={`font-mono text-[8px] uppercase tracking-[0.1em] ${mark.down ? "font-semibold text-hot-panel" : "text-panel-ink-3"}`}>A</span>
        </span>
      );

    case "box":
      return <Box state={mark.state} />;
  }
}

function Arrow({ dash, opacity }: { dash?: string; opacity: number }) {
  return (
    <svg width="46" height="12" viewBox="0 0 46 12" aria-hidden className="shrink-0">
      <path d="M1 6h38" fill="none" stroke="var(--af-panel-ink)" strokeOpacity={opacity} strokeWidth={EDGE_STROKE_WIDTH} strokeDasharray={dash} />
      <path d="M38 2.5L45 6l-7 3.5z" fill="var(--af-panel-ink)" fillOpacity={opacity} />
    </svg>
  );
}

const BOX_RING: Record<BoxState, string> = {
  healthy: "border-panel-line",
  degraded: "border-warm-panel",
  down: "border-hot-panel",
  contained: "border-panel-line af-node-contained",
  highlighted: "border-panel-line af-node-highlighted",
};

function Box({ state = "healthy", children }: { state?: BoxState; children?: ReactElement }) {
  const type: NodeType = "Api";

  return (
    <span className={`relative block h-[26px] w-[46px] shrink-0 overflow-hidden rounded-[5px] border ${BOX_RING[state]}`} style={{ background: `color-mix(in oklab, var(--af-t-${type}) 9%, var(--af-panel-2))` }}>
      <span className="block h-[2px]" style={{ background: `var(--af-t-${type})` }} />
      {state === "down" && <span className="absolute top-2 left-2 size-2 rounded-full bg-hot-panel" />}
      {state === "degraded" && <span className="absolute top-2 left-2 size-2 rounded-full border-2 border-warm-panel" />}
      {children}
    </span>
  );
}
