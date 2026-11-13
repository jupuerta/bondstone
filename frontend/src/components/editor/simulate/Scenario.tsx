import { downZones, isZoneFault, type ScenarioFault } from "../../../domain/scenarioUrl";
import { ZONES, type Zone } from "../../../domain/zone";
import type { SimulationStatus } from "../../../hooks/useSimulation";
import { useT } from "../../../i18n";
import type { FindingElement } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { CloseIcon } from "../../ui/Icons";
import { chipClass } from "../canvas/ViewStrip";
import { removeFault, setLatency, toggleZone } from "./scenario";
import { LINK } from "./tones";

export interface ScenarioProps {
  faults: ScenarioFault[];
  nameOf: (element: FindingElement) => string;
  onChange: (faults: ScenarioFault[]) => void;
  status: SimulationStatus;
  error: string | null;
  onRetry: () => void;
  narrow: boolean;
}

const KIND_LABEL: Record<ScenarioFault["kind"], string> = {
  down: "ui.scenario.kind.down",
  degraded: "ui.scenario.kind.degraded",
  latency: "ui.scenario.kind.latency",
};

const HOT = "border-[color-mix(in_oklab,var(--af-hot-panel)_55%,transparent)] text-hot-panel";
const WARM = "border-[color-mix(in_oklab,var(--af-warm-panel)_60%,transparent)] text-warm-panel";

function keyOf(fault: ScenarioFault): string {
  return `${fault.target}:${fault.id}:${fault.kind}`;
}

