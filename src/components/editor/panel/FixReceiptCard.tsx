import { useT } from "../../../i18n";
import { fixDelta, type FixDelta, type FixReceipt } from "./fixReceipt";

interface FixReceiptCardProps {
  receipt: FixReceipt;
  onUndo: () => void;
  onDismiss: () => void;
  onOpenCompare: (() => void) | null;
  narrow: boolean;
}

const TINT = "color-mix(in oklab, var(--af-ok-panel) 8%, transparent)";
const LINE = "color-mix(in oklab, var(--af-ok-panel) 32%, transparent)";

export function FixReceiptCard({ receipt, onUndo, onDismiss, onOpenCompare, narrow }: FixReceiptCardProps) {
  const t = useT();
  const delta = receipt.before && receipt.after ? fixDelta(receipt.before, receipt.after) : null;
  const link = `text-[12px] leading-[1.45] text-accent-panel underline underline-offset-4 hover:text-panel-ink ${narrow ? "min-h-11" : ""}`;

  return (
    <div className="mx-3 my-2 rounded-card px-2.5 py-2" style={{ background: TINT, border: `1px solid ${LINE}` }}>
      <div className="flex items-start gap-2">
        <span aria-hidden className="mt-[3px] size-2 shrink-0 rounded-full bg-ok-panel" />
        <span className="min-w-0 flex-1 text-[12px] leading-[1.45] text-panel-ink">{t(receipt.done)}</span>
        <button type="button" onClick={onDismiss} aria-label={t("ui.analysis.fix.dismiss")} title={t("ui.analysis.fix.dismiss")} className={`-mr-1 shrink-0 rounded-pill px-1.5 text-[12px] leading-none text-panel-ink-3 hover:text-panel-ink ${narrow ? "min-h-11 min-w-11" : "-mt-0.5"}`}>
          ×
        </button>
      </div>

      <FixDeltaLine delta={delta} measuring={receipt.before !== null} />

      <div className="mt-2 flex flex-wrap items-center gap-x-3.5 gap-y-1">
        <button type="button" onClick={onUndo} className={link}>
          {t("ui.analysis.fix.undo")}
        </button>
        {onOpenCompare && (
          <button type="button" onClick={onOpenCompare} className={link}>
            {t("ui.fixCompare.reopen")}
          </button>
        )}
      </div>
    </div>
  );
}

export function FixDeltaLine({ delta, measuring }: { delta: FixDelta | null; measuring: boolean }) {
  const t = useT();

  if (delta === null) return measuring ? <p className="mt-1.5 text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.analysis.fix.measuring")}</p> : null;

  return (
    <div className="mt-1.5 flex flex-wrap items-baseline gap-x-3.5 gap-y-1">
      {delta.points !== null && delta.points !== 0 && (
        <span className={`num text-[17px] font-semibold ${delta.points > 0 ? "text-ok-panel" : "text-hot-panel"}`}>{t("ui.analysis.fix.scoreMoved", { from: delta.from!, to: delta.to! })}</span>
      )}
      {delta.moved.map((move) => (
        <span key={move.area} className="text-[12px] text-panel-ink-2">
          {t("ui.analysis.fix.areaMoved", { area: { key: `ui.area.${move.area}` }, from: move.from, to: move.to })}
        </span>
      ))}
      {delta.points === 0 && <p className="w-full text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.analysis.fix.scoreSame")}</p>}
    </div>
  );
}
