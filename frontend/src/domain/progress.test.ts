import { beforeEach, describe, expect, it, vi } from "vitest";
import { passedChallenges, rememberPassed } from "./progress";

const KEY = "bondstone.challenges.passed";
const store = new Map<string, string>();

beforeEach(() => {
  store.clear();
  vi.stubGlobal("window", {
    localStorage: {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => void store.set(key, value),
    },
  });
});

describe("reading", () => {
  it("is empty for somebody who has never passed one", () => {
    expect(passedChallenges().size).toBe(0);
  });

  it("reads back what was written", () => {
    rememberPassed("backlog");
    rememberPassed("retry-storm");

    expect(passedChallenges()).toEqual(new Set(["backlog", "retry-storm"]));
  });

  it("reads a corrupted store as empty rather than letting it reach the catalogue", () => {
    store.set(KEY, "{not json");

    expect(passedChallenges().size).toBe(0);
  });

  it("survives a store with no localStorage at all", () => {
    vi.stubGlobal("window", {});

    expect(passedChallenges().size).toBe(0);
  });
});

describe("writing", () => {
  it("marks a challenge once, however many times it is passed", () => {
    rememberPassed("backlog");
    rememberPassed("backlog");
    rememberPassed("backlog");

    expect(JSON.parse(store.get(KEY)!)).toEqual(["backlog"]);
  });

  it("keeps the ones already there when a new one lands", () => {
    rememberPassed("backlog");
    rememberPassed("god-service");

    expect(JSON.parse(store.get(KEY)!)).toEqual(["backlog", "god-service"]);
  });

  it("loses the mark and not the page when the store refuses to be written to", () => {
    vi.stubGlobal("window", {
      localStorage: {
        getItem: () => null,
        setItem: () => {
          throw new Error("QuotaExceededError");
        },
      },
    });

    expect(() => rememberPassed("backlog")).not.toThrow();
  });

  it("does not throw on a corrupted store either, and starts the list over", () => {
    store.set(KEY, "17");

    expect(() => rememberPassed("backlog")).not.toThrow();
    expect(passedChallenges().has("backlog")).toBe(true);
  });
});
