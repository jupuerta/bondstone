import { useT } from "../../../i18n";
import { protocolOf } from "../../../domain/protocol";
import type { ConnectionDto, NodeDto, ResilienceConfiguration } from "../../../types/architecture";
import { Button } from "../../ui/Button";
import { Chip } from "../../ui/Chip";
import { FieldGloss, FieldGlossToggle, type GlossControls } from "../help/FieldGloss";
import { Term } from "../help/Term";
import { RETRIES_BOUNDS, TIMEOUT_SECONDS_BOUNDS, boundedNumber } from "./bounds";
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
  narrow?: boolean;
  edits?: EdgeEdits;
  glosses?: GlossControls;
}

type Access = "both" | "read" | "write";

export function EdgeInspector({ connection, source, target, heading = true, narrow = false, edits, glosses }: EdgeInspectorProps) {
  const t = useT();
  const configuration = connection.configuration ?? {};
  const protocol = protocolOf(source?.type, target?.type);
  const declared = (value: unknown) => (value === undefined || value === null ? t("ui.config.tristate.undeclared") : String(value));
  const flag = (value: boolean | undefined) => (value === undefined ? t("ui.config.tristate.undeclared") : value ? t("ui.config.tristate.yes") : t("ui.config.tristate.no"));
  const number = (value: number | null | undefined) => (typeof value === "number" ? value : "");
  const open = glosses?.open ?? false;
  const gloss = (id: "access" | "timeoutSeconds" | "retries" | "circuitBreaker" | "fallback") => (open ? <FieldGloss id={id} /> : null);

  const synchronousLabel = <Term id="synchronous">{t("ui.inspector.synchronous")}</Term>;
  const timeoutLabel = <Term id="timeout">{t("ui.inspector.timeoutSeconds")}</Term>;
  const retriesLabel = <Term id="retries">{t("ui.inspector.retries")}</Term>;
  const breakerLabel = <Term id="circuitBreaker">{t("ui.inspector.circuitBreaker")}</Term>;
  const fallbackLabel = <Term id="fallback">{t("ui.inspector.fallback")}</Term>;

  return (
    <div className="flex flex-col gap-3" data-help-panel>
      {heading && <span className="eyebrow text-panel-ink-3">{t("ui.inspector.call")}</span>}
      <p className="text-[13px] font-semibold text-panel-ink">{t("editor.inspector.callBetween", { source: source?.name ?? "?", target: target?.name ?? "?" })}</p>
      <div className="flex flex-wrap gap-1.5">
        <Chip variant="panel">{t(connection.synchronous ? "ui.inspector.synchronous" : "ui.inspector.asynchronous")}</Chip>
        <Chip variant="code">{protocol}</Chip>
      </div>
      {edits ? (
        <>
          <CheckField label={synchronousLabel} checked={connection.synchronous} onChange={edits.onSynchronous} />
          <SelectField<Access>
            label={t("ui.inspector.access")}
            hint={open ? undefined : t("ui.inspector.access.detail")}
            value={configuration.access ?? "both"}
            options={(["both", "read", "write"] as const).map((access) => ({ value: access, label: t(`ui.inspector.access.${access}`) }))}
            onChange={(access) => edits.onResilience({ access: access === "both" ? null : access })}
          />
          {gloss("access")}
          <span className="eyebrow border-t border-panel-line pt-3 text-panel-ink-3">{t("ui.inspector.resilience")}</span>
          <div className={open ? "flex flex-col gap-3" : "grid grid-cols-2 gap-2.5"}>
            <TextField
              label={timeoutLabel}
              kind="number"
              min={TIMEOUT_SECONDS_BOUNDS.min}
              max={TIMEOUT_SECONDS_BOUNDS.max}
              value={number(configuration.timeoutSeconds)}
              onChange={(raw) => edits.onResilience({ timeoutSeconds: boundedNumber(raw, TIMEOUT_SECONDS_BOUNDS) })}
            />
            {gloss("timeoutSeconds")}
            <TextField
              label={retriesLabel}
              kind="number"
              min={RETRIES_BOUNDS.min}
              value={number(configuration.retries)}
              onChange={(raw) => edits.onResilience({ retries: boundedNumber(raw, RETRIES_BOUNDS) })}
            />
            {gloss("retries")}
          </div>
          <CheckField label={breakerLabel} hint={open ? undefined : t("ui.inspector.circuitBreaker.detail")} checked={configuration.circuitBreaker === true} onChange={(on) => edits.onResilience({ circuitBreaker: on })} />
          {gloss("circuitBreaker")}
          <CheckField label={fallbackLabel} hint={open ? undefined : t("ui.inspector.fallback.detail")} checked={configuration.fallback === true} onChange={(on) => edits.onResilience({ fallback: on })} />
          {gloss("fallback")}
          {glosses && <FieldGlossToggle glosses={glosses} narrow={narrow} />}
          <Button variant="panel" size="sm" onClick={edits.onDelete} className="mt-2 border-hot-panel/60 text-hot-panel hover:border-hot-panel hover:bg-hot-panel/10">
            {t("ui.inspector.deleteEdge")}
          </Button>
        </>
      ) : (
        <>
          <Field label={t("ui.inspector.access")} disabled>
            {configuration.access ? t(`ui.inspector.access.${configuration.access}`) : t("ui.config.tristate.undeclared")}
          </Field>
          {gloss("access")}
          <Field label={timeoutLabel} disabled>
            {declared(configuration.timeoutSeconds)}
          </Field>
          {gloss("timeoutSeconds")}
          <Field label={retriesLabel} disabled>
            {declared(configuration.retries)}
          </Field>
          {gloss("retries")}
          <Field label={breakerLabel} disabled>
            {flag(configuration.circuitBreaker)}
          </Field>
          {gloss("circuitBreaker")}
          <Field label={fallbackLabel} disabled>
            {flag(configuration.fallback)}
          </Field>
          {gloss("fallback")}
          {glosses && <FieldGlossToggle glosses={glosses} narrow={narrow} />}
        </>
      )}
    </div>
  );
}
