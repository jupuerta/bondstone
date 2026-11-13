import { useEffect, useMemo, useRef } from "react";
import { useT } from "../../../i18n";
import { useEscape } from "../../../hooks/useEscape";
import type { ConnectionDto, NodeDto } from "../../../types/architecture";
import { layoutOf } from "../../engine/layout";
import { Button } from "../../ui/Button";
import { drawn, graphDelta } from "../canvas/graphDelta";
import { FixDeltaLine } from "../panel/FixReceiptCard";
import { fixDelta, type FixReceipt } from "../panel/fixReceipt";
import { Thumbnail } from "../starters/Thumbnail";

export interface FixShot {
  nodes: NodeDto[];
  connections: ConnectionDto[];
}

export interface FixShots {
  before: FixShot;
  after: FixShot;
}

interface FixDiffProps {
  shots: FixShots;
  receipt: FixReceipt;
  onUndo: () => void;
  onClose: () => void;
}

export function FixDiff({ shots, receipt, onUndo, onClose }: FixDiffProps) {
  const t = useT();
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    box.current?.querySelector<HTMLElement>("button")?.focus();
  }, []);

  useEscape(onClose);

  const frame = useMemo(() => layoutOf({ nodes: [...shots.before.nodes, ...shots.after.nodes] }), [shots]);
  const lit = useMemo(() => drawn(graphDelta(shots.before, shots.after)), [shots]);
  const delta = receipt.before && receipt.after ? fixDelta(receipt.before, receipt.after) : null;

  return (
    <div role="dialog" aria-modal="true" aria-labelledby="editor-fix-title" className="fixed inset-0 z-50 flex items-center justify-center bg-canvas/80 p-4 md:p-6" onClick={onClose}>
      <div ref={box} className="flex max-h-full w-full max-w-[880px] flex-col gap-2.5 overflow-y-auto rounded-panel border border-panel-line-2 bg-panel-2 p-3.5 text-panel-ink" onClick={(event) => event.stopPropagation()}>
        <div className="flex items-start justify-between gap-3">
          <h2 id="editor-fix-title" className="font-display text-panel font-semibold text-panel-ink">
            {t("ui.fixCompare.title")}
          </h2>
          <button type="button" onClick={onClose} aria-label={t("ui.close")} className="-mt-1 -mr-1 inline-flex size-9 shrink-0 items-center justify-center rounded-pill text-panel-ink-3 hover:text-panel-ink">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden>
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </div>

        <p className="flex items-start gap-2 text-[12px] leading-[1.45] text-panel-ink-2">
          <span aria-hidden className="mt-[5px] size-2 shrink-0 rounded-full bg-ok-panel" />
          {t(receipt.done)}
        </p>

        <div className="grid gap-3.5 md:grid-cols-2">
          <div className="flex min-w-0 flex-col gap-[5px]">
            <span className="eyebrow text-panel-ink-3">{t("ui.fixCompare.before")}</span>
            <Thumbnail nodes={shots.before.nodes} connections={shots.before.connections} frame={frame} />
          </div>
          <div className="flex min-w-0 flex-col gap-[5px]">
            <span className="eyebrow text-panel-ink-3">{t("ui.fixCompare.after")}</span>
            <Thumbnail nodes={shots.after.nodes} connections={shots.after.connections} frame={frame} highlighted={lit} />
          </div>
        </div>

        <p className="text-[12px] leading-[1.45] text-panel-ink-2">{lit.size > 0 ? t("editor.fixCompare.lit") : t("ui.fixCompare.nothingMoved")}</p>

        <FixDeltaLine delta={delta} measuring={receipt.before !== null} />

        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-panel-line pt-3">
          <span className="text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.fixCompare.appliedNote")}</span>
          <div className="flex gap-2">
            <Button variant="panel" size="sm" onClick={onUndo}>
              {t("ui.analysis.fix.undo")}
            </Button>
            <Button variant="panelPrimary" size="sm" onClick={onClose}>
              {t("ui.close")}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
