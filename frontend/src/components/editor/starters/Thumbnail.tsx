import { useId } from "react";
import { useT } from "../../../i18n";
import type { ConnectionDto, HealthState, NodeDto, NodeType } from "../../../types/architecture";
import { SCREEN_PALETTE, type DiagramPalette } from "../../../export/palette";
import { layoutOf, segmentOf, type DiagramLayout } from "../../engine/layout";
import { NODE_H, NODE_H_SIMULATE } from "../canvas/marks";
import { TypeGlyph } from "../palette/TypeIcons";

const NONE: ReadonlySet<string> = new Set();

export interface DrawingLabels {
  down: string;
  degraded: string;
  typeOf: (type: NodeType) => string;
}

interface DiagramGroupProps {
  nodes: readonly NodeDto[];
  connections: readonly ConnectionDto[];
  layout: DiagramLayout;
  markerId: string;
  highlighted?: ReadonlySet<string>;
  health?: ReadonlyMap<string, HealthState> | null;
  labels?: DrawingLabels;
  palette?: DiagramPalette;
}

export function drawingHeight(health: ReadonlyMap<string, HealthState> | null | undefined): number {
  return health ? NODE_H_SIMULATE : NODE_H;
}

export function DiagramGroup({ nodes, connections, layout, markerId, highlighted = NONE, health = null, labels, palette = SCREEN_PALETTE }: DiagramGroupProps) {
  const k = layout.fontScale;
  const marker = `url(#${markerId})`;

  return (
    <g>
      <defs>
        <marker id={markerId} viewBox="0 0 10 10" refX="9" refY="5" markerWidth={6 * k} markerHeight={6 * k} orient="auto-start-reverse">
          <path d="M0 0L10 5L0 10z" fill={palette.ink} fillOpacity={0.5} />
        </marker>
      </defs>
      {connections.map((connection) => {
        const segment = segmentOf(connection, layout.boxes);
        if (!segment) return null;
        const lit = highlighted.has(connection.id);
        const targetDown = health?.get(connection.targetNodeId) === "Down";
        return (
          <line
            key={connection.id}
            x1={segment.x1}
            y1={segment.y1}
            x2={segment.x2}
            y2={segment.y2}
            stroke={lit ? palette.warm : targetDown ? palette.hot : palette.ink}
            strokeOpacity={lit || targetDown ? 1 : 0.5}
            strokeWidth={(lit ? 2.2 : 1.6) * k}
            strokeDasharray={targetDown ? `${2 * k} ${4 * k}` : connection.synchronous ? undefined : `${7 * k} ${5 * k}`}
            markerEnd={marker}
          />
        );
      })}
      {nodes.map((node) => {
        const box = layout.boxes.get(node.id)!;
        const r = 6 * k;
        const lit = highlighted.has(node.id);
        const state = health?.get(node.id) ?? null;
        const tone = state === "Down" ? palette.hot : state === "Degraded" ? palette.warm : null;
        const word = state === "Down" ? labels?.down.toUpperCase() : state === "Degraded" ? labels?.degraded : undefined;
        return (
          <g key={node.id}>
            {lit && <rect x={box.x - 4 * k} y={box.y - 4 * k} width={box.w + 8 * k} height={box.h + 8 * k} rx={r + 4 * k} fill="none" stroke={palette.warm} strokeWidth={2 * k} />}
            <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={r} fill={palette.plate} stroke={tone ?? palette.line} strokeWidth={1 * k} />
            <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={r} fill={palette.tintOf(node.type)} fillOpacity={0.09} />
            {tone && <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={r} fill={tone} fillOpacity={0.12} />}
            <path d={`M${box.x + r} ${box.y} h${box.w - 2 * r} a${r} ${r} 0 0 1 ${r} ${r} v${2 * k} h-${box.w} v-${2 * k} a${r} ${r} 0 0 1 ${r} -${r} z`} fill={palette.tintOf(node.type)} />
            <TypeGlyph type={node.type} x={box.x + 12 * k} y={box.y + 13 * k} size={13 * k} color={palette.tintOf(node.type)} />
            <text x={box.x + 31 * k} y={box.y + 23.5 * k} fontSize={9.5 * k} fontFamily={palette.mono} letterSpacing="0.08em" fill={palette.ink3}>
              {(labels?.typeOf(node.type) ?? node.type).toUpperCase()}
            </text>
            <text x={box.x + 12 * k} y={box.y + 44 * k} fontSize={13 * k} fontWeight={600} fontFamily={palette.sans} fill={palette.ink}>
              {node.name.length > 18 ? `${node.name.slice(0, 17)}…` : node.name}
            </text>
            {tone && word && (
              <g>
                <circle cx={box.x + 16 * k} cy={box.y + 61 * k} r={3.5 * k} fill={state === "Down" ? tone : "none"} stroke={tone} strokeWidth={1.6 * k} />
                <text x={box.x + 24 * k} y={box.y + 64.5 * k} fontSize={10 * k} fontFamily={palette.mono} letterSpacing="0.04em" fill={tone}>
                  {word}
                </text>
              </g>
            )}
          </g>
        );
      })}
    </g>
  );
}

interface ThumbnailProps {
  nodes: readonly NodeDto[];
  connections: readonly ConnectionDto[];
  frame?: DiagramLayout;
  highlighted?: ReadonlySet<string>;
  health?: ReadonlyMap<string, HealthState> | null;
  className?: string;
}

export function Thumbnail({ nodes, connections, frame, highlighted = NONE, health = null, className = "" }: ThumbnailProps) {
  const id = useId();
  const t = useT();
  const own = layoutOf({ nodes }, { nodeHeight: drawingHeight(health) });
  const layout = frame ? { ...frame, boxes: own.boxes } : own;
  const labels: DrawingLabels = { down: t("web.engine.state.Down"), degraded: t("web.engine.state.Degraded"), typeOf: (type) => t(`ui.node.${type}`) };
  const sized = /(^|\s)(h-|aspect-)/.test(className);

  return (
    <svg viewBox={layout.viewBox} preserveAspectRatio="xMidYMid meet" aria-hidden className={`block w-full rounded-node border border-panel-line bg-canvas ${sized ? "" : "aspect-[13/8]"} ${className}`}>
      <DiagramGroup nodes={nodes} connections={connections} layout={layout} markerId={`thumb-arrow-${id}`} highlighted={highlighted} health={health} labels={labels} />
    </svg>
  );
}
