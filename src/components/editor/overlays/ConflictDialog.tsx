import { useEffect, useRef } from "react";
import { useT } from "../../../i18n";
import { Button } from "../../ui/Button";

export function ConflictDialog({ onReload }: { onReload: () => void }) {
  const t = useT();
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const button = () => box.current?.querySelector("button");
    button()?.focus();
    const trap = (event: KeyboardEvent) => {
      if (event.key === "Tab") {
        event.preventDefault();
        button()?.focus();
      }
    };
    window.addEventListener("keydown", trap);
    return () => window.removeEventListener("keydown", trap);
  }, []);

  return (
    <div role="dialog" aria-modal="true" aria-labelledby="editor-conflict-title" className="fixed inset-0 z-50 flex items-center justify-center bg-canvas/80 p-6">
      <div ref={box} className="flex w-full max-w-[400px] flex-col gap-3 rounded-panel border border-panel-line-2 bg-panel-2 p-5 text-panel-ink">
        <h2 id="editor-conflict-title" className="font-display text-panel font-semibold text-panel-ink">
          {t("ui.conflict.title")}
        </h2>
        <p className="text-small text-panel-ink-2">{t("ui.conflict.body")}</p>
        <Button variant="panelPrimary" size="sm" onClick={onReload} className="mt-2 w-full">
          {t("ui.conflict.reload")}
        </Button>
      </div>
    </div>
  );
}
