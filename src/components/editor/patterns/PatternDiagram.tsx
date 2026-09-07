import type { NodeType } from "../../../types/architecture";
import { tintOf } from "../../engine/tints";
import { TYPE_ICON_PATHS } from "../palette/TypeIcons";
import { ASYNC_DASH, HEAD_PATH, ICON, MARK, MARK_ICON, PANELS, PATTERNS, PLATE, VIEW, headTransform, linkRun, marksOf, round, shaftEnd, type Box, type MarkId, type Pattern, type PatternId, type PatternLink, type PatternNode, type PatternScene, type Point } from "./patterns";

const MARK_PATHS: Record<MarkId, string[]> = {
  timeout: ["M5 4.5v15M8 8h10M8 12h5M8 16h13"],
  retries: ["M4 12a8 8 0 1 1 2.6 5.9", "M4 8v4.4h4.4"],
  breaker: ["M6 5.5l8 6.5-8 6.5z", "M18 5.5v13"],
  limit: ["M12 3l7 3.5v5c0 4.2-2.9 8-7 9.5-4.1-1.5-7-5.3-7-9.5v-5z"],
};

const BEFORE = "var(--af-hot-panel)";
const AFTER = "var(--af-ok-panel)";

interface PatternDiagramProps {
  pattern: PatternId | Pattern;
  width?: number;
  className?: string;
}

export function PatternDiagram({ pattern, width = VIEW.width, className }: PatternDiagramProps) {
  const drawing = typeof pattern === "string" ? PATTERNS[pattern] : pattern;

  return (
    <svg viewBox={`0 0 ${VIEW.width} ${VIEW.height}`} width={width} height={(width * VIEW.height) / VIEW.width} fill="none" strokeLinecap="round" strokeLinejoin="round" className={className} aria-hidden>
      <Panel rect={PANELS.before} tone={BEFORE} />
      <Panel rect={PANELS.after} tone={AFTER} />
      <Scene scene={drawing.before} tone={BEFORE} />
      <Scene scene={drawing.after} tone={AFTER} />
      <path d="M 50.2 26.4 L 54 30 L 50.2 33.6" stroke="var(--af-panel-ink-3)" strokeWidth={1.3} opacity={0.7} />
    </svg>
  );
}

function Panel({ rect, tone }: { rect: Box; tone: string }) {
  return <rect x={rect.x} y={rect.y} width={rect.width} height={rect.height} rx={6} fill={tone} fillOpacity={0.07} stroke={tone} strokeOpacity={0.3} strokeWidth={0.8} />;
}

function Scene({ scene, tone }: { scene: PatternScene; tone: string }) {
  return (
    <g>
      {scene.links.map((link) => (
        <Link key={`${link.from}-${link.to}`} scene={scene} link={link} tone={tone} />
      ))}
      {scene.nodes.map((node) => (
        <Plate key={node.id} node={node} />
      ))}
      {marksOf(scene).map((mark) => (
        <Mark key={mark.key} at={mark.at} mark={mark.mark} tone={tone} />
      ))}
    </g>
  );
}

function Mark({ at, mark, tone }: { at: Point; mark: MarkId; tone: string }) {
  return (
    <g>
      <rect x={round(at.x - MARK / 2)} y={round(at.y - MARK / 2)} width={MARK} height={MARK} rx={2.8} fill={tone} fillOpacity={0.18} stroke={tone} strokeOpacity={0.55} strokeWidth={0.8} />
      <g transform={`translate(${round(at.x - MARK_ICON / 2)} ${round(at.y - MARK_ICON / 2)}) scale(${MARK_ICON / 24})`} stroke={tone} strokeWidth={1.8}>
        {MARK_PATHS[mark].map((d) => (
          <path key={d} d={d} />
        ))}
      </g>
    </g>
  );
}

function Link({ scene, link, tone }: { scene: PatternScene; link: PatternLink; tone: string }) {
  const { from, to } = linkRun(scene, link);
  const shaft = shaftEnd(from, to);

  return (
    <g stroke={tone}>
      <line x1={round(from.x)} y1={round(from.y)} x2={round(shaft.x)} y2={round(shaft.y)} strokeWidth={1.1} strokeDasharray={link.async ? ASYNC_DASH : undefined} />
      <path d={HEAD_PATH} transform={headTransform(from, to)} fill={tone} stroke="none" />
    </g>
  );
}

function Plate({ node }: { node: PatternNode }) {
  const tint = tintOf(node.type as NodeType);

  return (
    <g>
      <rect x={round(node.x - PLATE / 2)} y={round(node.y - PLATE / 2)} width={PLATE} height={PLATE} rx={3.6} fill={tint} fillOpacity={0.16} stroke={tint} strokeOpacity={0.5} strokeWidth={0.8} />
      <g transform={`translate(${round(node.x - ICON / 2)} ${round(node.y - ICON / 2)}) scale(${ICON / 24})`} stroke={tint} strokeWidth={1.8}>
        {TYPE_ICON_PATHS[node.type].map((d) => (
          <path key={d} d={d} />
        ))}
      </g>
    </g>
  );
}
