import { Fragment } from "react";
import { useT } from "../../../i18n";
import { ZONES, isYoursToPlace, nodeZone, type Zone } from "../../../domain/zone";
import type { NodeDto } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { FieldGloss, FieldGlossToggle, type GlossControls } from "../help/FieldGloss";
import { fieldHelpOf } from "../help/fieldHelp";
import { Term } from "../help/Term";
import { TypeIcon } from "../palette/TypeIcons";
import { CONFIG_SCHEMAS, type ConfigField } from "./configSchemas";
import { CheckField, ChoiceField, Field, TextField } from "./Field";

const NAME_MAX_LENGTH = 100;

export interface NodeEdits {
  onRename: (name: string) => void;
  onField: (key: string, value: string | number | boolean | undefined) => void;
  onDelete: () => void;
  connectedEdges: number;
}

interface NodeInspectorProps {
  node: NodeDto;
  isEntryPoint: boolean;
  heading?: boolean;
  narrow?: boolean;
  edits?: NodeEdits;
  glosses?: GlossControls;
  onOpenSheet?: () => void;
}

export function NodeInspector({ node, isEntryPoint, heading = true, narrow = false, edits, glosses, onOpenSheet }: NodeInspectorProps) {
  const t = useT();
  const configuration = node.configuration ?? {};
  const fields = CONFIG_SCHEMAS[node.type].filter((field) => isEntryPoint || !field.entryPointOnly);
  const zone = nodeZone(node.type, configuration);
  const labelOf = (field: ConfigField) => (field.term ? <Term id={field.term}>{t(field.label)}</Term> : t(field.label));
  const glossOf = (key: string) => {
    const help = glosses?.open ? fieldHelpOf(key) : null;
    return help === null ? null : <FieldGloss id={help.id} />;
  };

  return (
    <div className="flex flex-col gap-3" data-help-panel>
      {heading && <span className="eyebrow text-panel-ink-3">{t("editor.inspector.component")}</span>}
      <div className="flex items-center gap-2">
        <TypeIcon type={node.type} size={14} />
        <span className="text-[13px] font-semibold text-panel-ink">{t(`ui.node.${node.type}`)}</span>
        {isEntryPoint && (
          <span className="font-mono text-label uppercase tracking-[0.06em] text-panel-ink-3">
            <Term id="entryPoint">{t("ui.inspector.entryPoint")}</Term>
          </span>
        )}
        {onOpenSheet && (
          <button
            type="button"
            onClick={onOpenSheet}
            aria-label={t("ui.help.what")}
            title={t("ui.help.what")}
            className={`ml-auto inline-flex shrink-0 items-center justify-center rounded-pill border border-panel-line font-mono text-[12px] font-semibold text-panel-ink-2 hover:border-panel-line-2 hover:text-panel-ink ${narrow ? "size-11" : "size-7"}`}
          >
            ?
          </button>
        )}
      </div>
      {edits ? (
        <TextField label={t("ui.inspector.name")} value={node.name} maxLength={NAME_MAX_LENGTH} onChange={edits.onRename} />
      ) : (
        <Field label={t("ui.inspector.name")} disabled>
          {node.name}
        </Field>
      )}
      {isYoursToPlace(node.type) &&
        (edits ? (
          <ChoiceField<Zone | undefined>
            label={t("ui.inspector.zone")}
            value={zone ?? undefined}
            options={[{ value: undefined, label: t("ui.inspector.zone.none") }, ...ZONES.map((candidate) => ({ value: candidate, label: candidate.toUpperCase() }))]}
            onChange={(value) => edits.onField("zone", value)}
          />
        ) : (
          <div className="flex flex-col gap-1">
            <span className="eyebrow text-panel-ink-3">{t("ui.inspector.zone")}</span>
            <div className="flex flex-wrap gap-1.5" role="group" aria-label={t("ui.inspector.zone")}>
              <ZoneChip label={t("ui.inspector.zone.none")} on={zone === null} />
              {ZONES.map((candidate) => (
                <ZoneChip key={candidate} label={candidate.toUpperCase()} on={zone === candidate} />
              ))}
            </div>
          </div>
        ))}
      {fields.map((field) => (
        <Fragment key={field.key}>
          {edits ? (
            <EditableField field={field} label={labelOf(field)} value={configuration[field.key]} onChange={(value) => edits.onField(field.key, value)} />
          ) : (
            <Field label={labelOf(field)} disabled>
              {valueOf(field, configuration[field.key], t)}
            </Field>
          )}
          {glossOf(field.key)}
        </Fragment>
      ))}
      {fields.length === 0 && <p className="text-meta text-panel-ink-3">{t("ui.inspector.noFields")}</p>}
      {glosses && fields.length > 0 && <FieldGlossToggle glosses={glosses} narrow={narrow} />}
      {edits && (
        <Button variant="panel" size="sm" onClick={edits.onDelete} className="mt-2 flex-col gap-0 border-hot-panel/60 py-1.5 text-hot-panel hover:border-hot-panel hover:bg-hot-panel/10">
          {t("ui.inspector.deleteNode")}
          {edits.connectedEdges > 0 && <span className="text-meta font-normal text-panel-ink-3">{t("ui.inspector.deleteNode.edges", { count: edits.connectedEdges })}</span>}
        </Button>
      )}
    </div>
  );
}

interface EditableFieldProps {
  field: ConfigField;
  label: React.ReactNode;
  value: unknown;
  onChange: (value: string | number | boolean | undefined) => void;
}

function EditableField({ field, label, value, onChange }: EditableFieldProps) {
  const t = useT();

  if (field.kind === "tristate") {
    return (
      <ChoiceField<boolean | undefined>
        label={label}
        value={typeof value === "boolean" ? value : undefined}
        options={[
          { value: undefined, label: t("ui.config.tristate.undeclared") },
          { value: true, label: t("ui.config.tristate.yes") },
          { value: false, label: t("ui.config.tristate.no") },
        ]}
        onChange={onChange}
      />
    );
  }

  if (field.kind === "boolean") {
    return <CheckField label={label} checked={value === true} onChange={onChange} />;
  }

  return (
    <TextField
      label={label}
      kind={field.kind}
      min={field.kind === "number" ? (field.min ?? 0) : undefined}
      max={field.max}
      step={field.step}
      value={typeof value === "string" || typeof value === "number" ? value : ""}
      onChange={(raw) => {
        if (raw === "") return onChange(undefined);
        if (field.kind !== "number") return onChange(raw);
        const bounded = Math.min(Math.max(Number(raw), field.min ?? 0), field.max ?? Infinity);
        if (!Number.isNaN(bounded)) onChange(bounded);
      }}
    />
  );
}

function ZoneChip({ label, on }: { label: string; on: boolean }) {
  return (
    <span
      aria-current={on || undefined}
      className={`inline-flex min-h-[26px] items-center rounded-pill border px-2.5 font-mono text-label uppercase tracking-[0.06em] opacity-50 ${on ? "border-panel-ink bg-panel-ink font-semibold text-panel-on-ink" : "border-panel-line text-panel-ink-2"}`}
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
