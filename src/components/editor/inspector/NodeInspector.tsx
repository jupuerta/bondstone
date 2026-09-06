import { useT } from "../../../i18n";
import { ZONES, isYoursToPlace, nodeZone } from "../../../domain/zone";
import type { NodeDto } from "../../../types/architecture";
import { TypeIcon } from "../palette/TypeIcons";
import { CONFIG_SCHEMAS, type ConfigField } from "./configSchemas";
import { Field } from "./Field";

interface NodeInspectorProps {
  node: NodeDto;
  isEntryPoint: boolean;
  heading?: boolean;
}

export function NodeInspector({ node, isEntryPoint, heading = true }: NodeInspectorProps) {
  const t = useT();
  const configuration = node.configuration ?? {};
  const fields = CONFIG_SCHEMAS[node.type].filter((field) => isEntryPoint || !field.entryPointOnly);
  const zone = nodeZone(node.type, configuration);

  return (
    <div className="flex flex-col gap-3">
      {heading && <span className="eyebrow text-panel-ink-3">{t("editor.inspector.component")}</span>}
      <div className="flex items-center gap-2">
        <TypeIcon type={node.type} size={14} />
        <span className="text-[13px] font-semibold text-panel-ink">{t(`ui.node.${node.type}`)}</span>
        {isEntryPoint && <span className="font-mono text-label uppercase tracking-[0.06em] text-panel-ink-3">{t("ui.inspector.entryPoint")}</span>}
      </div>
      <Field label={t("ui.inspector.name")} disabled>
        {node.name}
      </Field>
      {isYoursToPlace(node.type) && (
        <div className="flex flex-col gap-1">
          <span className="eyebrow text-panel-ink-3">{t("ui.inspector.zone")}</span>
          <div className="flex flex-wrap gap-1.5" role="group" aria-label={t("ui.inspector.zone")}>
            <ZoneChip label={t("ui.inspector.zone.none")} on={zone === null} />
            {ZONES.map((candidate) => (
              <ZoneChip key={candidate} label={candidate.toUpperCase()} on={zone === candidate} />
            ))}
          </div>
        </div>
      )}
      {fields.map((field) => (
        <Field key={field.key} label={t(field.label)} disabled>
          {valueOf(field, configuration[field.key], t)}
        </Field>
      ))}
      {fields.length === 0 && <p className="text-meta text-panel-ink-3">{t("ui.inspector.noFields")}</p>}
    </div>
  );
}

function ZoneChip({ label, on }: { label: string; on: boolean }) {
  return (
    <span
      aria-current={on || undefined}
      className={`inline-flex min-h-[26px] items-center rounded-pill border px-2.5 font-mono text-label uppercase tracking-[0.06em] opacity-50 ${on ? "border-panel-ink bg-panel-ink font-semibold text-panel" : "border-panel-line text-panel-ink-2"}`}
    >
      {label}
    </span>
  );
}

function valueOf(field: ConfigField, value: unknown, t: ReturnType<typeof useT>): string {
  if (value === undefined || value === null || value === "") return t("ui.config.tristate.undeclared");
  if (field.kind === "boolean" || field.kind === "tristate") return value === true ? t("ui.config.tristate.yes") : t("ui.config.tristate.no");
  return String(value);
}
