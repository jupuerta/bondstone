import { useT } from "../../../i18n";
import type { Problem } from "./lens";

export function FindingBadge({ severity }: { severity: Problem }) {
  const t = useT();
  const critical = severity === "Critical";

  return (
    <span
      role="img"
      aria-label={t(critical ? "ui.node.criticalFinding" : "ui.node.warningFinding")}
      title={t(critical ? "ui.node.criticalFinding" : "ui.node.warningFinding")}
      className={`absolute -top-[5px] -right-[5px] size-[11px] rounded-full ${critical ? "bg-hot-panel ring-2 ring-canvas" : "border-[3px] border-warm-panel bg-canvas"}`}
    />
  );
}
