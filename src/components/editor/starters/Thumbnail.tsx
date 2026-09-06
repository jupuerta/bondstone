import { useId } from "react";
import type { ConnectionDto, NodeDto } from "../../../types/architecture";
import { layoutOf, segmentOf } from "../../engine/layout";
import { tintOf } from "../../engine/tints";

interface ThumbnailProps {
  nodes: readonly NodeDto[];
  connections: readonly ConnectionDto[];
  className?: string;
}

export function Thumbnail({ nodes, connections, className = "" }: ThumbnailProps) {
  const id = useId();
  const layout = layoutOf({ nodes });
  const k = layout.fontScale;

  return (
    <svg viewBox={layout.viewBox} preserveAspectRatio="xMidYMid meet" aria-hidden className={`block aspect-[13/8] w-full rounded-node border border-panel-line bg-canvas ${className}`}>
      <defs>
        <marker id={`thumb-arrow-${id}`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth={6 * k} markerHeight={6 * k} orient="auto-start-reverse">
          <path d="M0 0L10 5L0 10z" fill="var(--af-panel-ink)" fillOpacity={0.5} />
        </marker>
      </defs>
      {connections.map((connection) => {
        const segment = segmentOf(connection, layout.boxes);
        if (!segment) return null;
        return (
          <line
            key={connection.id}
            x1={segment.x1}
            y1={segment.y1}
            x2={segment.x2}
            y2={segment.y2}
            stroke="var(--af-panel-ink)"
            strokeOpacity={0.5}
            strokeWidth={1.6 * k}
            strokeDasharray={connection.synchronous ? undefined : `${7 * k} ${5 * k}`}
            markerEnd={`url(#thumb-arrow-${id})`}
          />
        );
      })}
      {nodes.map((node) => {
        const box = layout.boxes.get(node.id)!;
        const r = 6 * k;
        return (
          <g key={node.id}>
            <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={r} fill="var(--af-panel-2)" stroke="var(--af-panel-line)" strokeWidth={1 * k} />
            <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={r} fill={tintOf(node.type)} fillOpacity={0.09} />
            <path d={`M${box.x + r} ${box.y} h${box.w - 2 * r} a${r} ${r} 0 0 1 ${r} ${r} v${2 * k} h-${box.w} v-${2 * k} a${r} ${r} 0 0 1 ${r} -${r} z`} fill={tintOf(node.type)} />
            <text x={box.x + 12 * k} y={box.y + 26 * k} fontSize={9.5 * k} fontFamily="var(--af-font-mono)" letterSpacing="0.08em" fill="var(--af-panel-ink-3)">
              {node.type.toUpperCase()}
            </text>
            <text x={box.x + 12 * k} y={box.y + 44 * k} fontSize={13 * k} fontWeight={600} fill="var(--af-panel-ink)">
              {node.name.length > 18 ? `${node.name.slice(0, 17)}…` : node.name}
            </text>
          </g>
        );
      })}
    </svg>
  );
}
