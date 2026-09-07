import { useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import { useT } from "../../../i18n";
import type { Area } from "../../../types/architecture";
import { lensQuestion, type LensTab, type LensView } from "./lens";
import { TrafficDial, type TrafficDialProps } from "./TrafficDial";

export interface StripLens {
  tabs: LensTab[];
  view: LensView;
  onChange: (area: Area | null) => void;
}

export interface ViewStripProps {
  narrow: boolean;
  drawn: boolean;
  lens?: StripLens;
  badges?: { on: boolean; onToggle: () => void };
  legend: { open: boolean; onToggle: () => void; label?: string };
  dial?: TrafficDialProps & { onOpen: () => void };
  wave?: { playing: boolean; onReplay: () => void; onSkip: () => void };
  aside?: ReactNode;
}

const CHIP = "inline-flex shrink-0 items-center gap-1.5 rounded-pill border px-3 text-[13px] whitespace-nowrap text-panel-ink transition-[color,background-color,border-color] disabled:cursor-default disabled:opacity-[.36]";
const OFF = "border-panel-line hover:border-panel-line-2 disabled:hover:border-panel-line";
const ON = "border-[color-mix(in_oklab,var(--af-accent-panel)_45%,var(--af-panel))] bg-[color-mix(in_oklab,var(--af-accent-panel)_18%,var(--af-panel))]";

export function chipClass(on: boolean, narrow: boolean): string {
  return `${CHIP} ${on ? ON : OFF} ${narrow ? "min-h-11 px-3.5" : "h-[30px]"}`;
}

export function ViewStrip({ narrow, drawn, lens, badges, legend, dial, wave, aside }: ViewStripProps) {
  const t = useT();
  const [open, setOpen] = useState<"lens" | "dial" | null>(null);
  const box = useRef<HTMLDivElement>(null);
  const popover = useRef<HTMLDivElement>(null);
  const lensButton = useRef<HTMLButtonElement>(null);
  const dialButton = useRef<HTMLButtonElement>(null);
  const triggerOf = (which: "lens" | "dial") => (which === "lens" ? lensButton : dialButton).current;
  const items = () => [...(popover.current?.querySelectorAll<HTMLElement>('[role="menuitemradio"]') ?? [])];

  useEffect(() => {
    if (open === null) return;
    const dismiss = () => {
      if (open === "dial") dial?.onClose();
      triggerOf(open)?.focus();
      setOpen(null);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") dismiss();
    };
    const onPointer = (event: PointerEvent) => {
      if (box.current && !box.current.contains(event.target as globalThis.Node)) dismiss();
    };
    window.addEventListener("keydown", onKey);
    window.addEventListener("pointerdown", onPointer);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("pointerdown", onPointer);
    };
  }, [open, dial]);

  useEffect(() => {
    if (!drawn) setOpen(null);
  }, [drawn]);

  useEffect(() => {
    if (open === null) return;
    const frame = requestAnimationFrame(() => {
      if (open === "lens") {
        const all = items();
        (all.find((item) => item.getAttribute("aria-checked") === "true") ?? all[0])?.focus();
        return;
      }
      const slider = popover.current?.querySelector<HTMLInputElement>("input[type=range]");
      const active = document.activeElement;
      const settled = active === null || active === document.body || active === popover.current || active === dialButton.current;
      if (slider && !slider.disabled) {
        if (settled) slider.focus();
        return;
      }
      if (settled) popover.current?.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [open, dial?.curve]);

  const toggleLens = () => setOpen((current) => (current === "lens" ? null : "lens"));
  const closeDial = () => {
    setOpen(null);
    dial?.onClose();
  };
  const toggleDial = () => {
    if (open === "dial") {
      closeDial();
      return;
    }
    setOpen("dial");
    dial?.onOpen();
  };
  const onMenuKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Tab") {
      lensButton.current?.focus();
      setOpen(null);
      return;
    }
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const all = items();
    const at = all.indexOf(document.activeElement as HTMLElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? all.length - 1 : event.key === "ArrowDown" ? (at + 1) % all.length : (at - 1 + all.length) % all.length;
    all[next]?.focus();
  };
  const pick = (area: Area | null) => {
    lens?.onChange(area);
    lensButton.current?.focus();
    setOpen(null);
  };
  const lensLabel = lens ? (lens.view.area === null ? t("ui.lens.all") : t(`ui.area.${lens.view.area}`)) : "";

  return (
    <div ref={box} className="relative z-30 shrink-0 border-b border-panel-line bg-panel-3">
      <div className={`flex items-center gap-2 px-3 ${narrow ? "min-h-14 overflow-x-auto py-1.5" : "h-10"}`}>
      {lens && (
        <button
          ref={lensButton}
          type="button"
          disabled={!drawn}
          onClick={toggleLens}
          aria-expanded={open === "lens"}
          aria-haspopup="menu"
          title={lens.view.area !== null && lens.view.note ? `${t(lensQuestion(lens.view.area))} · ${t(lens.view.note, { count: lens.view.findings })}` : t("ui.lens.allNote")}
          className={chipClass(lens.view.area !== null, narrow)}
        >
          {narrow ? lensLabel : `${t("ui.lens.title")}: ${lensLabel}`}
          <Caret />
        </button>
      )}

      {badges && (
        <button type="button" disabled={!drawn} onClick={badges.onToggle} aria-pressed={badges.on} className={chipClass(badges.on, narrow)}>
          {t("ui.editor.badges")}
        </button>
      )}

      <button type="button" disabled={!drawn} onClick={legend.onToggle} aria-pressed={legend.open} title={t("ui.legend.open")} className={chipClass(legend.open, narrow)}>
        {legend.label ?? t("ui.legend.title")}
      </button>

      {wave && narrow && <WaveChip wave={wave} narrow />}

      {dial && (
        <button ref={dialButton} type="button" disabled={!drawn} onClick={toggleDial} aria-expanded={open === "dial"} aria-haspopup="dialog" className={chipClass(open === "dial" || dial.scale > 1, narrow)}>
          {dial.scale > 1 ? `${t("ui.dial.title")} ${t("ui.dial.scale", { scale: dial.scale })}` : t("ui.dial.ask")}
          <Caret />
        </button>
      )}

      {!narrow && (
        <span className="ml-auto flex shrink-0 items-center gap-2">
          {wave && <WaveChip wave={wave} narrow={false} />}
          {aside}
        </span>
      )}
      </div>

      {open === "lens" && lens && (
        <div ref={popover} role="menu" aria-label={t("ui.lens.title")} onKeyDown={onMenuKeyDown} className="absolute top-full left-3 mt-1.5 flex w-[300px] max-w-[calc(100vw-24px)] flex-col gap-0.5 rounded-panel border border-panel-line-2 bg-panel-2 p-1.5 shadow-[0_24px_48px_-24px_rgba(0,0,0,.8)]">
          {lens.tabs.map((tab) => {
            const current = tab.area === lens.view.area;
            return (
              <button
                key={tab.area ?? "all"}
                type="button"
                role="menuitemradio"
                aria-checked={current}
                onClick={() => pick(tab.area)}
                className={`flex w-full flex-col gap-0.5 rounded-card px-2.5 py-2 text-left hover:bg-wash/8 ${current ? "bg-wash/10" : ""} ${narrow ? "min-h-11" : ""}`}
              >
                <span className="flex items-baseline justify-between gap-2">
                  <span className="text-[13px] font-semibold text-panel-ink">{t(tab.label)}</span>
                  <span className="num text-[11px] text-panel-ink-3">{tab.score ?? "—"}</span>
                </span>
                <span className="text-[12px] leading-[1.4] text-panel-ink-3">{tab.area === null ? t("ui.lens.allNote") : t(lensQuestion(tab.area))}</span>
              </button>
            );
          })}
        </div>
      )}

      {open === "dial" && dial && (
        <div ref={popover} role="dialog" aria-label={t("ui.dial.title")} tabIndex={-1} className={`absolute top-full left-3 mt-1.5 w-[calc(100vw-24px)] rounded-panel border border-panel-line-2 bg-panel-2 p-3.5 shadow-[0_24px_48px_-24px_rgba(0,0,0,.8)] ${narrow ? "" : "max-w-[640px]"}`}>
          <TrafficDial {...dial} narrow={narrow} onClose={closeDial} />
        </div>
      )}
    </div>
  );
}

function WaveChip({ wave, narrow }: { wave: NonNullable<ViewStripProps["wave"]>; narrow: boolean }) {
  const t = useT();
  return (
    <button type="button" onClick={wave.playing ? wave.onSkip : wave.onReplay} className={chipClass(false, narrow)}>
      <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden className="shrink-0">
        {wave.playing ? (
          <>
            <path d="M6 5.5l8 6.5-8 6.5z" />
            <path d="M18 5.5v13" />
          </>
        ) : (
          <>
            <path d="M4 12a8 8 0 1 1 2.6 5.9" />
            <path d="M4 8v4.4h4.4" />
          </>
        )}
      </svg>
      {t(wave.playing ? "editor.wave.skip" : "editor.wave.replay")}
    </button>
  );
}

function Caret() {
  return (
    <svg width="10" height="10" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" aria-hidden className="shrink-0">
      <path d="M2 4.5l4 4 4-4" />
    </svg>
  );
}
