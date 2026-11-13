import type { NodeType } from "../../../types/architecture";

export const VIEW = { width: 120, height: 60 } as const;

export const PANELS = {
  before: { x: 2, y: 4, width: 44, height: 52 },
  after: { x: 58, y: 4, width: 60, height: 52 },
} as const;

export const PANEL_INSET = 2;

export const PLATE = 13;
export const ICON = 9;

export const LINK_GAP = 1.2;

export const HEAD = { length: 3.4, halfWidth: 2.1 } as const;

export const ASYNC_DASH = "3 2";

export const MARK = 10;
export const MARK_ICON = 7;
export const MARK_OFFSET = 8.5;
export const MARK_GAP = 1;

export type PatternId =
  | "balanced-instances"
  | "queue-between"
  | "cache-in-front"
  | "external-fallback"
  | "gate-in-front"
  | "bounded-call"
  | "retried-call"
  | "breaker-on-call"
  | "dead-letter-branch"
  | "read-replicas"
  | "capped-entry";

export type MarkId = "timeout" | "retries" | "breaker" | "limit";

export interface PatternNode {
  id: string;
  type: NodeType;
  x: number;
  y: number;
  mark?: MarkId;
}

export interface PatternLink {
  from: string;
  to: string;
  async?: boolean;
  mark?: MarkId;
}

export interface PatternScene {
  nodes: PatternNode[];
  links: PatternLink[];
}

export interface Pattern {
  id: PatternId;
  codes: string[];
  before: PatternScene;
  after: PatternScene;
}

const inserted = (
  from: NodeType,
  through: NodeType,
  to: NodeType,
  async = false,
): Pick<Pattern, "before" | "after"> => ({
  before: {
    nodes: [
      { id: "from", type: from, x: 11.5, y: 30 },
      { id: "to", type: to, x: 36.5, y: 30 },
    ],
    links: [{ from: "from", to: "to" }],
  },
  after: {
    nodes: [
      { id: "from", type: from, x: 66.5, y: 41 },
      { id: "through", type: through, x: 88, y: 19 },
      { id: "to", type: to, x: 109.5, y: 41 },
    ],
    links: [
      { from: "from", to: "through", async },
      { from: "through", to: "to", async },
    ],
  },
});

const settled = (
  from: NodeType,
  to: NodeType,
  mark: { before?: MarkId; after?: MarkId; onTarget?: MarkId },
): Pick<Pattern, "before" | "after"> => ({
  before: {
    nodes: [
      { id: "from", type: from, x: 11.5, y: 30 },
      { id: "to", type: to, x: 36.5, y: 30 },
    ],
    links: [{ from: "from", to: "to", mark: mark.before }],
  },
  after: {
    nodes: [
      { id: "from", type: from, x: 70.5, y: 30 },
      { id: "to", type: to, x: 105.5, y: 30, mark: mark.onTarget },
    ],
    links: [{ from: "from", to: "to", mark: mark.after }],
  },
});

const branched = (
  from: NodeType,
  kept: NodeType,
  added: NodeType,
  async = false,
): Pick<Pattern, "before" | "after"> => ({
  before: {
    nodes: [
      { id: "from", type: from, x: 11.5, y: 30 },
      { id: "kept", type: kept, x: 36.5, y: 30 },
    ],
    links: [{ from: "from", to: "kept", async }],
  },
  after: {
    nodes: [
      { id: "from", type: from, x: 70.5, y: 30 },
      { id: "kept", type: kept, x: 105.5, y: 17 },
      { id: "added", type: added, x: 105.5, y: 43 },
    ],
    links: [
      { from: "from", to: "kept", async },
      { from: "from", to: "added", async },
    ],
  },
});

