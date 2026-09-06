import { Link } from "react-router-dom";
import { useT } from "../../../i18n";
import { LanguageToggle } from "../../layout/LanguageToggle";
import { Button } from "../../ui/Button";
import { Chip } from "../../ui/Chip";
import { LogoMark, MenuIcon } from "../../ui/Icons";
import { ReferenceMenu } from "./ReferenceMenu";

export interface BarChip {
  key: string;
  label: string;
  variant?: "panel" | "critical";
}

interface EditorBarProps {
  name: string;
  shareToken: string;
  chips: BarChip[];
  narrow: boolean;
  onOpenCopy: () => void;
  copying: boolean;
  copyError: string | null;
  onShare: () => void;
  shared: boolean;
  onOpenMenu: () => void;
  menuOpen: boolean;
}

export function EditorBar({ name, shareToken, chips, narrow, onOpenCopy, copying, copyError, onShare, shared, onOpenMenu, menuOpen }: EditorBarProps) {
  const t = useT();

  if (narrow) {
    return (
      <header className="flex flex-col border-b border-panel-line bg-panel-2">
        <div className="flex h-12 items-center gap-2.5 px-3">
          <Link to="/" aria-label={t("editor.bar.home")} className="flex shrink-0 items-center text-panel-ink">
            <LogoMark size={20} />
          </Link>
          <span className="min-w-0 flex-1 truncate text-[13px] font-semibold text-panel-ink">{name}</span>
          <button
            type="button"
            onClick={onOpenMenu}
            aria-label={t("web.a11y.openMenu")}
            aria-expanded={menuOpen}
            aria-controls="editor-menu"
            className="inline-flex size-11 shrink-0 items-center justify-center rounded-pill text-panel-ink"
          >
            <MenuIcon size={22} />
          </button>
        </div>
        {chips.length > 0 && (
          <div className="flex gap-1.5 overflow-x-auto px-3 pb-2">
            {chips.map((chip) => (
              <Chip key={chip.key} variant={chip.variant ?? "panel"} className="shrink-0">
                {chip.label}
              </Chip>
            ))}
          </div>
        )}
      </header>
    );
  }

  return (
    <header className="flex h-14 shrink-0 items-center gap-4 border-b border-panel-line bg-panel-2 px-4">
      <div className="flex min-w-0 flex-1 items-center gap-3.5">
        <Link to="/" aria-label={t("editor.bar.home")} className="flex shrink-0 items-center gap-2 font-display text-[15px] font-semibold text-panel-ink no-underline">
          <LogoMark size={20} />
          ArchForge
        </Link>
        <span className="min-w-0 truncate text-[14px] font-semibold text-panel-ink">{name}</span>
        {chips.map((chip) => (
          <Chip key={chip.key} variant={chip.variant ?? "panel"} className="shrink-0">
            {chip.label}
          </Chip>
        ))}
      </div>
      <div className="flex shrink-0 items-center gap-3.5">
        {copyError && <span className="text-meta text-hot-panel">{copyError}</span>}
        <Button variant="panelPrimary" size="sm" onClick={onOpenCopy} loading={copying} loadingLabel={t("ui.editor.openingCopy")}>
          {t("ui.editor.openCopy")}
        </Button>
        <button type="button" onClick={onShare} className="inline-flex min-h-10 items-center rounded-pill px-2 text-[13px] text-panel-ink-2 hover:text-panel-ink">
          {shared ? t("ui.copied") : t("ui.share")}
        </button>
        <ReferenceMenu shareToken={shareToken} />
        <LanguageToggle dark />
      </div>
    </header>
  );
}
