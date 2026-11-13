import { useMemo, useState } from "react";
import { useFragility } from "../../../hooks/useFragility";
import { useLanguage } from "../../../i18n";
import { applyFormat } from "../../../i18n/format";
import type { Fault, FindingElement, FragilityRow } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { SpinnerIcon } from "../../ui/Icons";
import { containedBar, fragilityBars, worstBar, type FragilityBar } from "./fragility";
import { loadedRow } from "./scenario";
import { LINK, blastTone } from "./tones";

interface FragilityMapProps {
  shareToken: string;
  savedRevision: number;
  faults: Fault[];
  nameOf: (element: FindingElement) => string;
  onPreview: (row: FragilityRow | null) => void;
  onSelect: (row: FragilityRow) => void;
  narrow: boolean;
}

export function FragilityMap({ shareToken, savedRevision, faults, nameOf, onPreview, onSelect, narrow }: FragilityMapProps) {
  const { locale, t } = useLanguage();
  const [asked, setAsked] = useState(false);
  const fragility = useFragility({ shareToken, enabled: asked, savedRevision });
  const bars = useMemo(() => fragilityBars(fragility.map), [fragility.map]);
  const worst = worstBar(bars);
  const lesson = containedBar(bars);
  const nameOfRow = (row: FragilityRow) => nameOf({ type: "Node", id: row.nodeId });

  return (
    <section className="flex flex-col gap-1.5 border-t border-panel-line pt-[11px]">
      <div className="flex items-center justify-between gap-2">
        <span className="text-[14px] font-semibold text-panel-ink">{t("ui.fragility.title")}</span>
        {fragility.status === "loading" && <SpinnerIcon size={12} className="text-panel-ink-3" />}
      </div>

      {!asked ? (
        <button type="button" onClick={() => setAsked(true)} className={`${LINK} ${narrow ? "min-h-11" : ""}`}>
          {t("ui.fragility.ask")}
        </button>
      ) : (
        <div className={`flex flex-col gap-2 rounded-card border border-panel-line bg-panel-2 p-2.5 ${fragility.status === "loading" ? "opacity-60" : ""}`} aria-busy={fragility.status === "loading"}>
          {fragility.status === "error" && (
            <div className="flex items-center justify-between gap-2 rounded-card border border-[color-mix(in_oklab,var(--af-hot-panel)_40%,transparent)] px-2.5 py-1.5">
              <span className="text-[11px] leading-[1.45] text-hot-panel">{fragility.error ?? t("ui.fragility.failed")}</span>
              <Button variant="panel" size="sm" onClick={fragility.retry} className="min-h-8 shrink-0 px-3 text-[12px]">
                {t("ui.editor.retry")}
              </Button>
            </div>
          )}

          {fragility.map && bars.length === 0 && <p className="text-[12px] leading-[1.45] text-panel-ink-3">{t("ui.fragility.empty")}</p>}

          {worst && fragility.map && (
            <p className="text-[12px] leading-[1.45] text-panel-ink">
              {t("ui.fragility.headline", { name: nameOfRow(worst.row), down: worst.row.componentsDown, total: fragility.map.componentsTotal, requests: worst.row.requestsAffected })}
            </p>
          )}

          {bars.length > 0 && (
            <>
              <ul className="flex flex-col gap-1">
                {bars.map((bar) => (
                  <Bar key={bar.row.nodeId} bar={bar} name={nameOfRow(bar.row)} active={loadedRow(faults, bar.row)} onPreview={onPreview} onSelect={onSelect} narrow={narrow} />
                ))}
              </ul>

              <ul className="flex flex-wrap gap-x-3 gap-y-1 border-t border-panel-line pt-2">
                <li className="flex items-center gap-1.5 font-mono text-[11px] text-panel-ink-3">
                  <span aria-hidden className="size-[7px] shrink-0 rounded-[2px] bg-hot-panel" />
                  {t("ui.impact.componentsDown")}
                </li>
                <li className="flex items-center gap-1.5 font-mono text-[11px] text-panel-ink-3">
                  <span aria-hidden className="size-[7px] shrink-0 rounded-[2px] bg-warm-panel" />
                  {t("ui.impact.degraded")}
                </li>
              </ul>

              {lesson && <p className="text-[11px] leading-[1.45] text-warm-panel">{t("ui.fragility.lesson", { name: nameOfRow(lesson.row), blast: applyFormat(lesson.row.blastRadius, "percent", locale) })}</p>}

              <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.fragility.legend")}</p>
            </>
          )}
        </div>
      )}
    </section>
  );
}

interface BarProps {
  bar: FragilityBar;
  name: string;
  active: boolean;
  onPreview: (row: FragilityRow | null) => void;
  onSelect: (row: FragilityRow) => void;
  narrow: boolean;
}

function Bar({ bar, name, active, onPreview, onSelect, narrow }: BarProps) {
  const { locale, t } = useLanguage();
  const { row } = bar;
  const percent = (value: number) => applyFormat(value, "percent", locale);

  return (
    <li>
      <button
        type="button"
        aria-pressed={active}
        aria-label={t("ui.fragility.rowLabel", { name, blast: percent(row.blastRadius), requests: percent(row.requestsAffected) })}
        onMouseEnter={() => onPreview(row)}
        onMouseLeave={() => onPreview(null)}
        onFocus={() => onPreview(row)}
        onBlur={() => onPreview(null)}
        onClick={() => onSelect(row)}
        className={`block w-full rounded-node px-1.5 py-1.5 text-left transition-[background-color] ${active ? "bg-wash/10" : "hover:bg-wash/6"} ${narrow ? "min-h-11" : ""}`}
      >
        <span className="block truncate text-[12px] font-semibold text-panel-ink">{name}</span>
        <span className="mt-1.5 flex h-2 overflow-hidden rounded-[4px] bg-wash/10" aria-hidden>
          <span className="h-full bg-hot-panel" style={{ width: `${bar.downShare * 100}%` }} />
          <span className="h-full bg-warm-panel" style={{ width: `${bar.degradedShare * 100}%` }} />
        </span>
        <span className="num mt-1 flex flex-wrap items-baseline justify-between gap-x-2 text-[10.5px]">
          <span style={{ color: blastTone(row.blastRadius) }}>{t("ui.fragility.row.blast", { value: row.blastRadius })}</span>
          <span style={{ color: blastTone(row.requestsAffected) }}>{t("ui.fragility.row.requests", { value: row.requestsAffected })}</span>
        </span>
        {bar.latency && <span className="mt-0.5 block text-[10.5px] leading-[1.45] text-panel-ink-3">{bar.latency.kind === "noRoute" ? t("ui.impact.noRoute") : t("ui.fragility.row.latency", { from: bar.latency.from, to: bar.latency.to })}</span>}
      </button>
    </li>
  );
}
