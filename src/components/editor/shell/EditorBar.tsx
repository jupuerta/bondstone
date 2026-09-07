import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useT } from "../../../i18n";
import { LanguageToggle } from "../../layout/LanguageToggle";
import { Chip } from "../../ui/Chip";
import { LogoMark, MenuIcon, MoonIcon, SunIcon } from "../../ui/Icons";
import type { EditorTheme } from "../../../domain/editorPrefs";
import { ReferenceMenu } from "./ReferenceMenu";

export interface BarChip {
  key: string;
  label: string;
  variant?: "panel" | "critical";
}

interface EditorBarProps {
  name: string;
  onRename?: (name: string) => void;
  shareToken: string;
  chips: BarChip[];
  narrow: boolean;
  status?: ReactNode;
  mode?: ReactNode;
  primary?: ReactNode;
  notice?: string | null;
  onShare: () => void;
  shared: boolean;
  onExport?: () => void;
  onOpenMenu: () => void;
  menuOpen: boolean;
  theme?: EditorTheme;
  onToggleTheme?: () => void;
}

const NAME_MAX_LENGTH = 200;

export function EditorBar({ name, onRename, shareToken, chips, narrow, status, mode, primary, notice, onShare, shared, onExport, onOpenMenu, menuOpen, theme = "dark", onToggleTheme }: EditorBarProps) {
  const t = useT();

  if (narrow) {
    return (
      <header className="flex flex-col border-b border-panel-line bg-panel-2">
        <div className="flex h-12 items-center gap-2.5 px-3">
          <Link to="/" aria-label={t("editor.bar.home")} className="flex shrink-0 items-center text-panel-ink">
            <LogoMark size={20} />
          </Link>
          <span className="min-w-0 flex-1 truncate text-[13px] font-semibold text-panel-ink">{name}</span>
          {status}
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
        {mode && <div className="flex h-[52px] items-center border-t border-panel-line px-3">{mode}</div>}
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
        {onRename ? <NameField name={name} onRename={onRename} /> : <span className="min-w-0 truncate text-[14px] font-semibold text-panel-ink">{name}</span>}
        {chips.map((chip) => (
          <Chip key={chip.key} variant={chip.variant ?? "panel"} className="shrink-0">
            {chip.label}
          </Chip>
        ))}
      </div>
      {mode && <div className="flex shrink-0 items-center">{mode}</div>}
      <div className={`flex shrink-0 items-center gap-3.5 ${mode ? "flex-1 justify-end" : ""}`}>
        {notice && <span className="text-meta text-hot-panel">{notice}</span>}
        {status}
        {primary}
        <button type="button" onClick={onShare} className="inline-flex min-h-10 items-center rounded-pill px-2 text-[13px] text-panel-ink-2 hover:text-panel-ink">
          {shared ? t("ui.copied") : t("ui.share")}
        </button>
        {onExport && (
          <button type="button" onClick={onExport} className="inline-flex min-h-10 items-center rounded-pill px-2 text-[13px] text-panel-ink-2 hover:text-panel-ink">
            {t("ui.editor.export")}
          </button>
        )}
        <ReferenceMenu shareToken={shareToken} />
        <LanguageToggle dark />
        {onToggleTheme && (
          <button
            type="button"
            onClick={onToggleTheme}
            aria-label={t(theme === "light" ? "editor.theme.toDark" : "editor.theme.toLight")}
            title={t(theme === "light" ? "editor.theme.toDark" : "editor.theme.toLight")}
            className="inline-flex size-8 shrink-0 items-center justify-center rounded-pill border border-panel-line text-panel-ink-2 hover:border-panel-line-2 hover:text-panel-ink"
          >
            {theme === "light" ? <MoonIcon size={16} /> : <SunIcon size={16} />}
          </button>
        )}
      </div>
    </header>
  );
}

function NameField({ name, onRename }: { name: string; onRename: (name: string) => void }) {
  const t = useT();
  const [draft, setDraft] = useState(name);
  const editing = useRef(false);

  useEffect(() => {
    if (!editing.current) setDraft(name);
  }, [name]);

  return (
    <input
      type="text"
      value={draft}
      maxLength={NAME_MAX_LENGTH}
      aria-label={t("ui.canvas.architectureName")}
      onFocus={() => {
        editing.current = true;
      }}
      onChange={(event) => {
        setDraft(event.target.value);
        if (event.target.value.trim() !== "") onRename(event.target.value);
      }}
      onBlur={() => {
        editing.current = false;
        if (draft.trim() === "") setDraft(name);
      }}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === "Escape") event.currentTarget.blur();
      }}
      className="min-w-0 max-w-[320px] flex-1 truncate rounded-node border border-transparent bg-transparent px-2 py-1 text-[14px] font-semibold text-panel-ink outline-none hover:border-panel-line focus-visible:border-accent-panel"
    />
  );
}
