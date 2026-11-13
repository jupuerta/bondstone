import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { composeMarkdown } from "./markdownExporter";
import { createTranslator } from "../i18n";
import type { AnalysisResult, ArchitectureDto, Fault, SimulationResult } from "../types/architecture";

const EXPORTED_AT = new Date(Date.UTC(2026, 7, 16, 10, 30, 0));

const FIXTURES = join(__dirname, "__fixtures__");

const t = createTranslator("es");

const read = (name: string) => readFileSync(join(FIXTURES, name), "utf8");
const readJson = <T>(name: string): T => JSON.parse(read(name)) as T;

describe("the exported document matches its reference", () => {
  for (const name of ["minimal-api-db", "worst-case", "resilient-async", "gateway-microservices"]) {
    it(name, () => {
      const document = composeMarkdown({
        architecture: readJson<ArchitectureDto>(`${name}.architecture.json`),
        analysis: readJson<AnalysisResult>(`${name}.analysis.json`),
        exportedAt: EXPORTED_AT,
        t,
      });

      expect(document).toBe(read(`${name}.export.md`));
    });
  }

  it("minimal-api-db with the database down", () => {
    const document = composeMarkdown({
      architecture: readJson<ArchitectureDto>("minimal-api-db.architecture.json"),
      analysis: readJson<AnalysisResult>("minimal-api-db.analysis.json"),
      exportedAt: EXPORTED_AT,
      t,
      simulation: readJson<SimulationResult>("minimal-api-db.down.simulation.json"),
      faults: readJson<Fault[]>("minimal-api-db.down.faults.json"),
    });

    expect(document).toBe(read("minimal-api-db.down.export.md"));
  });

  it("resilient-async with the graph it started from", () => {
    const current = readJson<ArchitectureDto>("resilient-async.architecture.json");

    const document = composeMarkdown({
      architecture: { ...current, version: 8 },
      baseline: readJson<ArchitectureDto>("resilient-async.baseline.architecture.json"),
      analysis: readJson<AnalysisResult>("resilient-async.analysis.json"),
      exportedAt: EXPORTED_AT,
      t,
    });

    expect(document).toBe(read("resilient-async.evolved.export.md"));
  });
});

describe("the starting point", () => {
  const architecture = readJson<ArchitectureDto>("resilient-async.architecture.json");
  const analysis = readJson<AnalysisResult>("resilient-async.analysis.json");

  const compose = (baseline?: ArchitectureDto | null) =>
    composeMarkdown({ architecture, baseline, analysis, exportedAt: EXPORTED_AT, t });

  it("a session that changed nothing prints no starting point", () => {
    expect(compose(architecture)).toBe(compose());
    expect(compose()).not.toContain("## Punto de partida");
  });

  it("a component dragged across the canvas is not a change", () => {
    const moved: ArchitectureDto = {
      ...architecture,
      nodes: architecture.nodes.map((node) => ({ ...node, position: { x: node.position.x + 120, y: 40 } })),
    };

    expect(compose(moved)).toBe(compose());
  });

  it("the request tells the model which architecture it is reviewing", () => {
    const evolved = compose(readJson<ArchitectureDto>("resilient-async.baseline.architecture.json"));

    expect(evolved).toContain("El apartado «Punto de partida» no es lo que hay que revisar");
    expect(compose()).not.toContain("Punto de partida");
  });
});

describe("the document is a pure function of its inputs", () => {
  it("shuffling the architecture changes nothing", () => {
    const architecture = readJson<ArchitectureDto>("worst-case.architecture.json");
    const analysis = readJson<AnalysisResult>("worst-case.analysis.json");

    const shuffled: ArchitectureDto = {
      ...architecture,
      nodes: [...architecture.nodes].reverse(),
      connections: [...architecture.connections].reverse(),
    };

    expect(composeMarkdown({ architecture: shuffled, analysis, exportedAt: EXPORTED_AT, t })).toBe(
      composeMarkdown({ architecture, analysis, exportedAt: EXPORTED_AT, t }),
    );
  });

  it("no scenario and an empty scenario are not the same document", () => {
    const architecture = readJson<ArchitectureDto>("minimal-api-db.architecture.json");
    const analysis = readJson<AnalysisResult>("minimal-api-db.analysis.json");
    const simulation = readJson<SimulationResult>("minimal-api-db.down.simulation.json");

    const without = composeMarkdown({ architecture, analysis, exportedAt: EXPORTED_AT, t });
    const empty = composeMarkdown({ architecture, analysis, exportedAt: EXPORTED_AT, t, simulation, faults: [] });

    expect(without).not.toContain("## Simulación");
    expect(empty).toContain("**Sin fallos inyectados**");
  });
});

describe("the language is an input", () => {
  it("the same architecture comes out in English", () => {
    const document = composeMarkdown({
      architecture: readJson<ArchitectureDto>("worst-case.architecture.json"),
      analysis: readJson<AnalysisResult>("worst-case.analysis.json"),
      exportedAt: EXPORTED_AT,
      t: createTranslator("en"),
    });

    expect(document).toContain("## Analysis");
    expect(document).toContain("| Name | Type | Configuration |");
    expect(document).toContain("single point of failure");
    expect(document).not.toContain("punto único de fallo");
  });
});
