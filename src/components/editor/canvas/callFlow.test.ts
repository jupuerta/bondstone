import { describe, expect, it } from "vitest";
import { callFlows } from "./callFlow";
import type { ConnectionLoad } from "../../../types/architecture";

const amplified: ConnectionLoad[] = [call("door", 100), call("db", 2_400)];

describe("the flow on an arrow", () => {
  it("puts the busiest call at the top of the scale and the rest against it", () => {
    const flows = callFlows(amplified);

    expect(flows.get("db")).toEqual({ rps: 2_400, share: 1, tone: "busiest" });
    expect(flows.get("door")).toEqual({ rps: 100, share: 0.04, tone: "quiet" });
  });

  it("carries the backend's figure through untouched, for the hover to print", () => {
    expect(callFlows([call("c", 339.51), call("c2", 400)]).get("c")?.rps).toBe(339.51);
  });

  it("keeps a call that carries nothing on the map, at the bottom of the scale", () => {
    expect(callFlows([call("scrape", 0), call("db", 900)]).get("scrape")).toEqual({ rps: 0, share: 0, tone: "quiet" });
  });

  it("says nothing at all when nobody declared any traffic", () => {
    expect(callFlows(undefined)).toEqual(new Map());
    expect(callFlows(null)).toEqual(new Map());
    expect(callFlows([])).toEqual(new Map());
  });

  it("says nothing when the declared traffic reaches no arrow at all", () => {
    expect(callFlows([call("a", 0), call("b", 0)])).toEqual(new Map());
  });

  it("keeps the accent for the arrow there is something to compare against", () => {
    expect(callFlows(amplified).get("db")?.tone).toBe("busiest");
  });

  it("drops the accent when every call is the busiest, because then it points at everything", () => {
    const chain = callFlows([call("a", 400), call("b", 400), call("c", 400)]);

    expect([...chain.values()].map((flow) => flow.share)).toEqual([1, 1, 1]);
    expect([...chain.values()].map((flow) => flow.tone)).toEqual(["quiet", "quiet", "quiet"]);
  });

  it("marks every arrow tied at the top when one of them is quieter", () => {
    const split = callFlows([call("door", 100), call("left", 300), call("right", 300)]);

    expect([...split.values()].map((flow) => flow.tone)).toEqual(["quiet", "busiest", "busiest"]);
  });

  it("never grows a share past one, whatever the numbers are", () => {
    const huge = callFlows([call("a", 1), call("b", 2_005_000)]);

    expect(huge.get("b")?.share).toBe(1);
    expect(huge.get("a")?.share).toBeGreaterThanOrEqual(0);
    expect(huge.get("a")?.share).toBeLessThan(1);
  });

  it("is linear, so a ×24 does not get flattened into something tidier", () => {
    expect(callFlows([call("half", 500), call("all", 1_000)]).get("half")?.share).toBe(0.5);
  });
});

function call(connectionId: string, rps: number): ConnectionLoad {
  return { connectionId, rps };
}
