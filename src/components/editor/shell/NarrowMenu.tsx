import { useEffect, useRef } from "react";
import { useT } from "../../../i18n";
import { LanguageToggle } from "../../layout/LanguageToggle";
import { Button } from "../../ui/Button";
import { CloseIcon, LogoMark } from "../../ui/Icons";
import { DOCS_WINDOW, docsLinks } from "./docsWindow";

interface NarrowMenuProps {
  shareToken: string;
  onClose: () => void;
  onOpenCopy: () => void;
  copying: boolean;
  onShare: () => void;
  shared: boolean;
}

export function NarrowMenu({ shareToken, onClose, onOpenCopy, copying, onShare, shared }: NarrowMenuProps) {
  const t = useT();
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeRef.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const row = "flex min-h-[52px] items-center text-[16px] text-panel-ink no-underline";

  return (
    <div id="editor-menu" role="dialog" aria-modal="true" aria-label={t("web.nav.menu")} className="panel fixed inset-0 z-50 flex flex-col bg-panel text-panel-ink">
      <div className="flex h-12 items-center justify-between border-b border-panel-line px-3">
        <span className="flex items-center gap-2 font-display text-[15px] font-semibold">
          <LogoMark size={20} />
          ArchForge
        </span>
        <button ref={closeRef} type="button" onClick={onClose} aria-label={t("web.a11y.closeMenu")} className="inline-flex size-11 items-center justify-center rounded-pill">
          <CloseIcon size={22} />
        </button>
      </div>
      <div className="flex flex-col gap-3 px-3 py-4">
        <Button variant="panelPrimary" size="md" onClick={onOpenCopy} loading={copying} loadingLabel={t("ui.editor.openingCopy")} className="w-full">
          {t("ui.editor.openCopy")}
        </Button>
        <Button variant="panel" size="md" onClick={onShare} className="w-full">
          {shared ? t("ui.copied") : t("ui.share")}
        </Button>
      </div>
      <nav aria-label={t("editor.docs")} className="flex flex-col border-t border-panel-line px-3">
        <span className="eyebrow pt-4 pb-1 text-panel-ink-3">{t("editor.bar.reference")}</span>
        {docsLinks(shareToken).map((link) => (
          <a key={link.key} href={link.href} target={DOCS_WINDOW} onClick={onClose} className={`${row} border-t border-panel-line first-of-type:border-t-0`}>
            {t(link.key)}
          </a>
        ))}
      </nav>
      <div className="mt-auto flex items-center justify-between border-t border-panel-line px-3 py-4">
        <span className="eyebrow text-panel-ink-3">{t("web.nav.language")}</span>
        <LanguageToggle dark />
      </div>
    </div>
  );
}
