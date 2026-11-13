import { useState } from "react";
import { useT } from "../../../i18n";
import type { RuleState } from "../../../types/architecture";
import { DOCS_WINDOW } from "../shell/docsWindow";
import { actionKey, missingKey, type CoverageReading } from "./coverage";

const STATE_TONE: Record<RuleState, string> = {
  Fired: "var(--af-hot-panel)",
  Passed: "var(--af-ok-panel)",
  NotApplicable: "var(--af-panel-line-2)",
};

interface CoverageProps {
  reading: CoverageReading | null;
  shareToken: string;
  narrow: boolean;
}

export function Coverage({ reading, shareToken, narrow }: CoverageProps) {
  const t = useT();
  const [open, setOpen] = useState(false);
  const rulesLink = (
    <a href={`/rules/${encodeURIComponent(shareToken)}`} target={DOCS_WINDOW} className="self-start text-[12px] leading-[1.45] text-accent-panel underline underline-offset-4 hover:text-panel-ink">
      {t("ui.analysis.allRules")}
    </a>
  );

  if (reading === null) return <div className="flex flex-col gap-[5px] border-t border-panel-line pt-[11px]">{rulesLink}</div>;

  const silent = reading.muted.reduce((sum, group) => sum + group.codes.length, 0);

  return (
    <div className="flex flex-col gap-[5px] border-t border-panel-line pt-[11px]">
      <span className="eyebrow text-panel-ink-3">{t("ui.analysis.coverage.eyebrow")}</span>

      <span className="flex gap-[3px]" aria-hidden>
        {reading.runs.map((run) => (
          <a key={run.state} href={`/rules/${encodeURIComponent(shareToken)}?state=${run.state}`} target={DOCS_WINDOW} title={t("ui.analysis.coverage.seeState")} className="flex min-w-0 gap-[2px]" style={{ flex: `${run.count} 1 0` }} tabIndex={-1}>
            {Array.from({ length: run.count }, (_, index) => (
              <span
                key={index}
                className="h-[7px] min-w-0 flex-1 rounded-[2px]"
                style={{
                  background: run.state === "NotApplicable" ? "transparent" : STATE_TONE[run.state],
                  border: run.state === "NotApplicable" ? "1px solid var(--af-panel-line-2)" : undefined,
                }}
              />
            ))}
          </a>
        ))}
      </span>

      <p className="text-[12px] leading-[1.45] text-panel-ink-2">
        {reading.runs.map((run, index) => (
          <span key={run.state}>
            {index > 0 && " · "}
            <a href={`/rules/${encodeURIComponent(shareToken)}?state=${run.state}`} target={DOCS_WINDOW} title={t("ui.analysis.coverage.seeState")} className="num no-underline hover:underline hover:underline-offset-4" style={{ color: run.state === "NotApplicable" ? undefined : STATE_TONE[run.state] }}>
              {t(`ui.analysis.coverage.${run.state === "NotApplicable" ? "notApplicable" : run.state.toLowerCase()}`, { count: run.count })}
            </a>
          </span>
        ))}
      </p>

      {silent === 0 ? (
        <p className="text-[12px] leading-[1.45] text-panel-ink-2">{t("ui.analysis.coverage.allRan", { count: reading.total })}</p>
      ) : (
        <>
          <button
            type="button"
            aria-expanded={open}
            onClick={() => setOpen((current) => !current)}
            className={`self-start text-[12px] leading-[1.45] underline underline-offset-4 ${open ? "text-panel-ink" : "text-accent-panel hover:text-panel-ink"} ${narrow ? "min-h-11" : ""}`}
          >
            {t(open ? "ui.analysis.coverage.hide" : "ui.analysis.coverage.whatIsMissing")}
          </button>

          {open && (
            <div className="flex flex-col gap-2.5 pt-1">
              <p className="text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.analysis.coverage.note")}</p>
              <ul className="flex flex-col gap-2.5">
                {reading.muted.map((group) => (
                  <li key={group.cause} className="flex flex-col gap-1">
                    <span className="text-[12px] leading-[1.45] text-panel-ink">{t(missingKey(group.cause))}</span>
                    <span className="text-[11px] leading-[1.45] text-panel-ink-2">{t(actionKey(group.cause))}</span>
                    <span className="mt-0.5 flex flex-wrap gap-1">
                      {group.codes.map((code) => (
                        <a
                          key={code}
                          href={`/rules/${encodeURIComponent(shareToken)}#${code}`}
                          target={DOCS_WINDOW}
                          title={t("ui.analysis.openRule", { code })}
                          className="inline-flex h-[22px] items-center rounded-pill border border-dashed border-panel-line-2 px-2 font-mono text-[10.5px] text-panel-ink-2 no-underline hover:text-panel-ink"
                        >
                          {code}
                        </a>
                      ))}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </>
      )}

      {rulesLink}
    </div>
  );
}
