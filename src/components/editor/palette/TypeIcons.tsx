import type { CSSProperties } from "react";
import type { NodeType } from "../../../types/architecture";
import { tintOf } from "../../engine/tints";

interface TypeIconProps {
  type: NodeType;
  size?: number;
  className?: string;
  style?: CSSProperties;
}

export const TYPE_ICON_PATHS: Record<NodeType, string[]> = {
  Client: ["M3 4h18v12H3z", "M8 20h8M12 16v4"],
  ExternalService: ["M3 5h12v14H3z", "M16 8h5v5M21 8l-6 6"],
  IdentityProvider: ["M8 10m-4 0a4 4 0 1 0 8 0a4 4 0 1 0-8 0", "M11 12l9 9M17 18l2 2M15 16l2 2"],
  Gateway: ["M6 3v18M18 3v18", "M9 12h7M13 9l3 3-3 3"],
  LoadBalancer: ["M3 12h5", "M8 12c5 0 3-6 8-6M8 12c5 0 3 6 8 6M8 12h8", "M19 6m-2 0a2 2 0 1 0 4 0a2 2 0 1 0-4 0", "M19 12m-2 0a2 2 0 1 0 4 0a2 2 0 1 0-4 0", "M19 18m-2 0a2 2 0 1 0 4 0a2 2 0 1 0-4 0"],
  Cdn: ["M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0-18 0", "M3 12h18M12 3c3 3.5 3 14.5 0 18M12 3c-3 3.5-3 14.5 0 18"],
  Api: ["M8 6H6a2 2 0 0 0-2 2v2a2 2 0 0 1-2 2 2 2 0 0 1 2 2v2a2 2 0 0 0 2 2h2", "M16 6h2a2 2 0 0 1 2 2v2a2 2 0 0 0 2 2 2 2 0 0 0-2 2v2a2 2 0 0 1-2 2h-2"],
  Service: ["M12 3l8 4.5v9L12 21l-8-4.5v-9z", "M12 12l8-4.5M12 12v9M12 12L4 7.5"],
  Worker: ["M12 12m-3.2 0a3.2 3.2 0 1 0 6.4 0a3.2 3.2 0 1 0-6.4 0", "M12 2v3M12 19v3M22 12h-3M5 12H2M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1M18.4 18.4l-2.1-2.1M7.7 7.7L5.6 5.6"],
  Database: ["M4 6a8 3 0 1 0 16 0a8 3 0 1 0-16 0", "M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6", "M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"],
  Cache: ["M13 2L4 14h7l-1 8 9-12h-7z"],
  ObjectStorage: ["M4 6h16l-2 14H6z", "M4 6l2-3h12l2 3M8 11h8"],
  Queue: ["M3 7h4v10H3zM9 7h4v10H9z", "M16 12h5M18 9l3 3-3 3"],
  Topic: ["M6 12m-2.4 0a2.4 2.4 0 1 0 4.8 0a2.4 2.4 0 1 0-4.8 0", "M11 12h9M14 7l6 5-6 5M11 7v10"],
  Scheduler: ["M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0-18 0", "M12 7v5l3.5 2"],
  Observability: ["M2 14h4l3-8 4 14 3-9 2 3h4"],
};

export function TypeIcon({ type, size = 13, className, style }: TypeIconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      focusable={false}
      className={className}
      style={{ color: tintOf(type), ...style }}
    >
      {TYPE_ICON_PATHS[type].map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  );
}

interface TypeGlyphProps {
  type: NodeType;
  x: number;
  y: number;
  size: number;
  color: string;
}

export function TypeGlyph({ type, x, y, size, color }: TypeGlyphProps) {
  const k = size / 24;
  return (
    <g transform={`translate(${x} ${y}) scale(${k})`} fill="none" stroke={color} strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      {TYPE_ICON_PATHS[type].map((d) => (
        <path key={d} d={d} />
      ))}
    </g>
  );
}
