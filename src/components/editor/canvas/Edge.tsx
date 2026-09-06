import { BaseEdge, useInternalNode, type Edge, type EdgeProps } from "@xyflow/react";
import { useT } from "../../../i18n";
import { segmentBetween } from "../../engine/layout";
import { ARROW_HEAD, ASYNC_DASH, ASYNC_OPACITY, BUSIEST_OPACITY, DOWN_DASH, EDGE_INK, EDGE_STROKE_WIDTH, NODE_H, NODE_W, SYNC_OPACITY } from "./marks";
import type { ConnectionData } from "./types";

type EditorEdgeProps = EdgeProps<Edge<ConnectionData>>;

export function EditorEdge({ id, source, target, data, selected, interactionWidth }: EditorEdgeProps) {
  const t = useT();
  const from = useInternalNode(source);
  const to = useInternalNode(target);
  if (!from || !to) return null;

  const segment = segmentBetween(boxOf(from), boxOf(to));
  const path = `M ${segment.x1} ${segment.y1} L ${segment.x2} ${segment.y2}`;
  const ink = inkOf(data);
  const marker = `af-arrow-${id}`;
  const flow = data?.flow ?? null;

  return (
    <>
      {flow && <title>{t("ui.call.load", { rps: flow.rps })}</title>}
      <defs>
        <marker id={marker} markerWidth={ARROW_HEAD} markerHeight={ARROW_HEAD} refX={ARROW_HEAD - 0.5} refY={ARROW_HEAD / 2} orient="auto" markerUnits="userSpaceOnUse">
          <path d={`M0,0 L${ARROW_HEAD},${ARROW_HEAD / 2} L0,${ARROW_HEAD} z`} fill={ink.stroke} fillOpacity={ink.opacity} />
        </marker>
      </defs>
      {selected && <path d={path} fill="none" stroke="var(--af-accent-panel)" strokeWidth={7} strokeOpacity={0.35} strokeLinecap="round" />}
      <BaseEdge
        id={id}
        path={path}
        interactionWidth={interactionWidth}
        markerEnd={`url(#${marker})`}
        style={{ stroke: ink.stroke, strokeOpacity: ink.opacity, strokeWidth: EDGE_STROKE_WIDTH, strokeDasharray: ink.dash }}
      />
    </>
  );
}

function boxOf(node: { internals: { positionAbsolute: { x: number; y: number } }; measured: { width?: number; height?: number } }) {
  return {
    x: node.internals.positionAbsolute.x,
    y: node.internals.positionAbsolute.y,
    w: node.measured.width ?? NODE_W,
    h: node.measured.height ?? NODE_H,
  };
}

function inkOf(data: ConnectionData | undefined): { stroke: string; opacity: number; dash: string | undefined } {
  if (data?.state === "Down") return { stroke: "var(--af-hot-panel)", opacity: 1, dash: DOWN_DASH };
  if (data?.state === "Degraded") return { stroke: "var(--af-warm-panel)", opacity: 1, dash: data.synchronous ? undefined : ASYNC_DASH };
  const asynchronous = data?.synchronous === false;
  const busiest = data?.flow?.tone === "busiest";
  return {
    stroke: EDGE_INK,
    opacity: busiest ? BUSIEST_OPACITY : asynchronous ? ASYNC_OPACITY : SYNC_OPACITY,
    dash: asynchronous ? ASYNC_DASH : undefined,
  };
}
