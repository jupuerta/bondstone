import { useEffect, useRef, useState } from "react";
import { useT } from "../../../i18n";
import type { FindingElement, RuleFinding } from "../../../types/architecture";
import { PatternDiagram } from "../patterns/PatternDiagram";
import { DOCS_WINDOW } from "../shell/docsWindow";
import { patternForFinding } from "./findingPattern";
import { SeverityChip } from "./SeverityChip";

export type IdentifiedFinding = RuleFinding & { findingId: string };

export interface FindingActions {
  onPreview: (elements: FindingElement[] | null) => void;
  onPin: (findingId: string, elements: FindingElement[]) => void;
  pinnedId: string | null;
  onApplyFix?: (finding: RuleFinding) => void;
  fixesBlocked?: boolean;
}

interface FindingRowProps extends FindingActions {
  finding: IdentifiedFinding;
  shareToken: string;
  narrow: boolean;
  first: boolean;
}

const PINNED_TINT = "color-mix(in oklab, var(--af-warm-panel) 7%, transparent)";
const PINNED_LINE = "color-mix(in oklab, var(--af-warm-panel) 40%, transparent)";

export function FindingRow({ finding, shareToken, narrow, first, onPreview, onPin, pinnedId, onApplyFix, fixesBlocked = false }: FindingRowProps) {
  const t = useT();
  const [expanded, setExpanded] = useState(false);
  const row = useRef<HTMLLIElement>(null);
  const pinned = pinnedId === finding.findingId;
  const pattern = patternForFinding(finding);

  useEffect(() => {
    if (pinned) row.current?.scrollIntoView({ block: "nearest" });
  }, [pinned]);

  return (
    <li
      ref={row}
      onMouseEnter={() => onPreview(finding.elements)}
      onMouseLeave={() => onPreview(null)}
      onFocus={() => onPreview(finding.elements)}
      onBlur={() => onPreview(null)}
      onClick={() => onPin(finding.findingId, finding.elements)}
      className={`cursor-pointer px-3 py-[11px] ${pinned ? "relative z-[1]" : ""}`}
      style={{
        borderTop: `1px solid ${pinned ? PINNED_LINE : first ? "transparent" : "var(--af-panel-line)"}`,
        boxShadow: pinned ? `0 1px 0 0 ${PINNED_LINE}` : undefined,
        background: pinned ? PINNED_TINT : undefined,
      }}
    >
      <div className="flex flex-wrap items-center gap-x-[7px] gap-y-1.5">
        <SeverityChip severity={finding.severity} />
        <a
          href={`/rules/${encodeURIComponent(shareToken)}#${finding.code}`}
          target={DOCS_WINDOW}
          title={t("ui.analysis.openRule", { code: finding.code })}
          onClick={(event) => event.stopPropagation()}
          className="min-w-0 truncate font-mono text-[11px] text-panel-ink-3 no-underline hover:text-panel-ink"
        >
          {finding.code}
        </a>
        {finding.fix && onApplyFix && (
          <button
            type="button"
            disabled={fixesBlocked}
            onClick={(event) => {
              event.stopPropagation();
              onApplyFix(finding);
            }}
            className={`ml-auto inline-flex max-w-full shrink-0 items-center justify-center rounded-pill border border-panel-line-2 px-[11px] text-[12px] font-semibold text-panel-ink hover:bg-white/8 disabled:cursor-not-allowed disabled:border-dashed disabled:text-panel-ink-3 disabled:hover:bg-transparent ${narrow ? "min-h-11" : "min-h-[26px]"}`}
          >
            {t(finding.fix.label)}
          </button>
        )}
      </div>

      <p className="mt-[5px] text-[13px] leading-[1.4] text-panel-ink">{t(finding.message)}</p>

      <button
        type="button"
        aria-expanded={expanded}
        onClick={(event) => {
          event.stopPropagation();
          setExpanded((current) => !current);
        }}
        className={`mt-[5px] text-[12px] leading-[1.45] underline underline-offset-4 ${expanded ? "text-panel-ink" : "text-accent-panel hover:text-panel-ink"} ${narrow ? "min-h-11" : ""}`}
      >
        {t(expanded ? "ui.analysis.hideDetail" : "ui.analysis.whyItMatters")}
      </button>

      {expanded && (
        <div className="mt-1.5 flex flex-col gap-2">
          <p className="text-[12px] leading-[1.45] text-panel-ink-2">{t(finding.explanation)}</p>
          {finding.suggestion && (
            <p className="rounded-card border border-panel-line bg-panel-3 px-2.5 py-2 text-[12px] leading-[1.45] text-panel-ink-2">
              <span className="font-semibold text-panel-ink">{t("web.engine.whatToDo")}</span> {t(finding.suggestion)}
            </p>
          )}
          {pattern && (
            <div className="flex justify-center">
              <PatternDiagram pattern={pattern} width={240} className="h-auto max-w-full" />
            </div>
          )}
        </div>
      )}
    </li>
  );
}
