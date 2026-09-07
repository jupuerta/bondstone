import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { composeMarkdown } from "../src/export/markdownExporter";
import { createTranslator } from "../src/i18n";
import type { AnalysisResult, ArchitectureDto, Fault, SimulationResult } from "../src/types/architecture";

const FIXTURES = join(dirname(fileURLToPath(import.meta.url)), "..", "src", "export", "__fixtures__");

const EXPORTED_AT = new Date(Date.UTC(2026, 7, 16, 10, 30, 0));
const t = createTranslator("es");

const readJson = <T>(name: string): T => JSON.parse(readFileSync(join(FIXTURES, name), "utf8")) as T;
const write = (name: string, document: string) => {
  writeFileSync(join(FIXTURES, name), document);
  console.log(`  · ${name}`);
};

for (const name of ["minimal-api-db", "worst-case", "resilient-async", "gateway-microservices"]) {
  write(
    `${name}.export.md`,
    composeMarkdown({
      architecture: readJson<ArchitectureDto>(`${name}.architecture.json`),
      analysis: readJson<AnalysisResult>(`${name}.analysis.json`),
      exportedAt: EXPORTED_AT,
      t,
    }),
  );
}

write(
  "minimal-api-db.down.export.md",
  composeMarkdown({
    architecture: readJson<ArchitectureDto>("minimal-api-db.architecture.json"),
    analysis: readJson<AnalysisResult>("minimal-api-db.analysis.json"),
    exportedAt: EXPORTED_AT,
    t,
    simulation: readJson<SimulationResult>("minimal-api-db.down.simulation.json"),
    faults: readJson<Fault[]>("minimal-api-db.down.faults.json"),
  }),
);

const current = readJson<ArchitectureDto>("resilient-async.architecture.json");
write(
  "resilient-async.evolved.export.md",
  composeMarkdown({
    architecture: { ...current, version: 8 },
    baseline: readJson<ArchitectureDto>("resilient-async.baseline.architecture.json"),
    analysis: readJson<AnalysisResult>("resilient-async.analysis.json"),
    exportedAt: EXPORTED_AT,
    t,
  }),
);

console.log("six golden documents regenerated");
