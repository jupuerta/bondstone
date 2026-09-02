import { describe, expect, it } from "vitest";
import { RULES, readRules } from "./rulesCatalog";

describe("el catálogo de reglas", () => {
  it("tiene las veinticinco reglas, sin repetir", () => {
    expect(RULES).toHaveLength(25);
    expect(new Set(RULES.map((rule) => rule.code)).size).toBe(25);
  });

  it("marca exactamente las ocho que se arreglan con un clic", () => {
    expect(RULES.filter((rule) => rule.fix).map((rule) => rule.code).sort()).toEqual(
      ["ASYNC_NO_QUEUE", "EXTERNAL_NO_FALLBACK", "MISSING_CACHE", "MISSING_RESILIENCE", "NO_OBSERVABILITY", "ORPHAN_NODE", "QUEUE_NO_DLQ", "TIMEOUT_TOO_LONG"],
    );
  });

  it("sin análisis, ninguna regla tiene estado", () => {
    expect(readRules(null).every((rule) => rule.state === null && rule.findings === 0)).toBe(true);
  });
});
