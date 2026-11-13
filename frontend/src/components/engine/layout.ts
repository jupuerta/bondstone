import type { ArchitectureDto, ConnectionDto, NodeDto } from "../../types/architecture";

export const NODE_W = 176;
export const NODE_H = 60;
const PAD = 28;

export interface Box {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface Segment {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface DiagramLayout {
  viewBox: string;
  width: number;
  height: number;
  boxes: Map<string, Box>;
  /** Cuánto hay que agrandar la letra para que siga leyéndose cuando el dibujo es ancho. */
  fontScale: number;
}

/** Coloca cada nodo donde lo dejó su autor y encuadra el conjunto. */
export function layoutOf(architecture: { nodes: readonly NodeDto[] }, options: { nodeHeight?: number } = {}): DiagramLayout {
  const boxes = new Map<string, Box>();
  const nodeHeight = options.nodeHeight ?? NODE_H;
  for (const node of architecture.nodes) boxes.set(node.id, { x: node.position.x, y: node.position.y, w: NODE_W, h: nodeHeight });
  const all = [...boxes.values()];
  const minX = Math.min(...all.map((b) => b.x)) - PAD;
  const minY = Math.min(...all.map((b) => b.y)) - PAD;
  const maxX = Math.max(...all.map((b) => b.x + b.w)) + PAD;
  const maxY = Math.max(...all.map((b) => b.y + b.h)) + PAD;
  const width = Math.max(maxX - minX, 320);
  const height = Math.max(maxY - minY, 160);
  return { viewBox: `${minX} ${minY} ${width} ${height}`, width, height, boxes, fontScale: Math.max(1, width / 760) };
}

/** La flecha va de borde a borde, no de centro a centro: así la punta se ve. */
export function segmentOf(connection: ConnectionDto, boxes: Map<string, Box>): Segment | null {
  const from = boxes.get(connection.sourceNodeId);
  const to = boxes.get(connection.targetNodeId);
  if (!from || !to) return null;
  return segmentBetween(from, to);
}

export function segmentBetween(from: Box, to: Box): Segment {
  const a = clip(from, center(to));
  const b = clip(to, center(from));
  return { x1: a.x, y1: a.y, x2: b.x, y2: b.y };
}

function center(box: Box): { x: number; y: number } {
  return { x: box.x + box.w / 2, y: box.y + box.h / 2 };
}

/** El punto del borde de `box` en la recta que va de su centro a `towards`. */
function clip(box: Box, towards: { x: number; y: number }): { x: number; y: number } {
  const c = center(box);
  const dx = towards.x - c.x;
  const dy = towards.y - c.y;
  if (dx === 0 && dy === 0) return c;
  const tx = dx === 0 ? Infinity : box.w / 2 / Math.abs(dx);
  const ty = dy === 0 ? Infinity : box.h / 2 / Math.abs(dy);
  const t = Math.min(tx, ty);
  return { x: c.x + dx * t, y: c.y + dy * t };
}

export function nodeById(architecture: ArchitectureDto, id: string): NodeDto | undefined {
  return architecture.nodes.find((node) => node.id === id);
}

export function frameAtLeast(layout: DiagramLayout, minWidth: number, minHeight: number): DiagramLayout {
  const width = Math.max(layout.width, minWidth);
  const height = Math.max(layout.height, minHeight);
  if (width === layout.width && height === layout.height) return layout;
  const [minX, minY] = layout.viewBox.split(" ").map(Number);
  const x = minX - (width - layout.width) / 2;
  const y = minY - (height - layout.height) / 2;
  return { ...layout, viewBox: `${x} ${y} ${width} ${height}`, width, height, fontScale: Math.max(1, width / 760) };
}
