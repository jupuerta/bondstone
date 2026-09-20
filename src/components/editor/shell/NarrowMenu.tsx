import { useEffect, useRef, type ReactNode } from "react";
import { useT } from "../../../i18n";
import { useEscape } from "../../../hooks/useEscape";
import { AppearanceSegmented } from "../../layout/AppearanceToggle";
import { LanguageToggle } from "../../layout/LanguageToggle";
import { Button } from "../../ui/Button";
import { CloseIcon, LogoMark } from "../../ui/Icons";
import { DOCS_WINDOW, docsLinks } from "./docsWindow";

interface NarrowMenuProps {
  shareToken: string;
  onClose: () => void;
  primary?: ReactNode;
  onShare: () => void;
  shared: boolean;
  onExport?: () => void;
}

export function NarrowMenu({ shareToken, onClose, primary, onShare, shared, onExport }: NarrowMenuProps) {
  const t = useT();
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeRef.current?.focus();
  }, []);

  useEscape(onClose);

  const row = "flex min-h-[52px] items-center text-[16px] text-panel-ink no-underline";

  return (
    <div id="editor-menu" role="dialog" aria-modal="true" aria-label={t("web.nav.menu")} className="panel fixed inset-0 z-50 flex flex-col bg-panel text-panel-ink">
      <div className="flex h-12 items-center justify-between border-b border-panel-line px-3">
        <span className="flex items-center gap-2 font-display text-[15px] font-semibold">
          <LogoMark size={20} />
          Bondstone
        </span>
        <button ref={closeRef} type="button" onClick={onClose} aria-label={t("web.a11y.closeMenu")} className="inline-flex size-11 items-center justify-center rounded-pill">
          <CloseIcon size={22} />
        </button>
      </div>
      <div className="flex flex-col gap-3 px-3 py-4">
        {primary}
        <Button variant="panel" size="md" onClick={onShare} className="w-full">
          {shared ? t("ui.copied") : t("ui.share")}
        </Button>
        {onExport && (
          <Button
            variant="panel"
            size="md"
            onClick={() => {
              onExport();
              onClose();
            }}
            className="w-full"
          >
            {t("ui.editor.export")}
          </Button>
        )}
      </div>
      <nav aria-label={t("editor.docs")} className="flex flex-col border-t border-panel-line px-3">
        <span className="eyebrow pt-4 pb-1 text-panel-ink-3">{t("editor.bar.reference")}</span>
        {docsLinks(shareToken).map((link) => (
          <a key={link.key} href={link.href} target={DOCS_WINDOW} onClick={onClose} className={`${row} border-t border-panel-line first-of-type:border-t-0`}>
            {t(link.key)}
          </a>
        ))}
      </nav>
      <div className="mt-auto flex flex-col border-t border-panel-line">
        <div className="flex items-center justify-between px-3 py-4">
          <span className="eyebrow text-panel-ink-3">{t("web.nav.language")}</span>
          <LanguageToggle dark />
        </div>
        <div className="flex items-center justify-between border-t border-panel-line px-3 py-4">
          <span className="eyebrow text-panel-ink-3">{t("editor.theme.label")}</span>
          <AppearanceSegmented surface="editor" dark />
        </div>
      </div>
    </div>
  );
}
