import { useEffect, useRef, useState } from "react";
import { mechanismHref, mechanismLabel } from "../../../domain/mechanisms";
import { useT } from "../../../i18n";
import { DOCS_WINDOW } from "../shell/docsWindow";
import { CheckMark } from "./BriefBand";
import type { Difference } from "./graphDiff";
import type { Journal, Move } from "./journal";

const SHOWN = 4;

interface ChallengeJournalProps {
  journal: Journal;
  own: boolean;
  onCompare: () => void;
  onShare: () => void;
  shared: boolean;
  narrow: boolean;
}

export function ChallengeJournal({ journal, own, onCompare, onShare, shared, narrow }: ChallengeJournalProps) {
  const t = useT();
  const card = useRef<HTMLElement>(null);
  const [all, setAll] = useState(false);

  useEffect(() => {
    card.current?.scrollIntoView({ block: "nearest" });
  }, []);

  const shown = all ? journal.changes : journal.changes.slice(0, SHOWN);
  const hidden = journal.changes.length - shown.length;
  const link = `text-[12px] leading-[1.45] text-accent-panel underline underline-offset-4 hover:text-panel-ink ${narrow ? "min-h-11" : ""}`;

  return (
    <section ref={card} aria-label={t("ui.journal.title")} className="flex flex-col gap-1.5 rounded-card border border-dashed border-panel-line-2 bg-panel-2 p-3">
      <p className="flex items-center gap-1.5 text-[13px] font-semibold text-panel-ink">
        <CheckMark size={14} className="shrink-0 text-ok-panel" />
        {t("ui.challenge.passed")}
      </p>
      <p className="text-[12px] leading-[1.45] text-panel-ink-2">{t("editor.challenge.passedDetail")}</p>
      <span className="eyebrow mt-1 text-panel-ink-3">{t("ui.journal.title")}</span>

      {own && !journal.kept && <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.journal.nothingKept")}</p>}
      {own && journal.kept && journal.changes.length > 0 && (
        <>
          <p className="text-[12px] font-medium text-panel-ink">{t("ui.journal.youChanged")}</p>
          <ul className="flex flex-col gap-0.5">
            {shown.map((change, index) => (
              <Line key={index} change={change} />
            ))}
          </ul>
          {hidden > 0 && (
            <button type="button" onClick={() => setAll(true)} className={`self-start ${link}`}>
              {t("ui.journal.more", { count: hidden })}
            </button>
          )}
        </>
      )}

      {journal.moves.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {journal.moves.map((move) => (
            <MoveChip key={move.what === "score" ? "score" : move.area} move={move} />
          ))}
        </div>
      )}

      <p className="text-[12px] leading-[1.45] text-panel-ink">{t({ key: journal.lesson })}</p>
      {journal.mechanisms.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {journal.mechanisms.map((mechanism) => (
            <a
              key={mechanism}
              href={mechanismHref(mechanism)}
              target={DOCS_WINDOW}
              className={`inline-flex items-center rounded-pill border border-panel-line px-2.5 font-mono text-label tracking-[0.06em] text-panel-ink-2 uppercase no-underline hover:border-panel-line-2 hover:text-panel-ink ${narrow ? "min-h-11" : "min-h-7"}`}
            >
              {t(mechanismLabel(mechanism))}
            </a>
          ))}
        </div>
      )}

      <div className="mt-1 flex flex-wrap items-center gap-x-3.5 gap-y-1">
        <button type="button" onClick={onCompare} className={link}>
          {t("ui.compare.open")}
        </button>
        <button type="button" onClick={onShare} className={link}>
          {t(shared ? "ui.linkCopied" : "ui.challenge.shareMine")}
        </button>
      </div>
    </section>
  );
}

function Line({ change }: { change: Difference }) {
  const t = useT();
  return (
    <li className="flex gap-1.5 text-[12px] leading-[1.45] text-panel-ink-2">
      <span aria-hidden className="mt-[7px] size-[3px] shrink-0 rounded-full bg-panel-line-2" />
      <span>{t(change.text)}</span>
    </li>
  );
}

function MoveChip({ move }: { move: Move }) {
  const t = useT();
  const up = move.to > move.from;
  return (
    <span className={`num inline-flex min-h-7 items-center gap-1.5 rounded-pill border px-2.5 text-[11px] ${up ? "border-[color-mix(in_oklab,var(--af-ok-panel)_55%,transparent)] text-ok-panel" : "border-[color-mix(in_oklab,var(--af-hot-panel)_55%,transparent)] text-hot-panel"}`}>
      <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" strokeLinejoin="round" aria-hidden className="shrink-0">
        {up ? <path d="M4 17l6-6 4 4 6-8M14 7h6v6" /> : <path d="M4 7l6 6 4-4 6 8M14 17h6v-6" />}
      </svg>
      {move.what === "score" ? t("ui.journal.scoreMoved", { from: move.from, to: move.to }) : t("ui.analysis.fix.areaMoved", { area: { key: `ui.area.${move.area}` }, from: move.from, to: move.to })}
    </span>
  );
}
