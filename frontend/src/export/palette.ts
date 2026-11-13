import type { HealthState, NodeType } from "../types/architecture";
import { NODE_TYPES } from "../components/editor/canvas/types";
import { oklchToHex } from "./oklch";

export const TINT_OKLCH: Record<NodeType, readonly [number, number, number]> = {
  Gateway: [0.78, 0.1, 40],
  LoadBalancer: [0.78, 0.1, 155],
  Api: [0.78, 0.1, 230],
  Service: [0.78, 0.1, 288],
  Worker: [0.78, 0.1, 55],
  Database: [0.78, 0.1, 78],
  Cache: [0.78, 0.1, 118],
  ObjectStorage: [0.78, 0.1, 198],
  Queue: [0.78, 0.1, 262],
  Topic: [0.78, 0.1, 262],
  Observability: [0.78, 0.1, 330],
  ExternalService: [0.78, 0.1, 8],
  Client: [0.78, 0.1, 212],
  Cdn: [0.78, 0.1, 176],
  Scheduler: [0.78, 0.1, 98],
  IdentityProvider: [0.78, 0.1, 310],
};

export const ACCENT_OKLCH: readonly [number, number, number] = [0.72, 0.17, 267];

export interface DiagramPalette {
  paper: string;
  plate: string;
  ink: string;
  ink2: string;
  ink3: string;
  line: string;
  line2: string;
  hot: string;
  warm: string;
  ok: string;
  accent: string;
  sans: string;
  mono: string;
  display: string;
  tintOf: (type: NodeType) => string;
}

export const SCREEN_PALETTE: DiagramPalette = {
  paper: "var(--af-canvas)",
  plate: "var(--af-panel-2)",
  ink: "var(--af-panel-ink)",
  ink2: "var(--af-panel-ink-2)",
  ink3: "var(--af-panel-ink-3)",
  line: "var(--af-panel-line)",
  line2: "var(--af-panel-line-2)",
  hot: "var(--af-hot-panel)",
  warm: "var(--af-warm-panel)",
  ok: "var(--af-ok-panel)",
  accent: "var(--af-accent-panel)",
  sans: "var(--af-font-ui)",
  mono: "var(--af-font-mono)",
  display: "var(--af-font-display)",
  tintOf: (type) => `var(--af-t-${type})`,
};

const POSTCARD_TINT: Record<NodeType, string> = Object.fromEntries(
  NODE_TYPES.map((type) => [type, oklchToHex(...TINT_OKLCH[type])]),
) as Record<NodeType, string>;

export const POSTCARD_FONTS = {
  sans: '"Instrument Sans", "Helvetica Neue", Arial, sans-serif',
  mono: '"IBM Plex Mono", ui-monospace, Menlo, Consolas, monospace',
  display: '"Bricolage Grotesque", "Bricolage Grotesque Variable", "Helvetica Neue", Arial, sans-serif',
} as const;

export const POSTCARD_PALETTE: DiagramPalette = {
  paper: "#0f1115",
  plate: "#14171d",
  ink: "#eef0f4",
  ink2: "#a3aab8",
  ink3: "#7c8494",
  line: "#2c2e31",
  line2: "#494a4d",
  hot: "#ff5f5f",
  warm: "#f3b64f",
  ok: "#3ecf8e",
  accent: oklchToHex(...ACCENT_OKLCH),
  sans: POSTCARD_FONTS.sans,
  mono: POSTCARD_FONTS.mono,
  display: POSTCARD_FONTS.display,
  tintOf: (type) => POSTCARD_TINT[type],
};

export function stateColor(state: HealthState, palette: DiagramPalette, type: NodeType): string {
  if (state === "Down") return palette.hot;
  if (state === "Degraded") return palette.warm;
  return palette.tintOf(type);
}
