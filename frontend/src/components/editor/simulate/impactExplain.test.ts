import { describe, expect, it } from "vitest";
import { createTranslator } from "../../../i18n";
import type { FindingElement, SimulationMetrics } from "../../../types/architecture";
import { IMPACT_METRICS, metricArithmetic } from "./impactExplain";

const es = createTranslator("es");
const en = createTranslator("en");

const nameOf = (element: FindingElement) => (element.id === "db" ? "Postgres" : element.id);

const metrics = (overrides: Partial<SimulationMetrics> = {}): SimulationMetrics => ({
  componentsDown: 3,
  componentsDegraded: 2,
  componentsTotal: 7,
  blastRadius: 0.7143,
  entryPointsAffected: 1,
  entryPointsTotal: 2,
  nominalLatencyMs: 120,
  faultedLatencyMs: 8052,
  requestAmplification: 12,
  requestsAffected: 0.83,
  bottleneck: { type: "Node", id: "db" },
  backlogRps: 40,
  loadAmplification: 21.6,
  ...overrides,
});

describe("the arithmetic of the simulator (V16 C6)", () => {
  it("writes the blast radius as the sum it is, over the total", () => {
    expect(metricArithmetic("blastRadius", metrics(), es, nameOf)?.formula).toBe("(3 + 2) / 7 = 71 %");
    expect(metricArithmetic("blastRadius", metrics(), en, nameOf)?.formula).toBe("(3 + 2) / 7 = 71%");
  });

  it("writes the requests lost as the complement of what the worst entry point still serves", () => {
    expect(metricArithmetic("requestsAffected", metrics(), es, nameOf)?.formula).toBe("100 % − 17 % = 83 %");
    expect(metricArithmetic("requestsAffected", metrics({ requestsAffected: 0 }), es, nameOf)?.formula).toBe(
      "100 % − 100 % = 0 %",
    );
  });

  it("gives the three counts their share of the whole", () => {
    expect(metricArithmetic("componentsDown", metrics(), es, nameOf)?.formula).toBe("3 / 7 = 43 %");
    expect(metricArithmetic("componentsDegraded", metrics(), es, nameOf)?.formula).toBe("2 / 7 = 29 %");
    expect(metricArithmetic("entryPoints", metrics(), es, nameOf)?.formula).toBe("1 / 2 = 50 %");
  });

  it("names the bottleneck through the panel's own namer, never through the id", () => {
    expect(metricArithmetic("bottleneck", metrics(), es, nameOf)?.formula).toBe(
      "mín(capacidad / carga) del árbol de llamadas = Postgres",
    );
  });

  it("carries the factors with the panel's `×` and the backlog with its sign", () => {
    expect(metricArithmetic("amplification", metrics(), es, nameOf)?.formula).toContain("×12");
    expect(metricArithmetic("loadAmplification", metrics(), es, nameOf)?.formula).toContain("×21,6");
    expect(metricArithmetic("backlog", metrics(), es, nameOf)?.formula).toContain("+40 rps");
  });

  it("says nothing where there is nothing to divide", () => {
    const empty = metrics({ componentsTotal: 0, entryPointsTotal: 0, bottleneck: null });

    expect(metricArithmetic("blastRadius", empty, es, nameOf)).toBeNull();
    expect(metricArithmetic("componentsDown", empty, es, nameOf)).toBeNull();
    expect(metricArithmetic("componentsDegraded", empty, es, nameOf)).toBeNull();
    expect(metricArithmetic("entryPoints", empty, es, nameOf)).toBeNull();
    expect(metricArithmetic("bottleneck", empty, es, nameOf)).toBeNull();

    expect(metricArithmetic("bottleneck", metrics({ bottleneck: undefined }), es, nameOf)).toBeNull();
  });

  it("explains a scenario where nothing broke, because a 0 has an origin too", () => {
    const intact = metrics({
      componentsDown: 0,
      componentsDegraded: 0,
      blastRadius: 0,
      entryPointsAffected: 0,
      requestsAffected: 0,
      faultedLatencyMs: 120,
      requestAmplification: 1,
      backlogRps: 0,
      loadAmplification: 1,
    });

    expect(metricArithmetic("blastRadius", intact, es, nameOf)?.formula).toBe("(0 + 0) / 7 = 0 %");
    expect(metricArithmetic("entryPoints", intact, es, nameOf)?.formula).toBe("0 / 2 = 0 %");
  });

  it("gives every one of the ten manual §08 metrics both halves, in both languages", () => {
    expect(IMPACT_METRICS).toHaveLength(10);

    for (const metric of IMPACT_METRICS) {
      for (const t of [es, en]) {
        const line = metricArithmetic(metric, metrics(), t, nameOf);

        expect(line, metric).not.toBeNull();
        expect(line!.formula, metric).not.toContain("ui.impact.formula");
        expect(line!.why, metric).not.toContain("ui.impact.formula");
        expect(line!.formula, metric).not.toMatch(/\{[A-Za-z]/);
        expect(line!.why, metric).not.toMatch(/\{[A-Za-z]/);
      }
    }
  });
});
