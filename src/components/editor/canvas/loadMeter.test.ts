import { describe, expect, it } from "vitest";
import { SATURATION, loadMeter } from "./loadMeter";
import type { ComponentLoad, NodeType } from "../../../types/architecture";

const api = load({ rps: 340, capacityRps: 500, utilization: 0.68 });

describe("the load meter", () => {
  it("fills with the utilisation the backend measured, and does not re-derive it", () => {
    expect(loadMeter("Api", api)).toEqual({ fill: 0.68, tone: "quiet" });
  });

  it("stays quiet under saturation, because a measurement is not a warning", () => {
    expect(loadMeter("Api", load({ utilization: SATURATION - 0.01 }))?.tone).toBe("quiet");
  });

  it("turns warm the moment the rule would start speaking", () => {
    expect(loadMeter("Api", load({ utilization: SATURATION }))?.tone).toBe("warm");
  });

  it("turns hot past capacity, where the rule turns Critical", () => {
    expect(loadMeter("Database", load({ utilization: 1.01 }))?.tone).toBe("hot");
  });

  it("does not grow the bar past the box: 300 % is a colour, not a longer bar", () => {
    expect(loadMeter("Database", load({ rps: 1500, capacityRps: 500, utilization: 3 }))).toEqual({
      fill: 1,
      tone: "hot",
    });
  });

  it("says nothing when nobody declared traffic", () => {
    expect(loadMeter("Api", null)).toBeNull();
    expect(loadMeter("Api", undefined)).toBeNull();
  });

  it("says nothing on the six boxes nobody can size", () => {
    const nobodys: NodeType[] = ["Client", "Cdn", "ExternalService", "IdentityProvider", "Scheduler", "Observability"];
    for (const type of nobodys) expect(loadMeter(type, api)).toBeNull();
  });

  it("keeps the meter on everything that is yours to size", () => {
    const yours: NodeType[] = [
      "Gateway",
      "LoadBalancer",
      "Api",
      "Service",
      "Worker",
      "Database",
      "Cache",
      "ObjectStorage",
      "Queue",
      "Topic",
    ];
    for (const type of yours) expect(loadMeter(type, api)).not.toBeNull();
  });

  it("says nothing where no traffic arrives", () => {
    expect(loadMeter("Worker", load({ rps: 0, utilization: 0 }))).toBeNull();
  });

  it("paints a barely-loaded component instead of hiding it", () => {
    expect(loadMeter("Gateway", load({ rps: 340, capacityRps: 20_000, utilization: 0.02 }))).toEqual({
      fill: 0.02,
      tone: "quiet",
    });
  });
});

function load(patch: Partial<ComponentLoad>): ComponentLoad {
  return { nodeId: "n1", rps: 340, capacityRps: 500, utilization: 0.68, ...patch };
}
