import { describe, expect, it } from "vitest";
import { renderToStaticMarkup } from "react-dom/server";
import { Postcard, POSTCARD_H, POSTCARD_W, postcardFaceTexts, postcardTexts } from "./Postcard";
import { postcardModel } from "./postcard";
import { POSTCARD_PALETTE } from "./palette";
import { createTranslator, type Locale } from "../i18n";
import type { AnalysisResult, ArchitectureDto } from "../types/architecture";

const architecture: ArchitectureDto = {
  name: "Tienda online — checkout",
  shareToken: "tok",
  version: 4,
  createdAt: "2026-08-26T00:00:00Z",
  updatedAt: "2026-08-26T00:00:00Z",
  nodes: [
    { id: "gw", type: "Gateway", name: "Gateway", position: { x: 420, y: 0 } },
    { id: "api", type: "Api", name: "API Checkout", position: { x: 420, y: 240 } },
    { id: "db", type: "Database", name: "Postgres", position: { x: 60, y: 480 } },
    { id: "q", type: "Queue", name: "Cola de pedidos", position: { x: 780, y: 240 } },
  ],
  connections: [
    { id: "c1", sourceNodeId: "gw", targetNodeId: "api", protocol: "Http", synchronous: true },
    { id: "c2", sourceNodeId: "api", targetNodeId: "db", protocol: "Sql", synchronous: true },
    { id: "c3", sourceNodeId: "api", targetNodeId: "q", protocol: "Amqp", synchronous: false },
  ],
};

const analysis: AnalysisResult = {
  scoringVersion: "v8",
  score: 83,
  summary: { critical: 2, warning: 3, info: 0, passed: 19 },
  findings: [],
  areas: [
    { area: "Availability", score: 85, state: "Scored" },
    { area: "Resilience", score: 70, state: "Scored" },
    { area: "Coupling", score: 100, state: "Scored" },
    { area: "Scalability", score: 60, state: "Scored" },
    { area: "Security", score: 100, state: "Scored" },
    { area: "Observability", score: null, state: "NoRules" },
  ],
};

const model = postcardModel({ architecture, analysis, origin: "https://bondstone.dev" });

function markup(locale: Locale = "es", fonts: Parameters<typeof Postcard>[0]["fonts"] = null): string {
  return renderToStaticMarkup(<Postcard model={model} nodes={architecture.nodes} connections={architecture.connections} t={createTranslator(locale)} fonts={fonts} />);
}

function plain(svg: string): string {
  return svg.replace(/<\/tspan>\s*<tspan[^>]*>/g, " ").replace(/<[^>]+>/g, "");
}

describe("the postcard, as the file that leaves the browser", () => {
  it("carries no colour a raster cannot read", () => {
    const svg = markup();

    expect(svg).not.toMatch(/var\(--/);
    expect(svg).not.toMatch(/color-mix/);
    expect(svg).not.toMatch(/oklch/);
    expect(svg).not.toMatch(/currentColor/);
  });

  it("asks the network for nothing", () => {
    const svg = markup();

    expect(svg).not.toMatch(/<image/);
    expect(svg).not.toMatch(/xlink:href/);
    expect(svg).not.toMatch(/https?:\/\/(?!www\.w3\.org)/);
    expect(svg).not.toMatch(/@import|<script|foreignObject/);
  });

  it("declares its namespace and its size, which is what an <img> needs", () => {
    const svg = markup();

    expect(svg).toContain('xmlns="http://www.w3.org/2000/svg"');
    expect(svg).toContain(`width="${POSTCARD_W}"`);
    expect(svg).toContain(`height="${POSTCARD_H}"`);
    expect(svg).toContain(`viewBox="0 0 ${POSTCARD_W} ${POSTCARD_H}"`);
  });

  it("says the five things it exists to say", () => {
    const svg = markup();

    expect(svg).toContain("Tienda online — checkout");
    expect(svg).toContain(">83<");
    expect(svg).toContain("FRÁGIL");
    expect(svg).toContain("2 críticos");
    expect(svg).toContain("bondstone.dev/a/tok");
  });

  it("draws every component and every call, in the same ink as the canvas", () => {
    const svg = markup();

    for (const node of architecture.nodes) expect(plain(svg), node.name).toContain(node.name);
    expect(svg.match(/<line /g) ?? []).toHaveLength(architecture.connections.length + 1);
    expect(svg.match(/stroke-dasharray="7 5"/g) ?? []).toHaveLength(1);
    for (const tint of architecture.nodes.map((node) => POSTCARD_PALETTE.tintOf(node.type))) {
      expect(svg, tint).toContain(`fill="${tint}"`);
    }
  });

  it("draws the architecture and never a scenario, because the score is not a scenario's", () => {
    const svg = markup();

    expect(svg).not.toContain(`fill="${POSTCARD_PALETTE.hot}" fill-opacity="0.12"`);
    expect(svg).not.toContain('stroke-dasharray="2 4"');
  });

  it("says all of it in the other language too", () => {
    const svg = markup("en");

    expect(svg).toContain("2 criticals");
    expect(svg).toContain("FRAGILE");
    expect(svg).not.toContain("críticos");
  });

  it("carries its typefaces inside itself when they are given, and asks for none otherwise", () => {
    const face = { family: "Bricolage Grotesque", weight: 600, bytes: 3, dataUrl: "data:font/otf;base64,AQID" };

    expect(markup("es", [face])).toContain('@font-face{font-family:"Bricolage Grotesque";font-weight:600');
    expect(markup()).not.toContain("@font-face");
  });
});

describe("the text each face has to be able to write", () => {
  const texts = postcardTexts(model, createTranslator("es"));
  const faces = postcardFaceTexts(texts, architecture.nodes);

  it("gives the title to the display face and the score to the bold monospace", () => {
    expect(faces.display).toContain("Tienda online — checkout");
    expect(faces.display).toContain("Bondstone");
    expect(faces.monoBold).toBe("83");
  });

  it("gives the type labels and the summary to the monospace, and the names to the sans", () => {
    expect(faces.mono).toContain("BASE DE DATOS");
    expect(faces.mono).toContain("2 críticos");
    expect(faces.mono).toContain("bondstone.dev/a/tok");
    expect(faces.sans).toContain("Cola de pedidos");
  });
});
