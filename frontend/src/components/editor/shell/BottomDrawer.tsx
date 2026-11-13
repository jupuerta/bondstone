import type { ReactNode } from "react";
import { useT } from "../../../i18n";

interface BottomDrawerProps {
  title: string;
  aside?: ReactNode;
  open: boolean;
  onToggle: () => void;
  children: ReactNode;
}

export function BottomDrawer({ title, aside, open, onToggle, children }: BottomDrawerProps) {
  const t = useT();

  return (
    <div className="flex shrink-0 flex-col border-t border-panel-line bg-panel-2">
      <div className="flex h-[52px] items-center gap-2.5 px-3">
        <strong className="min-w-0 flex-1 truncate text-[13px] text-panel-ink">{title}</strong>
        {aside}
        <button
          type="button"
          onClick={onToggle}
          aria-expanded={open}
          aria-controls="editor-drawer"
          aria-label={open ? t("editor.drawer.close") : t("editor.drawer.open")}
          className="inline-flex size-11 shrink-0 items-center justify-center rounded-pill text-panel-ink-2"
        >
          <svg width="12" height="12" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" aria-hidden>
            {open ? <path d="M2 4.5l4 4 4-4" /> : <path d="M2 7.5l4-4 4 4" />}
          </svg>
        </button>
      </div>
      <div id="editor-drawer" hidden={!open} className="max-h-[42dvh] overflow-y-auto border-t border-panel-line bg-panel p-3">
        {children}
      </div>
    </div>
  );
}
