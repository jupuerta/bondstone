import { useT } from "../../../i18n";
import type { NodeType } from "../../../types/architecture";
import { NODE_GROUP_OF, NODE_META } from "../palette/nodeMeta";
import { TypeIcon } from "../palette/TypeIcons";
import { PatternDiagram } from "../patterns/PatternDiagram";
import type { PatternId } from "../patterns/patterns";
import { DOCS_WINDOW } from "../shell/docsWindow";
import { CapacityBlock } from "./CapacityBlock";
import { RuleProse } from "./prose";

export const PATTERN_FOR_TYPE: Partial<Record<NodeType, PatternId>> = {
  LoadBalancer: "balanced-instances",
  Worker: "queue-between",
  Database: "cache-in-front",
  ExternalService: "external-fallback",
};

interface ComponentSheetProps {
  type: NodeType;
  onBack?: () => void;
  narrow?: boolean;
}

export function ComponentSheetTitle({ type, group = false }: { type: NodeType; group?: boolean }) {
  const t = useT();
  return (
    <div className="flex items-center gap-2.5">
      <span className="flex size-8 shrink-0 items-center justify-center rounded-node bg-panel-3">
        <TypeIcon type={type} size={17} />
      </span>
      <span className="min-w-0">
        <span className="block font-display text-[17px] leading-[1.2] font-semibold text-panel-ink">{t(NODE_META[type].label)}</span>
        {group && <span className="eyebrow block text-panel-ink-3">{t(NODE_GROUP_OF[type])}</span>}
      </span>
    </div>
  );
}

export function ComponentSheet({ type, onBack, narrow = false }: ComponentSheetProps) {
  const t = useT();
  const { help, avoid, rules } = NODE_META[type];
  const pattern = PATTERN_FOR_TYPE[type];

  return (
    <div className="flex flex-col gap-3">
      {onBack && (
        <button type="button" onClick={onBack} className={`self-start text-[12px] leading-[1.45] text-accent-panel underline underline-offset-4 hover:text-panel-ink ${narrow ? "min-h-11" : ""}`}>
          ← {t("editor.help.backToInspector")}
        </button>
      )}
      <ComponentSheetTitle type={type} />
      <div className="flex flex-col gap-1">
        <span className="eyebrow text-panel-ink-3">{t("ui.help.whatIs")}</span>
        <p className="text-[12px] leading-[1.5] text-panel-ink-2">{t(help)}</p>
      </div>
      <div className="flex flex-col gap-1">
        <span className="eyebrow text-panel-ink-3">{t("ui.help.avoid")}</span>
        <RuleProse className="text-[12px] leading-[1.5] text-panel-ink-2" text={t(avoid)} />
        {pattern !== undefined && <PatternDiagram pattern={pattern} width={200} className="mt-1.5 h-auto max-w-full" />}
      </div>
      <CapacityBlock type={type} />
      <div className="flex flex-col gap-1.5">
        <span className="eyebrow text-panel-ink-3">{t("ui.help.rules")}</span>
        {rules.length > 0 ? (
          <div className="flex flex-wrap gap-1.5">
            {rules.map((rule) => (
              <a key={rule} href={`/rules#${rule}`} target={DOCS_WINDOW} className={`inline-flex items-center rounded-pill border border-panel-line px-2 font-mono text-[10px] uppercase tracking-[0.06em] text-panel-ink-2 no-underline hover:border-panel-line-2 hover:text-panel-ink ${narrow ? "min-h-11" : "min-h-[22px]"}`}>
                {rule}
              </a>
            ))}
          </div>
        ) : (
          <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.help.noRules")}</p>
        )}
      </div>
    </div>
  );
}
