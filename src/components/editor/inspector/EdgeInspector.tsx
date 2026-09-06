import { useT } from "../../../i18n";
import { protocolOf } from "../../../domain/protocol";
import type { ConnectionDto, NodeDto } from "../../../types/architecture";
import { Chip } from "../../ui/Chip";
import { Field } from "./Field";

interface EdgeInspectorProps {
  connection: ConnectionDto;
  source: NodeDto | undefined;
  target: NodeDto | undefined;
  heading?: boolean;
}

export function EdgeInspector({ connection, source, target, heading = true }: EdgeInspectorProps) {
  const t = useT();
  const configuration = connection.configuration ?? {};
  const protocol = protocolOf(source?.type, target?.type);
  const declared = (value: unknown) => (value === undefined || value === null ? t("ui.config.tristate.undeclared") : String(value));
  const flag = (value: boolean | undefined) => (value === undefined ? t("ui.config.tristate.undeclared") : value ? t("ui.config.tristate.yes") : t("ui.config.tristate.no"));

  return (
    <div className="flex flex-col gap-3">
      {heading && <span className="eyebrow text-panel-ink-3">{t("ui.inspector.call")}</span>}
      <p className="text-[13px] font-semibold text-panel-ink">{t("editor.inspector.callBetween", { source: source?.name ?? "?", target: target?.name ?? "?" })}</p>
      <div className="flex flex-wrap gap-1.5">
        <Chip variant="panel">{t(connection.synchronous ? "ui.inspector.synchronous" : "ui.inspector.asynchronous")}</Chip>
        <Chip variant="code">{protocol}</Chip>
      </div>
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
    </div>
  );
}