export function Scenario({ faults, nameOf, onChange, status, error, onRetry, narrow }: ScenarioProps) {
  const t = useT();
  const down = downZones(faults);
  const labelOf = (fault: ScenarioFault) => (isZoneFault(fault) ? t("ui.scenario.zone", { zone: fault.id.toUpperCase() }) : nameOf({ type: fault.target, id: fault.id }));
  const kindOf = (fault: ScenarioFault) => `${t(KIND_LABEL[fault.kind])}${fault.kind === "latency" && !isZoneFault(fault) && fault.value ? ` ${fault.value} ms` : ""}`;
  const removeLabel = (fault: ScenarioFault) => t("ui.scenario.removeFault", { kind: { key: KIND_LABEL[fault.kind] } });

  if (narrow) {
    return (
      <div aria-label={t("ui.scenario.title")} className="flex min-h-14 shrink-0 items-center gap-2 overflow-x-auto border-b border-panel-line bg-panel-3 px-3 py-1.5">
        <span className="eyebrow shrink-0 text-panel-ink-3">{t("ui.scenario.title")}</span>
        {faults.map((fault) => (
          <button key={keyOf(fault)} type="button" onClick={() => onChange(removeFault(faults, fault))} aria-label={`${labelOf(fault)} · ${removeLabel(fault)}`} className={`${chipClass(false, true)} ${fault.kind === "down" ? HOT : WARM}`}>
            <span aria-hidden className={`size-2 shrink-0 rounded-full ${fault.kind === "down" ? "bg-current" : "border-2 border-current"}`} />
            <span className="max-w-32 truncate">{labelOf(fault)}</span>
            <span>· {kindOf(fault)}</span>
            <CloseIcon size={12} className="shrink-0" />
          </button>
        ))}
        {faults.length === 0 && <span className="min-w-[220px] text-[11px] leading-[1.35] whitespace-normal text-panel-ink-3">{t("ui.scenario.empty")}</span>}
        {ZONES.map((zone) => (
          <ZoneChip key={zone} zone={zone} down={down.includes(zone)} onToggle={() => onChange(toggleZone(faults, zone))} className={chipClass(false, true)} label={t("ui.scenario.zone", { zone: zone.toUpperCase() })} />
        ))}
        {faults.length > 0 && (
          <button type="button" onClick={() => onChange([])} className={chipClass(false, true)}>
            {t("ui.scenario.clearAll")}
          </button>
        )}
        {status === "error" && (
          <Button variant="panel" size="sm" onClick={onRetry} className="min-h-11 shrink-0" title={error ?? t("ui.impact.failed")}>
            {t("ui.editor.retry")}
          </Button>
        )}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2.5">
      <div className="flex items-baseline justify-between gap-2">
        <h2 className="font-display text-[15px] leading-[1.2] font-semibold text-panel-ink">{t("ui.scenario.title")}</h2>
        <span className="eyebrow shrink-0 text-panel-ink-3">{faults.length > 0 ? t("ui.scenario.faultCount", { count: faults.length }) : t("editor.scenario.noFaults")}</span>
      </div>

      {status === "error" && (
        <div className="flex flex-col gap-1.5 rounded-card border border-[color-mix(in_oklab,var(--af-hot-panel)_40%,transparent)] px-2.5 py-2">
          <div className="flex items-center justify-between gap-2">
            <span className="text-[12px] leading-[1.45] text-hot-panel">{error ?? t("ui.impact.failed")}</span>
            <Button variant="panel" size="sm" onClick={onRetry} className="min-h-8 shrink-0 px-3 text-[12px]">
              {t("ui.editor.retry")}
            </Button>
          </div>
          <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t("editor.scenario.errorNote")}</p>
        </div>
      )}

      {faults.length === 0 ? (
        <p className="text-[12px] leading-[1.45] text-panel-ink-3">{t("ui.scenario.empty")}</p>
      ) : (
        <>
          <ul className="flex flex-col gap-2">
            {faults.map((fault) => (
              <li key={keyOf(fault)} className="flex flex-col rounded-card border border-panel-line bg-panel-2 p-[9px]">
                <div className="flex items-center justify-between gap-2">
                  <span className="min-w-0 truncate text-[13px] font-semibold text-panel-ink">{labelOf(fault)}</span>
                  <span className={`inline-flex h-[22px] shrink-0 items-center gap-1.5 rounded-pill border px-[9px] font-mono text-[11px] font-semibold uppercase tracking-[0.06em] whitespace-nowrap ${fault.kind === "down" ? HOT : WARM}`}>
                    <span aria-hidden className={`size-2 shrink-0 rounded-full ${fault.kind === "down" ? "bg-current" : "border-2 border-current"}`} />
                    {t(KIND_LABEL[fault.kind])}
                  </span>
                </div>
                {fault.kind === "latency" && !isZoneFault(fault) && (
                  <label className="mt-2 flex items-center gap-1.5">
                    <input
                      type="number"
                      inputMode="numeric"
                      min={1}
                      step={100}
                      value={fault.value ?? 0}
                      onChange={(event) => onChange(setLatency(faults, fault, Number(event.target.value)))}
                      aria-label={t("ui.fault.latency")}
                      className="num h-[26px] w-20 rounded-node border border-panel-line bg-panel px-2 text-[12px] text-panel-ink outline-none hover:border-panel-line-2 focus-visible:border-accent-panel"
                    />
                    <span className="font-mono text-[10px] text-panel-ink-3">ms</span>
                  </label>
                )}
                <button type="button" onClick={() => onChange(removeFault(faults, fault))} aria-label={removeLabel(fault)} className={`${LINK} mt-1.5`}>
                  {t("editor.scenario.remove")}
                </button>
              </li>
            ))}
          </ul>
          <button type="button" onClick={() => onChange([])} className={LINK}>
            {t("ui.scenario.clearAll")}
          </button>
        </>
      )}

      <div className="flex flex-col gap-1.5 border-t border-panel-line pt-2.5">
        <span className="eyebrow text-panel-ink-3">{t("ui.scenario.zones")}</span>
        <div className="flex gap-1.5">
          {ZONES.map((zone) => (
            <ZoneChip
              key={zone}
              zone={zone}
              down={down.includes(zone)}
              onToggle={() => onChange(toggleZone(faults, zone))}
              className="inline-flex h-[26px] shrink-0 items-center gap-1.5 rounded-pill border px-2.5 font-mono text-[11px] font-semibold uppercase tracking-[0.06em] transition-[color,background-color,border-color]"
              label={zone.toUpperCase()}
            />
          ))}
        </div>
        <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.scenario.zones.hint")}</p>
      </div>
    </div>
  );
}

function ZoneChip({ zone, down, onToggle, className, label }: { zone: Zone; down: boolean; onToggle: () => void; className: string; label: string }) {
  const t = useT();
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-pressed={down}
      title={down ? t("ui.canvas.zoneDown", { zone: zone.toUpperCase() }) : t("ui.canvas.zone", { zone: zone.toUpperCase() })}
      className={`${className} ${down ? `${HOT} bg-[color-mix(in_oklab,var(--af-hot-panel)_12%,var(--af-panel))]` : "border-panel-line text-panel-ink-2 hover:border-panel-line-2"}`}
    >
      {down && <span aria-hidden className="size-2 shrink-0 rounded-full bg-current" />}
      {label}
    </button>
  );
}