export const PATTERNS: Record<PatternId, Pattern> = {
  "balanced-instances": {
    id: "balanced-instances",
    codes: ["SPOF"],
    before: {
      nodes: [
        { id: "client", type: "Client", x: 11.5, y: 30 },
        { id: "api", type: "Api", x: 36.5, y: 30 },
      ],
      links: [{ from: "client", to: "api" }],
    },
    after: {
      nodes: [
        { id: "client", type: "Client", x: 66.5, y: 30 },
        { id: "lb", type: "LoadBalancer", x: 88, y: 30 },
        { id: "api-a", type: "Api", x: 109.5, y: 18 },
        { id: "api-b", type: "Api", x: 109.5, y: 42 },
      ],
      links: [
        { from: "client", to: "lb" },
        { from: "lb", to: "api-a" },
        { from: "lb", to: "api-b" },
      ],
    },
  },

  "queue-between": {
    id: "queue-between",
    codes: ["SYNC_CHAIN", "MISSING_RESILIENCE", "ASYNC_NO_QUEUE"],
    ...inserted("Service", "Queue", "Worker", true),
  },

  "cache-in-front": {
    id: "cache-in-front",
    codes: ["MISSING_CACHE", "UNDERSIZED_COMPONENT"],
    ...inserted("Api", "Cache", "Database"),
  },

  "external-fallback": {
    id: "external-fallback",
    codes: ["EXTERNAL_NO_FALLBACK"],
    before: {
      nodes: [
        { id: "service", type: "Service", x: 11.5, y: 30 },
        { id: "external", type: "ExternalService", x: 36.5, y: 30 },
      ],
      links: [{ from: "service", to: "external" }],
    },
    after: {
      nodes: [
        { id: "service", type: "Service", x: 70.5, y: 30 },
        { id: "external", type: "ExternalService", x: 105.5, y: 17 },
        { id: "cache", type: "Cache", x: 105.5, y: 43 },
      ],
      links: [
        { from: "service", to: "external" },
        { from: "service", to: "cache", async: true },
      ],
    },
  },

  "gate-in-front": {
    id: "gate-in-front",
    codes: ["EXPOSED_DATABASE", "UNAUTHENTICATED_ENTRY"],
    ...inserted("Client", "Gateway", "Database"),
  },

  "bounded-call": {
    id: "bounded-call",
    codes: [],
    ...settled("Service", "ExternalService", { after: "timeout" }),
  },

  "retried-call": {
    id: "retried-call",
    codes: [],
    ...settled("Api", "Service", { after: "retries" }),
  },

  "breaker-on-call": {
    id: "breaker-on-call",
    codes: [],
    ...settled("Api", "ExternalService", { before: "retries", after: "breaker" }),
  },

  "capped-entry": {
    id: "capped-entry",
    codes: [],
    ...settled("Client", "Api", { onTarget: "limit" }),
  },

  "dead-letter-branch": {
    id: "dead-letter-branch",
    codes: [],
    ...branched("Queue", "Worker", "Queue", true),
  },

  "read-replicas": {
    id: "read-replicas",
    codes: [],
    ...branched("Api", "Database", "Database"),
  },
};

export const PATTERN_IDS = Object.keys(PATTERNS) as PatternId[];

export const PATTERN_FOR_CODE: Record<string, PatternId> = Object.fromEntries(
  PATTERN_IDS.flatMap((id) => PATTERNS[id].codes.map((code) => [code, id])),
);

export interface Point {
  x: number;
  y: number;
}

export interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

export function plateBox(node: PatternNode): Box {
  return { x: node.x - PLATE / 2, y: node.y - PLATE / 2, width: PLATE, height: PLATE };
}

export function linkRun(scene: PatternScene, link: PatternLink): { from: Point; to: Point; length: number } {
  const a = nodeOf(scene, link.from);
  const b = nodeOf(scene, link.to);
  const from = leaves(a, b);
  const to = leaves(b, a);

  return { from, to, length: Math.hypot(to.x - from.x, to.y - from.y) };
}

function leaves(node: PatternNode, other: PatternNode): Point {
  const dx = other.x - node.x;
  const dy = other.y - node.y;
  const half = PLATE / 2 + LINK_GAP;
  const t = Math.min(dx === 0 ? Infinity : half / Math.abs(dx), dy === 0 ? Infinity : half / Math.abs(dy));

  return { x: node.x + dx * t, y: node.y + dy * t };
}

export function linkMarkAt(scene: PatternScene, link: PatternLink): Point {
  const { from, to, length } = linkRun(scene, link);
  const mid = { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };
  if (length === 0) return mid;

  return {
    x: round(mid.x + ((to.y - from.y) / length) * MARK_OFFSET),
    y: round(mid.y - ((to.x - from.x) / length) * MARK_OFFSET),
  };
}

export function nodeMarkAt(node: PatternNode): Point {
  return { x: node.x, y: node.y - PLATE / 2 - MARK / 2 - MARK_GAP };
}

export function markBox(at: Point): Box {
  return { x: at.x - MARK / 2, y: at.y - MARK / 2, width: MARK, height: MARK };
}

export function marksOf(scene: PatternScene): Array<{ key: string; at: Point; mark: MarkId }> {
  return [
    ...scene.nodes
      .filter((node) => node.mark !== undefined)
      .map((node) => ({ key: `node:${node.id}`, at: nodeMarkAt(node), mark: node.mark! })),
    ...scene.links
      .filter((link) => link.mark !== undefined)
      .map((link) => ({ key: `link:${link.from}-${link.to}`, at: linkMarkAt(scene, link), mark: link.mark! })),
  ];
}

export function nodeOf(scene: PatternScene, id: string): PatternNode {
  const node = scene.nodes.find((candidate) => candidate.id === id);
  if (!node) throw new Error(`No node "${id}" in this scene`);

  return node;
}

export const HEAD_PATH = `M 0 0 L ${-HEAD.length} ${-HEAD.halfWidth} L ${-HEAD.length} ${HEAD.halfWidth} Z`;

export function headTransform(from: Point, to: Point): string {
  const angle = (Math.atan2(to.y - from.y, to.x - from.x) * 180) / Math.PI;

  return `translate(${round(to.x)} ${round(to.y)}) rotate(${round(angle)})`;
}

export function shaftEnd(from: Point, to: Point): Point {
  const length = Math.hypot(to.x - from.x, to.y - from.y);
  if (length === 0) return to;

  const back = Math.min(HEAD.length * 0.8, length);

  return { x: to.x - ((to.x - from.x) / length) * back, y: to.y - ((to.y - from.y) / length) * back };
}

export function round(value: number): number {
  return Math.round(value * 100) / 100;
}
