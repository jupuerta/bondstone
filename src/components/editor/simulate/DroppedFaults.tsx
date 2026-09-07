import { useT } from "../../../i18n";
import { CloseIcon } from "../../ui/Icons";

export function DroppedFaults({ count, onDismiss }: { count: number; onDismiss: () => void }) {
  const t = useT();
  return (
    <div role="status" className="pointer-events-auto flex items-center gap-2 rounded-card border border-[color-mix(in_oklab,var(--af-warm-panel)_45%,transparent)] bg-panel-2 px-3 py-2 text-[12px] leading-[1.45] text-warm-panel shadow-[0_12px_32px_-16px_rgba(0,0,0,.8)]">
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden className="shrink-0">
        <path d="M12 3l10 18H2z" />
        <path d="M12 10v4M12 17v.5" />
      </svg>
      <span className="min-w-0 flex-1">{t("ui.canvas.faultsDropped", { count })}</span>
      <button type="button" onClick={onDismiss} aria-label={t("ui.dismiss")} className="inline-flex size-7 shrink-0 items-center justify-center rounded-pill text-warm-panel hover:bg-white/8">
        <CloseIcon size={14} />
      </button>
    </div>
  );
}
