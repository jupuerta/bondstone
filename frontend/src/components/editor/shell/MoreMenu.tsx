import { useEffect, useRef, useState } from "react";
import { useT } from "../../../i18n";

/**
 * El desplegable de las acciones que no son de todos los días. Hoy solo lleva una, y es la razón
 * de que exista: eliminar no puede estar al lado de «Compartir» y «Exportar», donde se pulsa sin
 * mirar, pero tampoco escondido — la página de privacidad promete que se puede borrar desde aquí.
 *
 * Mismo comportamiento que `ReferenceMenu`, del que copia el patrón: se cierra al pulsar fuera y
 * con Escape.
 */
export function MoreMenu({ onDelete }: { onDelete: () => void }) {
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
        aria-label={t("ui.editor.more")}
        className="inline-flex size-10 items-center justify-center rounded-pill text-panel-ink-2 hover:text-panel-ink"
      >
        <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden>
          <circle cx="3" cy="8" r="1.5" />
          <circle cx="8" cy="8" r="1.5" />
          <circle cx="13" cy="8" r="1.5" />
        </svg>
      </button>
      {open && (
        <div role="menu" className="absolute top-full right-0 z-30 mt-1 flex w-[260px] flex-col rounded-card border border-panel-line-2 bg-panel-2 p-1.5">
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setOpen(false);
              onDelete();
            }}
            className="rounded-node px-3 py-2.5 text-left text-small text-hot-panel hover:bg-wash/8"
          >
            {t("editor.delete.action")}
          </button>
        </div>
      )}
    </div>
  );
}
