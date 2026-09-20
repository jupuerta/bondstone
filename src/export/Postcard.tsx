import type { Ref } from "react";
import type { Translator } from "../i18n";
import type { ConnectionDto, NodeDto } from "../types/architecture";
import { DiagramGroup, type DrawingLabels } from "../components/editor/starters/Thumbnail";
import { layoutOf } from "../components/engine/layout";
import type { ScoreBand } from "../components/editor/panel/scoreExplain";
import { fitLabel } from "./diagram";
import { fontFaceStyle, type EmbeddedFace } from "./fonts";
import { POSTCARD_PALETTE } from "./palette";
import type { PostcardModel } from "./postcard";

export const POSTCARD_W = 1200;
export const POSTCARD_H = 630;

const PAD = 28;
const HEADER_H = 56;
const REGION = { x: PAD, y: 178, w: POSTCARD_W - 2 * PAD, h: POSTCARD_H - 178 - PAD };
const MAX_SCALE = 1.05;

const P = POSTCARD_PALETTE;

const BAND_TONE: Record<ScoreBand, string> = {
  solid: P.ok,
  fair: P.ink2,
  fragile: P.warm,
  serious: P.hot,
  unevaluated: P.ink3,
};

export interface PostcardTexts {
  brand: string;
  eyebrow: string;
  link: string;
  title: string;
  score: string;
  outOf: string;
  band: string;
  summary: string;
  empty: string;
  typeOf: (type: NodeDto["type"]) => string;
}

