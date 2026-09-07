import { useT } from "../../../i18n";
import type { Mechanism } from "../../../types/architecture";
import { MECHANISM_LABEL } from "./mechanismLabel";

export function MechanismChip({ mechanism, className = "" }: { mechanism: Mechanism; className?: string }) {
  const t = useT();
  return (
    <span className={`inline-flex h-[20px] shrink-0 items-center gap-1.5 rounded-pill border border-[color-mix(in_oklab,var(--af-warm-panel)_60%,transparent)] px-2 font-mono text-[10.5px] uppercase tracking-[0.06em] whitespace-nowrap text-warm-panel ${className}`}>
      <span aria-hidden className="size-1.5 shrink-0 rounded-full border-2 border-current" />
      {t(MECHANISM_LABEL[mechanism])}
    </span>
  );
}
