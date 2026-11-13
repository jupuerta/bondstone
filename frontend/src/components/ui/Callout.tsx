import type { ReactNode } from "react";

interface CalloutProps {
  label: string;
  children: ReactNode;
  compact?: boolean;
  className?: string;
}

/** El recuadro con tinte: la única pieza para todo lo que es aviso. */
export function Callout({ label, children, compact = false, className = "" }: CalloutProps) {
  return (
    <aside className={`flex flex-col gap-3 rounded-card border border-accent-line bg-accent-tint ${compact ? "px-4 py-3.5" : "px-[22px] py-5"} ${className}`}>
      <span className="eyebrow eyebrow-accent">{label}</span>
      {children}
    </aside>
  );
}

export function CalloutRow({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <div className="flex items-start gap-3 text-accent">
      <span className="mt-px shrink-0">{icon}</span>
      <p className="text-[15px] leading-normal text-ink">{children}</p>
    </div>
  );
}
