import { describe, expect, it } from "vitest";
import { postcardModel, scoreTone } from "./postcard";
import { AREA_ORDER, type AnalysisResult, type ArchitectureDto } from "../types/architecture";

const architecture: ArchitectureDto = {
  name: "Tienda",
  shareToken: "tok",
  version: 4,
  createdAt: "2026-08-26T00:00:00Z",
  updatedAt: "2026-08-26T00:00:00Z",
  nodes: [
    { id: "gw", type: "Gateway", name: "Gateway", position: { x: 0, y: 0 } },
    { id: "api", type: "Api", name: "Pedidos", position: { x: 300, y: 0 } },
    { id: "db", type: "Database", name: "Postgres", position: { x: 600, y: 0 } },
  ],
  connections: [
    { id: "c1", sourceNodeId: "gw", targetNodeId: "api", protocol: "Http", synchronous: true },
    { id: "c2", sourceNodeId: "api", targetNodeId: "db", protocol: "Sql", synchronous: true },
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

describe("what the postcard claims", () => {
  const model = postcardModel({ architecture, analysis, origin: "https://bondstone.dev" });

  it("carries the seven areas in the panel's order, scored or not", () => {
    expect(model.areas.map((area) => area.area)).toEqual(AREA_ORDER);
    expect(model.areas.map((area) => area.score)).toEqual([85, 70, 100, 60, 100, null, null]);
    expect(model.scored).toBe(5);
    expect(model.total).toBe(6);
  });

  it("counts what the summary counts and nothing it worked out itself", () => {
    expect(model.critical).toBe(2);
    expect(model.warning).toBe(3);
    expect(model.components).toBe(3);
    expect(model.calls).toBe(2);
  });

  it("links back to the architecture by its share token", () => {
    expect(model.link).toBe("https://bondstone.dev/a/tok");
  });

  it("says the word when five areas and three components earn it", () => {
    expect(model.band).toBe("fragile");
  });

  it("withholds the word on a sketch, however good the number looks", () => {
    const sketch = postcardModel({
      architecture: { ...architecture, nodes: architecture.nodes.slice(0, 2), connections: [architecture.connections[0]] },
      analysis: { ...analysis, score: 100 },
      origin: "https://bondstone.dev",
    });

    expect(sketch.band).toBe("unevaluated");
  });

  it("withholds it too when four areas carry the whole number", () => {
    const partial = postcardModel({
      architecture,
      analysis: {
        ...analysis,
        areas: analysis.areas.map((area, index) =>
          index < 1 ? { ...area, score: null, state: "NoRules" as const } : area,
        ),
      },
      origin: "https://bondstone.dev",
    });

    expect(partial.scored).toBe(4);
    expect(partial.band).toBe("unevaluated");
  });

  it("says nothing about a score it does not have", () => {
    const empty = postcardModel({
      architecture,
      analysis: { ...analysis, score: null },
      origin: "https://bondstone.dev",
    });

    expect(empty.score).toBeNull();
    expect(empty.tone).toBeNull();
  });
});

describe("scoreTone", () => {
  it("uses the thresholds the area bars use", () => {
    expect(scoreTone(100)).toBe("cool");
    expect(scoreTone(90)).toBe("cool");
    expect(scoreTone(89)).toBe("warm");
    expect(scoreTone(70)).toBe("warm");
    expect(scoreTone(69)).toBe("hot");
    expect(scoreTone(0)).toBe("hot");
  });
});
