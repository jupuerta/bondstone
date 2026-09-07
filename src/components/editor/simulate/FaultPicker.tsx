import { useEffect, useState } from "react";
import type { ScenarioFault } from "../../../domain/scenarioUrl";
import { useT } from "../../../i18n";
import type { ElementState, ElementType, FaultKind, NodeType } from "../../../types/architecture";
import { SpinnerIcon } from "../../ui/Icons";
import { TypeIcon } from "../palette/TypeIcons";
import { MechanismChip } from "./MechanismChip";
import { addFault, faultOn, kindsFor } from "./scenario";

export interface FaultPickerProps {
  target: ElementType;
  id: string;
  name: string;
  type?: NodeType;
  state: ElementState | null;
  faults: ScenarioFault[];
  onChange: (faults: ScenarioFault[]) => void;
  loading: boolean;
  narrow: boolean;
}

const VERB =
  "inline-flex w-full items-center justify-center gap-2 rounded-pill border border-panel-line px-3 text-[12px] font-semibold text-panel-ink transition-[color,background-color,border-color] hover:border-panel-line-2 hover:bg-white/8 disabled:cursor-not-allowed disabled:border-dashed disabled:text-panel-ink-3 disabled:hover:border-panel-line disabled:hover:bg-transparent";

export function FaultPicker({ target, id, name, type, state, faults, onChange, loading, narrow }: FaultPickerProps) {
  const t = useT();
  const [pending, setPending] = useState<FaultKind | null>(null);
  const [hovered, setHovered] = useState<FaultKind | null>(null);
  const kinds = kindsFor(target);

  useEffect(() => {
    if (!loading) setPending(null);
  }, [loading]);
  useEffect(() => {
    setPending(null);
    setHovered(null);
  }, [target, id]);

  const applied = (kind: FaultKind) => faultOn(faults, target, id, kind) !== null;
  const hintOf = (kind: FaultKind) => (kind === "down" ? t(target === "Node" ? "ui.fault.down.detail" : "ui.fault.down.edge") : kind === "degraded" ? t("ui.fault.degraded.detail") : t("ui.fault.latency.detail"));
  const shown = hovered ?? kinds[0];
  const press = (kind: FaultKind) => {
    setPending(kind);
    onChange(addFault(faults, target, id, kind));
  };

  return (
    <div className="flex flex-col gap-[7px]">
      <span className="eyebrow text-panel-ink-3">{t(target === "Node" ? "editor.fault.touchedNode" : "editor.fault.touchedEdge")}</span>
      <div className="flex items-center gap-2">
        {type && <TypeIcon type={type} size={16} className="shrink-0" />}
        <span className="min-w-0 truncate text-[13px] font-semibold text-panel-ink">{name}</span>
      </div>
      {kinds.map((kind) => {
        const done = applied(kind);
        const busy = loading && pending === kind;
        return (
          <button
            key={kind}
            type="button"
            disabled={done || loading}
            onClick={() => press(kind)}
            onMouseEnter={() => setHovered(kind)}
            onMouseLeave={() => setHovered(null)}
            onFocus={() => setHovered(kind)}
            onBlur={() => setHovered(null)}
            title={done ? t("editor.fault.alreadyApplied") : undefined}
            aria-busy={busy || undefined}
            className={`${VERB} ${narrow ? "min-h-11" : "h-8"}`}
          >
            {busy && <SpinnerIcon size={14} />}
            {busy ? t("editor.fault.propagating") : t(`ui.fault.${kind}`)}
          </button>
        );
      })}
      <p className="text-[11px] leading-[1.45] text-panel-ink-3">{applied(shown) ? t("editor.fault.alreadyApplied") : hintOf(shown)}</p>
      {state && (
        <p className="border-t border-panel-line pt-2 text-[11px] leading-[1.5] text-panel-ink-2">
          {state.mechanism && <MechanismChip mechanism={state.mechanism} className="mr-1.5 align-middle" />}
          {t(state.reason)}
        </p>
      )}
    </div>
  );
}
