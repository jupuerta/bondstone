import { describe, expect, it } from "vitest";
import en from "../../../i18n/en.json";
import es from "../../../i18n/es.json";
import type { Mechanism } from "../../../types/architecture";
import { MECHANISM_LABEL } from "./mechanismLabel";

const ALL: Mechanism[] = ["fallback", "circuitBreaker", "queue", "healthCheck"];

describe("MECHANISM_LABEL", () => {
  it("names every mechanism the simulator can contain a failure with", () => {
    expect(Object.keys(MECHANISM_LABEL).sort()).toEqual([...ALL].sort());
  });

  it.each(ALL)("says %s in both languages", (mechanism) => {
    const key = MECHANISM_LABEL[mechanism];

    for (const catalogue of [es, en] as Array<Record<string, unknown>>) {
      const text = catalogue[key];
      expect(typeof text, `${key} is missing from a catalogue, or is not a plain string`).toBe("string");
      expect(text).not.toBe(mechanism);
      expect(text as string).not.toContain(".");
    }
  });

  it("reuses names that already exist rather than minting a family of its own", () => {
    for (const key of Object.values(MECHANISM_LABEL)) {
      expect(key.startsWith("ui.")).toBe(true);
    }
  });
});
