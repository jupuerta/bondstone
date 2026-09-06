import { useT } from "../../../i18n";
import type { Severity } from "../../../types/architecture";

interface Tone {
  chip: string;
  dot: string;
  filled: boolean;
}

const TONE: Record<Severity, Tone> = {
  Critical: { chip: "bg-hot-panel text-[#210505]", dot: "bg-[#210505]", filled: true },
  Warning: { chip: "border border-[color-mix(in_oklab,var(--af-warm-panel)_60%,transparent)] text-warm-panel", dot: "", filled: false },
  Info: { chip: "border border-panel-line text-panel-ink-2", dot: "", filled: false },
  Good: { chip: "border border-[color-mix(in_oklab,var(--af-ok-panel)_55%,transparent)] text-ok-panel", dot: "", filled: false },
};

export function SeverityChip({ severity }: { severity: Severity }) {
  const t = useT();
  const tone = TONE[severity];

  return (
    <span className={`inline-flex h-[22px] shrink-0 items-center gap-1.5 rounded-pill px-[9px] font-mono text-[11px] font-semibold uppercase tracking-[0.06em] whitespace-nowrap ${tone.chip}`}>
      <span aria-hidden className={`size-2 shrink-0 rounded-full ${tone.filled ? tone.dot : "border-2 border-current"}`} />
      {t(`web.engine.severity.${severity}`)}
    </span>
  );
}
