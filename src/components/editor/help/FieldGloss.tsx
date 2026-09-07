import { useT } from "../../../i18n";
import { DOCS_WINDOW } from "../shell/docsWindow";
import { FIELD_HELP, destinationsOf, fieldEffect, fieldFormula, readerLabel, type FieldDestination, type FieldId } from "./fieldHelp";

export interface GlossControls {
  open: boolean;
  onToggle: () => void;
}

export function FieldGlossToggle({ glosses, narrow }: { glosses: GlossControls; narrow: boolean }) {
  const t = useT();
  return (
    <div className="border-t border-panel-line pt-2.5">
      <button type="button" onClick={glosses.onToggle} aria-pressed={glosses.open} className={`text-[12px] leading-[1.45] underline underline-offset-4 ${glosses.open ? "text-panel-ink" : "text-accent-panel hover:text-panel-ink"} ${narrow ? "min-h-11" : ""}`}>
        {t("ui.field.toggle")}
      </button>
    </div>
  );
}

export function FieldGloss({ id }: { id: FieldId }) {
  const t = useT();
  const formula = fieldFormula(id);
  const destinations = destinationsOf(id);

  return (
    <div className="-mt-1 flex flex-col gap-1 border-l-2 border-panel-line-2 pl-2.5">
      <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t(readerLabel(FIELD_HELP[id].reader))}</p>
      <p className="text-[12px] leading-[1.5] text-panel-ink-2">{t(fieldEffect(id))}</p>
      {formula !== null && <p className="overflow-x-auto font-mono text-[11px] leading-[1.5] whitespace-nowrap text-panel-ink-3">{t(formula)}</p>}
      {destinations.length > 0 && (
        <div className="mt-0.5 flex flex-wrap gap-1">
          {destinations.map((destination) => (
            <Destination key={destination.to} destination={destination} />
          ))}
        </div>
      )}
    </div>
  );
}

function Destination({ destination }: { destination: FieldDestination }) {
  const t = useT();
  const label = destination.code ?? t(destination.kind === "mechanism" ? "ui.field.mechanism" : destination.kind === "model" ? "ui.field.model" : "ui.field.rules");

  return (
    <a href={destination.to} target={DOCS_WINDOW} className="inline-flex min-h-[22px] items-center rounded-pill border border-panel-line px-2 font-mono text-[10px] uppercase tracking-[0.06em] text-panel-ink-2 no-underline hover:border-panel-line-2 hover:text-panel-ink">
      {label}
    </a>
  );
}
