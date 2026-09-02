import { describe, expect, it } from "vitest";
import { pickFinding } from "./FindingCard";
import type { RuleFinding } from "../../types/architecture";

const finding = (code: string, elements: string[], severity: RuleFinding["severity"] = "Critical"): RuleFinding => ({
  code,
  area: "Availability",
  severity,
  message: { key: code },
  explanation: { key: code },
  elements: elements.map((id) => ({ type: "Node", id })),
});

describe("qué hallazgo enseña el panel", () => {
  it("entre varios de la misma gravedad, el que habla del componente tumbado", () => {
    const findings = [finding("SPOF", ["api"]), finding("SPOF", ["gateway"]), finding("NO_OBSERVABILITY", ["api", "gateway", "postgres", "pedidos", "pagos"]), finding("SPOF", ["postgres"])];
    expect(pickFinding(findings, new Set(["postgres"]))?.elements[0].id).toBe("postgres");
  });

  it("uno concreto antes que uno sobre todo el sistema, aunque el del sistema nombre al tumbado", () => {
    const findings = [finding("NO_OBSERVABILITY", ["api", "postgres", "pedidos"]), finding("SPOF", ["api"])];
    expect(pickFinding(findings, new Set(["postgres"]))?.code).toBe("SPOF");
  });

  it("lo grave antes que lo leve", () => {
    const findings = [finding("MISSING_CACHE", ["postgres"], "Warning"), finding("SPOF", ["api"])];
    expect(pickFinding(findings, new Set(["postgres"]))?.code).toBe("SPOF");
  });
});
