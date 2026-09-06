import { useT } from "../../../i18n";
import { protocolOf } from "../../../domain/protocol";
import type { ConnectionDto, NodeDto, ResilienceConfiguration } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { Chip } from "../../ui/Chip";
import { CheckField, Field, SelectField, TextField } from "./Field";

export interface EdgeEdits {
  onSynchronous: (synchronous: boolean) => void;
  onResilience: (patch: ResilienceConfiguration) => void;
  onDelete: () => void;
}

interface EdgeInspectorProps {
  connection: ConnectionDto;
  source: NodeDto | undefined;
  target: NodeDto | undefined;
  heading?: boolean;
  edits?: EdgeEdits;
}

type Access = "both" | "read" | "write";

export function EdgeInspector({ connection, source, target, heading = true, edits }: EdgeInspectorProps) {
  const t = useT();
  const configuration = connection.configuration ?? {};
  const protocol = protocolOf(source?.type, target?.type);
  const declared = (value: unknown) => (value === undefined || value === null ? t("ui.config.tristate.undeclared") : String(value));
  const flag = (value: boolean | undefined) => (value === undefined ? t("ui.config.tristate.undeclared") : value ? t("ui.config.tristate.yes") : t("ui.config.tristate.no"));
  const number = (value: number | null | undefined) => (typeof value === "number" ? value : "");
  const parse = (raw: string) => (raw === "" ? null : Number(raw));

  return (
    <div className="flex flex-col gap-3">
      {heading && <span className="eyebrow text-panel-ink-3">{t("ui.inspector.call")}</span>}
      <p className="text-[13px] font-semibold text-panel-ink">{t("editor.inspector.callBetween", { source: source?.name ?? "?", target: target?.name ?? "?" })}</p>
      <div className="flex flex-wrap gap-1.5">
        <Chip variant="panel">{t(connection.synchronous ? "ui.inspector.synchronous" : "ui.inspector.asynchronous")}</Chip>
        <Chip variant="code">{protocol}</Chip>
      </div>
      {edits ? (
        <>
          <CheckField label={t("ui.inspector.synchronous")} checked={connection.synchronous} onChange={edits.onSynchronous} />
          <SelectField<Access>
            label={t("ui.inspector.access")}
            hint={t("ui.inspector.access.detail")}
            value={configuration.access ?? "both"}
            options={(["both", "read", "write"] as const).map((access) => ({ value: access, label: t(`ui.inspector.access.${access}`) }))}
            onChange={(access) => edits.onResilience({ access: access === "both" ? null : access })}
          />
          <span className="eyebrow border-t border-panel-line pt-3 text-panel-ink-3">{t("ui.inspector.resilience")}</span>
          <div className="grid grid-cols-2 gap-2.5">
            <TextField label={t("ui.inspector.timeoutSeconds")} kind="number" min={1} max={600} value={number(configuration.timeoutSeconds)} onChange={(raw) => edits.onResilience({ timeoutSeconds: parse(raw) })} />
            <TextField label={t("ui.inspector.retries")} kind="number" min={0} value={number(configuration.retries)} onChange={(raw) => edits.onResilience({ retries: parse(raw) })} />
          </div>
          <CheckField label={t("ui.inspector.circuitBreaker")} hint={t("ui.inspector.circuitBreaker.detail")} checked={configuration.circuitBreaker === true} onChange={(on) => edits.onResilience({ circuitBreaker: on })} />
          <CheckField label={t("ui.inspector.fallback")} hint={t("ui.inspector.fallback.detail")} checked={configuration.fallback === true} onChange={(on) => edits.onResilience({ fallback: on })} />
          <Button variant="panel" size="sm" onClick={edits.onDelete} className="mt-2 border-hot-panel/60 text-hot-panel hover:border-hot-panel hover:bg-hot-panel/10">
            {t("ui.inspector.deleteEdge")}
          </Button>
        </>
      ) : (
        <>
          <Field label={t("ui.inspector.access")} disabled>
            {configuration.access ? t(`ui.inspector.access.${configuration.access}`) : t("ui.config.tristate.undeclared")}
          </Field>
          <Field label={t("ui.inspector.timeoutSeconds")} disabled>
            {declared(configuration.timeoutSeconds)}
          </Field>
          <Field label={t("ui.inspector.retries")} disabled>
            {declared(configuration.retries)}
          </Field>
          <Field label={t("ui.inspector.circuitBreaker")} disabled>
            {flag(configuration.circuitBreaker)}
          </Field>
          <Field label={t("ui.inspector.fallback")} disabled>
            {flag(configuration.fallback)}
          </Field>
        </>
      )}
    </div>
  );
}
