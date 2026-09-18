import { useT } from "../../i18n";
import type { ArchitectureDto, ElementState, HealthState } from "../../types/architecture";
import { layoutOf, segmentOf } from "./layout";
import { tintOf } from "./tints";

export type NodeStatus = { health: HealthState | null; injected: boolean; dragged: boolean };

export function statusOf(nodeId: string, states: ElementState[] | null, faultIds: Set<string>): NodeStatus {
  const state = states?.find((entry) => entry.type === "Node" && entry.id === nodeId) ?? null;
  return { health: state?.state ?? null, injected: faultIds.has(nodeId), dragged: state?.causedBy != null && !faultIds.has(nodeId) };
}

export function statusColor(health: HealthState | null): string {
  if (health === "Down") return "var(--af-hot-panel)";
  if (health === "Degraded") return "var(--af-warm-panel)";
  if (health === "Healthy") return "var(--af-ok-panel)";
  return "var(--af-panel-ink-3)";
}

interface DiagramProps {
  architecture: ArchitectureDto;
  states: ElementState[] | null;
  faultIds: Set<string>;
  /** Un id único por dibujo, para que dos diagramas en la misma página no compartan marcador. */
  id: string;
}

/** El dibujo del motor: los nodos donde los dejó su autor, y el estado de cada uno en texto y color. */
export function Diagram({ architecture, states, faultIds, id }: DiagramProps) {
  const t = useT();
  const layout = layoutOf(architecture);
  const k = layout.fontScale;

  return (
    <svg
      viewBox={layout.viewBox}
      role="img"
      aria-label={architecture.name}
      className="block h-auto w-full font-mono"
      style={{ maxHeight: 360 }}
    >
      <defs>
        <marker id={`arrow-${id}`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth={7 * k} markerHeight={7 * k} orient="auto-start-reverse">
          <path d="M0 0L10 5L0 10z" fill="color-mix(in oklab, var(--af-panel-ink) 40%, transparent)" />
        </marker>
        <filter id={`glow-${id}`} x="-40%" y="-40%" width="180%" height="180%">
          <feGaussianBlur stdDeviation={6 * k} />
        </filter>
      </defs>

      {architecture.connections.map((connection) => {
        const segment = segmentOf(connection, layout.boxes);
        if (!segment) return null;
        return (
          <line
            key={connection.id}
            x1={segment.x1}
            y1={segment.y1}
            x2={segment.x2}
            y2={segment.y2}
            stroke="color-mix(in oklab, var(--af-panel-ink) 40%, transparent)"
            strokeWidth={1.5 * k}
            strokeDasharray={connection.synchronous ? undefined : `${6 * k} ${5 * k}`}
            markerEnd={`url(#arrow-${id})`}
          />
        );
      })}

      {architecture.nodes.map((node) => {
        const box = layout.boxes.get(node.id)!;
        const status = statusOf(node.id, states, faultIds);
        const color = statusColor(status.health);
        const down = status.health === "Down";
        const degraded = status.health === "Degraded";
        const label = status.injected
          ? t("web.engine.injected")
          : status.health === null
            ? t("web.engine.state.none")
            : `${t(`web.engine.state.${status.health}`)}${status.dragged ? ` · ${t("web.engine.dragged")}` : ""}`;
        const r = 6 * k;
        return (
          <g key={node.id}>
            {down && <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={r} fill="var(--af-hot-panel)" opacity={0.45} filter={`url(#glow-${id})`} />}
            <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={r} fill="var(--af-panel-2)" stroke={down || degraded ? color : "var(--af-panel-line)"} strokeWidth={1.5 * k} />
            {(down || degraded) && <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={r} fill={color} fillOpacity={0.12} />}
            <rect x={box.x} y={box.y} width={4 * k} height={box.h} rx={2 * k} fill={tintOf(node.type)} />
            <text x={box.x + 14 * k} y={box.y + 24 * k} fontSize={12.5 * k} fontWeight={600} fill="var(--af-panel-ink)">
              {node.name.length > 20 ? `${node.name.slice(0, 19)}…` : node.name}
            </text>
            <circle cx={box.x + 18 * k} cy={box.y + 43 * k} r={3.5 * k} fill={color} />
            <text x={box.x + 26 * k} y={box.y + 47 * k} fontSize={10 * k} fill={down || degraded ? color : "var(--af-panel-ink-2)"}>
              {label}
            </text>
          </g>
        );
      })}
    </svg>
  );
}
