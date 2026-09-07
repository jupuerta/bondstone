import { useModelReference } from "../../../hooks/useModelReference";
import { useT } from "../../../i18n";
import type { NodeType } from "../../../types/architecture";
import { tintOf } from "../../engine/tints";
import { DOCS_WINDOW } from "../shell/docsWindow";
import { capacityScale, decadeLabel } from "./capacityScale";
import { RuleProse } from "./prose";

export function CapacityBlock({ type }: { type: NodeType }) {
  const t = useT();
  const model = useModelReference();

  if (model === null) return null;

  const row = model.components.find((component) => component.type === type);
  if (row === undefined) return null;

  const scale = capacityScale(model.components.map((component) => component.capacityPerInstance));

  return (
    <div className="flex flex-col gap-1.5">
      <span className="eyebrow text-panel-ink-3">{t("ui.help.capacity")}</span>
      <div className="rounded-node border border-panel-line bg-panel-2 px-2.5 py-2">
        <span className="relative flex h-[9px] items-center">
          <span aria-hidden className="absolute inset-0">
            {scale.decades.map((decade) => (
              <span key={decade} className="absolute top-0 bottom-0 w-px bg-panel-line-2" style={{ left: `${scale.positionOf(decade) * 100}%` }} />
            ))}
          </span>
          <span className="relative h-[7px] rounded-[3px]" style={{ width: `${Math.max(scale.positionOf(row.capacityPerInstance) * 100, 1.5)}%`, background: tintOf(type) }} />
        </span>
        <span className="relative block h-3.5">
          {scale.decades.map((decade) => (
            <span key={decade} className="num absolute top-0 text-[9px] text-panel-ink-3" style={{ left: `${scale.positionOf(decade) * 100}%`, transform: decade === scale.max ? "translateX(-100%)" : decade === scale.min ? "none" : "translateX(-50%)" }}>
              {decadeLabel(decade)}
            </span>
          ))}
        </span>
        <p className="mt-1 flex flex-wrap items-baseline gap-x-2 gap-y-0.5 font-mono text-[11px] text-panel-ink-2">
          {t("ui.help.capacityRow", { capacity: row.capacityPerInstance, processing: row.processingMs })}
          <a href="/model" target={DOCS_WINDOW} className="text-accent-panel underline underline-offset-4 hover:text-panel-ink">
            {t("ui.help.wholeModel")}
          </a>
        </p>
      </div>
      <RuleProse className="text-[11px] leading-[1.45] text-panel-ink-3" text={type === "ExternalService" ? t("ui.help.fixedCapacity") : t("ui.help.saturation", { value: model.saturationWarning })} />
    </div>
  );
}
