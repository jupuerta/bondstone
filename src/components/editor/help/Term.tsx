import { useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { mechanismHref } from "../../../domain/mechanisms";
import { useT } from "../../../i18n";
import { PatternDiagram } from "../patterns/PatternDiagram";
import { DOCS_WINDOW } from "../shell/docsWindow";
import { GLOSSARY, termDefinition, termLabel, type TermId } from "./glossary";
import { HelpSheet } from "./HelpSheet";
import { TERM_CARD_MAX_HEIGHT, TERM_CARD_WIDTH, placeTermCard, type HelpPlacement } from "./placement";
import { RuleProse } from "./prose";
import { APPEAR_MS, CLOSE_MS, GRACE_MS } from "./useComponentHelp";

interface TermProps {
  id: TermId;
  children?: ReactNode;
}

export function Term({ id, children }: TermProps) {
  const t = useT();
  const word = useRef<HTMLSpanElement>(null);
  const timer = useRef<number | undefined>(undefined);
  const [opened, setOpened] = useState<{ anchor: DOMRect; panel: DOMRect | undefined } | null>(null);

  const clear = useCallback(() => window.clearTimeout(timer.current), []);
  useEffect(() => clear, [clear]);

  useEffect(() => {
    if (opened === null) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      clear();
      setOpened(null);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [opened, clear]);

  const after = useCallback(
    (ms: number, next: "open" | "close") => {
      clear();
      timer.current = window.setTimeout(() => {
        const here = word.current;
        setOpened(next === "open" && here !== null ? { anchor: here.getBoundingClientRect(), panel: panelOf(here) } : null);
      }, ms);
    },
    [clear],
  );

  const cardId = `term-${id}`;

  return (
    <>
      <span
        ref={word}
        tabIndex={0}
        aria-describedby={opened !== null ? cardId : undefined}
        onMouseEnter={() => after(APPEAR_MS, "open")}
        onMouseLeave={() => after(GRACE_MS, "close")}
        onFocus={() => after(0, "open")}
        onBlur={() => after(0, "close")}
        className="cursor-help rounded-[2px] underline decoration-panel-ink-3 decoration-dotted underline-offset-[3px] hover:decoration-panel-ink"
      >
        {children ?? t(termLabel(id))}
      </span>
      {opened !== null && createPortal(<TermCard id={id} cardId={cardId} anchor={opened.anchor} panel={opened.panel} onMouseEnter={clear} onMouseLeave={() => after(CLOSE_MS, "close")} />, document.body)}
    </>
  );
}

interface TermCardProps {
  id: TermId;
  cardId: string;
  anchor: DOMRect;
  panel: DOMRect | undefined;
  onMouseEnter: () => void;
  onMouseLeave: () => void;
}

function TermCard({ id, cardId, anchor, panel, onMouseEnter, onMouseLeave }: TermCardProps) {
  const t = useT();
  const { pattern, mechanism } = GLOSSARY[id];
  const card = useRef<HTMLDivElement>(null);
  const [placement, setPlacement] = useState<HelpPlacement>({ top: anchor.top, left: anchor.left });

  useLayoutEffect(() => {
    const height = card.current?.getBoundingClientRect().height ?? 0;
    setPlacement(placeTermCard(anchor, { width: window.innerWidth, height: window.innerHeight }, height, panel));
  }, [anchor, panel]);

  const style: CSSProperties = { ...placement, width: Math.min(TERM_CARD_WIDTH, window.innerWidth - 32), maxHeight: TERM_CARD_MAX_HEIGHT };

  return (
    <HelpSheet
      ref={card}
      id={cardId}
      anchored="fixed"
      style={style}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      header={
        <span className="min-w-0">
          <span className="eyebrow block text-panel-ink-3">{t("ui.help.glossary")}</span>
          <span className="block text-[13px] font-semibold text-panel-ink">{t(termLabel(id))}</span>
        </span>
      }
    >
      <RuleProse className="text-[12px] leading-[1.5] text-panel-ink-2" text={t(termDefinition(id))} />
      {pattern !== undefined && <PatternDiagram pattern={pattern} width={176} className="mt-2.5" />}
      {mechanism !== undefined && (
        <a href={mechanismHref(mechanism)} target={DOCS_WINDOW} className="mt-2.5 inline-flex text-[12px] leading-[1.45] text-accent-panel underline underline-offset-4 hover:text-panel-ink">
          {t("ui.term.readMore")}
        </a>
      )}
    </HelpSheet>
  );
}

function panelOf(word: HTMLElement): DOMRect | undefined {
  return word.closest("[data-help-panel]")?.getBoundingClientRect();
}
