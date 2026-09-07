import { useEffect, useRef, useState } from "react";
import { useT } from "../../../i18n";
import { DOCS_WINDOW, docsLinks } from "./docsWindow";

export function ReferenceMenu({ shareToken }: { shareToken: string }) {
  const t = useT();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    window.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  return (
    <div ref={ref} className="relative shrink-0">
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={t("editor.docs")}
        className="inline-flex min-h-10 items-center gap-1.5 rounded-pill px-2 text-[13px] text-panel-ink-2 hover:text-panel-ink"
      >
        {t("editor.bar.reference")}
        <svg width="10" height="10" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" aria-hidden>
          <path d="M2 4.5l4 4 4-4" />
        </svg>
      </button>
      {open && (
        <div role="menu" className="absolute top-full right-0 z-30 mt-1 flex w-[300px] flex-col rounded-card border border-panel-line-2 bg-panel-2 p-1.5">
          {docsLinks(shareToken).map((link) => (
            <a
              key={link.key}
              role="menuitem"
              href={link.href}
              target={DOCS_WINDOW}
              onClick={() => setOpen(false)}
              className="rounded-node px-3 py-2.5 text-small text-panel-ink no-underline hover:bg-wash/8"
            >
              {t(link.key)}
            </a>
          ))}
        </div>
      )}
    </div>
  );
}
