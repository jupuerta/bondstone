export function blastTone(value: number): string {
  return value === 0 ? "var(--af-ok-panel)" : value > 0.5 ? "var(--af-hot-panel)" : "var(--af-warm-panel)";
}

export const LINK = "self-start text-[12px] leading-[1.45] text-accent-panel underline underline-offset-4 hover:text-panel-ink";
