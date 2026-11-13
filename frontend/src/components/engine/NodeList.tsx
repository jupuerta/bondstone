import { useT } from "../../i18n";
import type { ArchitectureDto, ElementState } from "../../types/architecture";
import { statusColor, statusOf } from "./Diagram";
import { tintOf } from "./tints";

interface NodeListProps {
  architecture: ArchitectureDto;
  states: ElementState[] | null;
  faultIds: Set<string>;
  className?: string;
}

/** La misma información que el dibujo, en vertical: es lo que se ve en pantalla estrecha, y lo que lee un lector de pantalla. */
export function NodeList({ architecture, states, faultIds, className = "" }: NodeListProps) {
  const t = useT();
  const rank = (nodeId: string) => {
    const s = statusOf(nodeId, states, faultIds);
    return s.injected ? 0 : s.health === "Down" ? 1 : s.health === "Degraded" ? 2 : s.health === "Healthy" ? 3 : 4;
  };
  const nodes = [...architecture.nodes].sort((a, b) => rank(a.id) - rank(b.id));

  return (
    <ul className={`flex flex-col gap-1.5 ${className}`}>
      {nodes.map((node) => {
        const status = statusOf(node.id, states, faultIds);
        const color = statusColor(status.health);
        const alarmed = status.health === "Down" || status.health === "Degraded";
        const label = status.injected
          ? `${t("web.engine.state.Down")} · ${t("web.engine.injected")}`
          : status.health === null
            ? t("web.engine.state.none")
            : `${t(`web.engine.state.${status.health}`)}${status.dragged ? ` · ${t("web.engine.dragged")}` : ""}`;
        return (
          <li
            key={node.id}
            className="flex items-center gap-3 rounded-node border bg-panel-2 px-3 py-2.5"
            style={{ borderColor: alarmed ? color : "var(--af-panel-line)" }}
          >
            <span aria-hidden className="h-7 w-1 shrink-0 rounded-sm" style={{ background: tintOf(node.type) }} />
            <span className="flex-1 text-small font-semibold text-panel-ink">{node.name}</span>
            <span className="inline-flex items-center gap-1.5 font-mono text-label" style={{ color }}>
              <span aria-hidden className="size-[7px] rounded-full" style={{ background: color }} />
              {label}
            </span>
          </li>
        );
      })}
    </ul>
  );
}
