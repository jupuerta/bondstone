import type { ReactNode } from "react";

interface FieldProps {
  label: string;
  children: ReactNode;
  disabled?: boolean;
}

export function Field({ label, children, disabled = false }: FieldProps) {
  return (
    <div className="flex flex-col gap-1">
      <span className="eyebrow text-panel-ink-3">{label}</span>
      <div className={`flex min-h-[30px] items-center rounded-node border px-2.5 text-[13px] ${disabled ? "border-dashed border-panel-line text-panel-ink-3" : "border-panel-line bg-panel-2 text-panel-ink"}`}>
        {children}
      </div>
    </div>
  );
}