export function postcardTexts(model: PostcardModel, t: Translator): PostcardTexts {
  const summary = [
    t("ui.export.postcard.components", { count: model.components }),
    t("ui.export.postcard.calls", { count: model.calls }),
    model.scored === model.total ? t("ui.export.postcard.areasAll", { count: model.total }) : t("ui.export.postcard.areasSome", { scored: model.scored, total: model.total }),
    model.critical === 0 ? t("ui.export.postcard.clean") : t("ui.export.postcard.criticals", { count: model.critical }),
    t("ui.export.postcard.warnings", { count: model.warning }),
  ].join(" · ");

  return {
    brand: "Bondstone",
    eyebrow: t("ui.export.postcard.eyebrow").toUpperCase(),
    link: model.link.replace(/^https?:\/\//, ""),
    title: model.name,
    score: model.score === null ? "—" : String(model.score),
    outOf: t("ui.analysis.outOf"),
    band: t(`ui.analysis.band.${model.band}`).toUpperCase(),
    summary,
    empty: t("export.diagram.empty"),
    typeOf: (type) => t(`ui.node.${type}`),
  };
}

export function postcardFaceTexts(texts: PostcardTexts, nodes: readonly NodeDto[]): { display: string; mono: string; monoBold: string; sans: string } {
  return {
    display: texts.brand + texts.title,
    mono: texts.eyebrow + texts.link + texts.outOf + texts.band + texts.summary + texts.empty + nodes.map((node) => texts.typeOf(node.type).toUpperCase()).join(""),
    monoBold: texts.score,
    sans: nodes.map((node) => (node.name.length > 18 ? `${node.name.slice(0, 17)}…` : node.name)).join(""),
  };
}

interface PostcardProps {
  model: PostcardModel;
  nodes: readonly NodeDto[];
  connections: readonly ConnectionDto[];
  t: Translator;
  fonts?: readonly EmbeddedFace[] | null;
  svgRef?: Ref<SVGSVGElement>;
  className?: string;
}

export function Postcard({ model, nodes, connections, t, fonts = null, svgRef, className }: PostcardProps) {
  const texts = postcardTexts(model, t);
  const layout = { ...layoutOf({ nodes }), fontScale: 1 };
  const scale = Math.min(REGION.w / layout.width, REGION.h / layout.height, MAX_SCALE);
  const [minX, minY] = layout.viewBox.split(" ").map(Number);
  const tx = REGION.x + (REGION.w - layout.width * scale) / 2 - minX * scale;
  const ty = REGION.y + (REGION.h - layout.height * scale) / 2 - minY * scale;
  const tone = BAND_TONE[model.band];
  const chipText = fitLabel(texts.band, 200, 12);
  const chipWidth = 30 + chipText.length * 8.2;
  const scoreWidth = texts.score.length * 40;
  const outOfWidth = texts.outOf.length * 12.2;
  const scoreEnd = POSTCARD_W - PAD - outOfWidth - 10;
  const titleRoom = scoreEnd - scoreWidth - 24 - PAD;
  const labels: DrawingLabels = { down: "", degraded: "", typeOf: texts.typeOf };

  return (
    <svg ref={svgRef} xmlns="http://www.w3.org/2000/svg" width={POSTCARD_W} height={POSTCARD_H} viewBox={`0 0 ${POSTCARD_W} ${POSTCARD_H}`} className={className} fontFamily={P.sans} role="img" aria-label={model.name}>
      {fonts && fonts.length > 0 && (
        <defs>
          <style>{fontFaceStyle(fonts)}</style>
        </defs>
      )}
      <rect x={0} y={0} width={POSTCARD_W} height={POSTCARD_H} fill={P.paper} />

      <g transform={`translate(${PAD} 18) scale(${20 / 24})`} fill="none" stroke={P.ink} strokeWidth={2} strokeLinejoin="round">
        <rect x="3" y="3" width="8" height="8" rx="1.5" />
        <rect x="13" y="13" width="8" height="8" rx="1.5" />
        <path d="M11 7h4a2 2 0 0 1 2 2v4" />
      </g>
      <text x={PAD + 32} y={34} fontFamily={P.display} fontWeight={600} fontSize={17} letterSpacing="-0.01em" fill={P.ink}>
        {texts.brand}
      </text>
      <text x={PAD + 136} y={33} fontFamily={P.mono} fontSize={11} letterSpacing="0.1em" fill={P.ink3}>
        {texts.eyebrow}
      </text>
      <text x={POSTCARD_W - PAD} y={33} textAnchor="end" fontFamily={P.mono} fontSize={11} fill={P.ink3}>
        {fitLabel(texts.link, 420, 11)}
      </text>
      <line x1={0} y1={HEADER_H} x2={POSTCARD_W} y2={HEADER_H} stroke={P.line} strokeWidth={1} />

      <text x={PAD} y={124} fontFamily={P.display} fontWeight={600} fontSize={46} letterSpacing="-0.03em" fill={P.ink}>
        {fitLabel(texts.title, titleRoom, 46)}
      </text>
      <text x={scoreEnd} y={124} textAnchor="end" fontFamily={P.mono} fontWeight={600} fontSize={64} letterSpacing="-0.02em" fill={P.ink}>
        {texts.score}
      </text>
      <text x={POSTCARD_W - PAD} y={124} textAnchor="end" fontFamily={P.mono} fontSize={20} fill={P.ink2}>
        {texts.outOf}
      </text>

      <rect x={PAD} y={136} width={chipWidth} height={30} rx={15} fill="none" stroke={P.line} strokeWidth={1} />
      <circle cx={PAD + 17} cy={151} r={4} fill={model.band === "serious" ? tone : "none"} stroke={tone} strokeWidth={2} />
      <text x={PAD + 29} y={155} fontFamily={P.mono} fontSize={12} letterSpacing="0.06em" fill={P.ink2}>
        {chipText}
      </text>
      <text x={PAD + chipWidth + 14} y={155} fontFamily={P.mono} fontSize={13} fill={P.ink2}>
        {fitLabel(texts.summary, POSTCARD_W - 2 * PAD - chipWidth - 14, 13)}
      </text>

      {nodes.length === 0 ? (
        <text x={POSTCARD_W / 2} y={REGION.y + REGION.h / 2} textAnchor="middle" fontFamily={P.mono} fontSize={13} fill={P.ink3}>
          {texts.empty}
        </text>
      ) : (
        <g transform={`translate(${round(tx)} ${round(ty)}) scale(${round(scale)})`}>
          <DiagramGroup nodes={nodes} connections={connections} layout={layout} markerId="postcard-arrow" palette={P} labels={labels} />
        </g>
      )}
    </svg>
  );
}

function round(value: number): number {
  return Math.round(value * 1000) / 1000;
}
