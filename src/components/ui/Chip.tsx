import type { ReactNode } from "react";
import { CloseIcon } from "./Icons";

export type ChipVariant = "outline" | "passed" | "started" | "critical" | "warning" | "info" | "code" | "panel";

const VARIANT: Record<ChipVariant, string> = {
  outline: "border border-line text-ink-2",
  passed: "bg-accent-tint text-accent font-semibold",
  started: "border border-line-2 text-ink-2",
  critical: "bg-hot-panel text-[#1a0606] font-semibold",
  warning: "bg-warm-panel text-[#1a1206] font-semibold",
  info: "border border-panel-line text-panel-ink-2",
  code: "border border-panel-line text-panel-ink-2",
  panel: "border border-panel-line text-panel-ink-2",
};

interface ChipProps {
  variant?: ChipVariant;
  children: ReactNode;
  className?: string;
}

/** Chip informativo: no se pulsa. */
export function Chip({ variant = "outline", children, className = "" }: ChipProps) {
  return (
    <span
      className={`inline-flex min-h-7 items-center gap-2 rounded-pill px-2.5 font-mono text-label uppercase tracking-[0.06em] ${VARIANT[variant]} ${className}`}
    >
      {children}
    </span>
  );
}

interface FilterChipProps {
  selected: boolean;
  onClick: () => void;
  children: ReactNode;
}

/** Chip de filtro: sí se pulsa. Seleccionado, en negro y con la equis para quitarlo. */
export function FilterChip({ selected, onClick, children }: FilterChipProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={selected}
      className={`inline-flex min-h-9 items-center gap-2 rounded-pill border px-3 text-small transition-colors ${
        selected
          ? "border-ink bg-ink font-semibold text-white"
          : "border-line text-ink-2 hover:bg-[color-mix(in_oklab,var(--af-ink)_5%,transparent)]"
      }`}
    >
      {children}
      {selected && <CloseIcon size={14} />}
    </button>
  );
}
