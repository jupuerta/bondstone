import type { DragEvent, FocusEvent, MouseEvent } from "react";
import { useT } from "../../../i18n";
import type { NodeType } from "../../../types/architecture";
import { HELP_CARD_ID } from "../help/ComponentCard";
import { DND_NODE_TYPE_FORMAT, NODE_GROUPS } from "./nodeMeta";
import { TypeIcon } from "./TypeIcons";

export interface PaletteHelp {
  type: NodeType | null;
  onEnter: (type: NodeType, anchorTop: number) => void;
  onFocus: (type: NodeType, anchorTop: number) => void;
  onLeave: () => void;
  onClose: () => void;
}

interface PaletteProps {
  collapsed: boolean;
  onToggle: () => void;
  help?: PaletteHelp;
}

const TOTAL = NODE_GROUPS.reduce((total, group) => total + group.types.length, 0);

export function Palette({ collapsed, onToggle, help }: PaletteProps) {
  const t = useT();

  const startDrag = (event: DragEvent<HTMLElement>, type: NodeType) => {
    event.dataTransfer.setData(DND_NODE_TYPE_FORMAT, type);
    event.dataTransfer.effectAllowed = "move";
    help?.onClose();
  };

  const handlers = (type: NodeType) =>
    help
      ? {
          tabIndex: 0,
          "aria-describedby": help.type === type ? HELP_CARD_ID : undefined,
          onMouseEnter: (event: MouseEvent<HTMLElement>) => help.onEnter(type, event.currentTarget.getBoundingClientRect().top),
          onMouseLeave: help.onLeave,
          onFocus: (event: FocusEvent<HTMLElement>) => {
            if (event.currentTarget.matches(":focus-visible")) help.onFocus(type, event.currentTarget.getBoundingClientRect().top);
          },
          onBlur: help.onLeave,
        }
      : { tabIndex: 0 };

  if (collapsed) {
    return (
      <div className="flex w-12 shrink-0 flex-col items-center border-r border-panel-line bg-panel">
        <div className="flex h-11 w-full shrink-0 items-center justify-center border-b border-panel-line">
          <button type="button" onClick={onToggle} aria-label={t("ui.palette.expand")} title={t("ui.palette.expand")} className="inline-flex size-8 items-center justify-center rounded-pill text-panel-ink-2 hover:bg-wash/8 hover:text-panel-ink">
            <Chevron open={false} />
          </button>
        </div>
        <div className="flex min-h-0 flex-1 flex-col items-center gap-0.5 overflow-y-auto py-2">
          {NODE_GROUPS.map((group, index) => (
            <div key={group.label} className="flex flex-col items-center gap-0.5">
              {index > 0 && <span aria-hidden className="my-1.5 h-px w-5 bg-panel-line" />}
              {group.types.map((type) => (
                <span
                  key={type}
                  draggable
                  aria-label={t(`ui.node.${type}`)}
                  title={t(`ui.node.${type}`)}
                  onDragStart={(event) => startDrag(event, type)}
                  {...handlers(type)}
                  className={`flex size-8 cursor-grab items-center justify-center rounded-node border active:cursor-grabbing ${help?.type === type ? "border-accent-panel bg-panel-2" : "border-transparent hover:border-panel-line hover:bg-panel-2"}`}
                >
                  <TypeIcon type={type} size={15} />
                </span>
              ))}
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="flex w-full flex-col">
      <div className="flex h-11 shrink-0 items-center justify-between border-b border-panel-line px-3">
        <span className="font-display text-panel font-semibold text-panel-ink">{t("ui.palette.title")}</span>
        <span className="flex items-center gap-2">
          <span className="font-mono text-label text-panel-ink-3">{TOTAL}</span>
          <button type="button" onClick={onToggle} aria-label={t("ui.palette.collapse")} title={t("ui.palette.collapse")} className="inline-flex size-8 items-center justify-center rounded-pill text-panel-ink-2 hover:bg-wash/8 hover:text-panel-ink">
            <Chevron open />
          </button>
        </span>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto py-2">
        {NODE_GROUPS.map((group) => (
          <div key={group.label} className="pb-1.5">
            <div className="px-3 pt-2 pb-1 font-mono text-label uppercase tracking-[0.1em] text-panel-ink-3">{t(group.label)}</div>
            {group.types.map((type) => (
              <div
                key={type}
                draggable
                onDragStart={(event) => startDrag(event, type)}
                className={`group mx-2 flex cursor-grab items-center gap-2.5 rounded-node border px-2 py-1.5 active:cursor-grabbing ${help?.type === type ? "border-accent-panel bg-panel-2" : "border-transparent hover:border-panel-line hover:bg-panel-2"}`}
              >
                <span {...handlers(type)} aria-label={t(`ui.node.${type}`)} className="flex size-6 shrink-0 items-center justify-center rounded-node bg-panel-3 outline-offset-2">
                  <TypeIcon type={type} size={14} />
                </span>
                <span className="min-w-0 truncate text-[13px] text-panel-ink">{t(`ui.node.${type}`)}</span>
                <span aria-hidden className="ml-auto font-mono text-[10px] text-panel-ink-3 opacity-0 transition-opacity group-hover:opacity-100">
                  ⋮⋮
                </span>
              </div>
            ))}
          </div>
        ))}
      </div>
      <p className="m-3 rounded-node border border-dashed border-panel-line px-2.5 py-2 text-meta leading-relaxed text-panel-ink-3">
        {t("ui.palette.hintDrag")} {help && t("ui.palette.hintHelp")} <kbd className="rounded border border-panel-line bg-panel-3 px-1 font-mono text-[10px] text-panel-ink-2">{t("ui.key.delete")}</kbd> {t("ui.palette.hintDelete")}
      </p>
    </div>
  );
}

function Chevron({ open }: { open: boolean }) {
  return (
    <svg width="12" height="12" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      {open ? <path d="M7.5 2l-4 4 4 4" /> : <path d="M4.5 2l4 4-4 4" />}
    </svg>
  );
}
