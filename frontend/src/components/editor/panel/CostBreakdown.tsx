import { useT } from "../../../i18n";
import type { ArchitectureCost, ComponentCost, FindingElement } from "../../../types/architecture";
import { DOCS_WINDOW } from "../shell/docsWindow";

interface CostBreakdownProps {
  cost: ArchitectureCost;
  onPreview: (elements: FindingElement[] | null) => void;
}

export function CostBreakdown({ cost, onPreview }: CostBreakdownProps) {
  const t = useT();
  const billed = cost.components.filter((component) => component.total > 0).sort(byCost);
  const free = cost.components.length - billed.length;
  const worst = billed[0]?.total ?? 0;

  return (
    <div className="mt-2.5 flex flex-col gap-2 border-t border-panel-line pt-2.5" onMouseLeave={() => onPreview(null)}>
      <div className="flex items-baseline justify-between gap-2">
        <span className="eyebrow text-panel-ink-3">{t("ui.analysis.cost.title")}</span>
        <span className="num text-[13px] font-semibold text-panel-ink">{t("ui.analysis.cost.units", { value: cost.total })}</span>
      </div>
      <p className="text-[11px] leading-[1.5] text-panel-ink-3">{t("ui.analysis.cost.note")}</p>

      <ul className="flex flex-col gap-[3px]">
        {billed.map((component) => (
          <CostLine key={component.nodeId} component={component} worst={worst} onPreview={onPreview} />
        ))}
      </ul>

      {free > 0 && <p className="text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.analysis.cost.notYours", { count: free })}</p>}

      <a href="/model" target={DOCS_WINDOW} className="self-start text-[12px] leading-[1.45] text-accent-panel underline underline-offset-4 hover:text-panel-ink">
        {t("ui.analysis.cost.wholeModel")}
      </a>
    </div>
  );
}

function CostLine({ component, worst, onPreview }: { component: ComponentCost; worst: number; onPreview: (elements: FindingElement[] | null) => void }) {
  const t = useT();
  const preview = () => onPreview([{ type: "Node", id: component.nodeId }]);

  return (
    <li>
      <button type="button" title={t("ui.analysis.cost.highlight")} onMouseEnter={preview} onFocus={preview} onBlur={() => onPreview(null)} className="group flex w-full items-center gap-2 text-left">
        <span className="w-[84px] shrink-0 truncate text-[11.5px] text-panel-ink-2 group-hover:text-panel-ink">{component.name}</span>
        <span className="h-1.5 min-w-6 flex-1 overflow-hidden rounded-full bg-wash/10">
          <span className="block h-full rounded-full bg-accent-panel opacity-70 group-hover:opacity-100" style={{ width: `${worst === 0 ? 0 : (component.total / worst) * 100}%` }} />
        </span>
        <span className="num w-[76px] shrink-0 text-right text-[11px] whitespace-nowrap text-panel-ink-3">
          {t("ui.analysis.cost.line", { perInstance: component.perInstance, instances: component.instances })}
        </span>
        <span className="num w-[42px] shrink-0 text-right text-[11.5px] whitespace-nowrap text-panel-ink-2">{t("ui.analysis.cost.units", { value: component.total })}</span>
      </button>
    </li>
  );
}

function byCost(a: ComponentCost, b: ComponentCost): number {
  return b.total - a.total || (a.nodeId < b.nodeId ? -1 : a.nodeId > b.nodeId ? 1 : 0);
}
