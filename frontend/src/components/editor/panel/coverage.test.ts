import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import en from "../../../i18n/en.json";
import es from "../../../i18n/es.json";
import { LOCALES, createTranslator } from "../../../i18n";
import type { AnalysisResult, Area, RuleCoverage, RuleState } from "../../../types/architecture";
import {
  MUTED_CAUSES,
  OTHER_CAUSE,
  STATE_ORDER,
  actionKey,
  causeOf,
  missingKey,
  readCoverage,
  type MutedCause,
} from "./coverage";

const ALL_CAUSES: MutedCause[] = [...MUTED_CAUSES, OTHER_CAUSE];

const FIXTURES = join(__dirname, "..", "..", "..", "export", "__fixtures__");

const recorded = (name: string): AnalysisResult =>
  JSON.parse(readFileSync(join(FIXTURES, `${name}.analysis.json`), "utf8"));

const REFERENCES = ["minimal-api-db", "resilient-async", "gateway-microservices", "worst-case"];

function analysis(coverage: RuleCoverage[] | undefined): AnalysisResult {
  return {
    scoringVersion: "test",
    score: 0,
    areas: [],
    summary: { critical: 0, warning: 0, info: 0, passed: 0 },
    findings: [],
    coverage,
  };
}

function entry(code: string, state: RuleState, reason?: string | null, area: Area = "Resilience"): RuleCoverage {
  return { code, area, state, reason: reason == null ? reason : { key: reason } };
}

describe("readCoverage", () => {
  it("has nothing to say without a coverage field", () => {
    expect(readCoverage(null)).toBeNull();
    expect(readCoverage(analysis(undefined))).toBeNull();
    expect(readCoverage(analysis([]))).toBeNull();
  });

  it("runs the three states in the order the bar draws them, and drops the empty ones", () => {
    const reading = readCoverage(
      analysis([entry("A", "Passed"), entry("B", "Fired"), entry("C", "Passed")]),
    );

    expect(reading?.total).toBe(3);
    expect(reading?.runs).toEqual([
      { state: "Fired", count: 1 },
      { state: "Passed", count: 2 },
    ]);
    expect(reading?.muted).toEqual([]);
  });

  it("groups the silent rules by what the drawing is missing, not by rule", () => {
    const reading = readCoverage(
      analysis([
        entry("QUEUE_NO_DLQ", "NotApplicable", "rule.na.QUEUE_NO_DLQ.noQueue"),
        entry("MISSING_CDN", "NotApplicable", "rule.na.MISSING_CDN.noLoad"),
        entry("QUEUE_NO_CONSUMER", "NotApplicable", "rule.na.QUEUE_NO_CONSUMER.noQueue"),
      ]),
    );

    expect(reading?.muted).toEqual([
      { cause: "noQueue", codes: ["QUEUE_NO_DLQ", "QUEUE_NO_CONSUMER"] },
      { cause: "noLoad", codes: ["MISSING_CDN"] },
    ]);
  });

  it("puts the biggest group first and keeps the analyzer's order for a tie", () => {
    const reading = readCoverage(
      analysis([
        entry("TIMEOUT_TOO_LONG", "NotApplicable", "rule.na.TIMEOUT_TOO_LONG.noTimeout"),
        entry("SPOF", "NotApplicable", "rule.na.SPOF.noDependency"),
        entry("CYCLE", "NotApplicable", "rule.na.CYCLE.noCalls"),
        entry("HIGH_COUPLING", "NotApplicable", "rule.na.HIGH_COUPLING.noCalls"),
      ]),
    );

    expect(reading?.muted.map((group) => group.cause)).toEqual(["noCalls", "noTimeout", "noDependency"]);
  });

  it("does not count a rule that fired or passed as silent", () => {
    const reading = readCoverage(
      analysis([entry("SPOF", "Fired"), entry("CYCLE", "NotApplicable", "rule.na.CYCLE.noCalls")]),
    );

    expect(reading?.muted).toEqual([{ cause: "noCalls", codes: ["CYCLE"] }]);
  });

  it("keeps a reason it does not recognise instead of dropping the rule", () => {
    const reading = readCoverage(
      analysis([
        entry("NEW_RULE", "NotApplicable", "rule.na.NEW_RULE.somethingNobodyWroteYet"),
        entry("OLD_RULE", "NotApplicable", null),
      ]),
    );

    expect(reading?.muted).toEqual([{ cause: OTHER_CAUSE, codes: ["NEW_RULE", "OLD_RULE"] }]);
  });
});

describe("causeOf", () => {
  it("reads the cause off the tail of the sentence the rule stood down with", () => {
    expect(causeOf({ key: "rule.na.UNDERSIZED_COMPONENT.noLoad" })).toBe("noLoad");
    expect(causeOf({ key: "rule.na.NO_OBSERVABILITY.singleComponent" })).toBe("singleComponent");
  });

  it("falls back rather than inventing a cause", () => {
    expect(causeOf(null)).toBe(OTHER_CAUSE);
    expect(causeOf(undefined)).toBe(OTHER_CAUSE);
    expect(causeOf({ key: "finding.SPOF.message" })).toBe(OTHER_CAUSE);
    expect(causeOf({ key: "rule.na.SPOF.brandNew" })).toBe(OTHER_CAUSE);
  });
});

describe("the four recorded references", () => {
  it("reads every one of them, twenty-five rules at a time", () => {
    for (const name of REFERENCES) {
      const reading = readCoverage(recorded(name));

      expect(reading, name).not.toBeNull();
      expect(reading!.total, name).toBe(25);
      expect(reading!.runs.reduce((sum, run) => sum + run.count, 0), name).toBe(25);
      expect(reading!.runs.map((run) => run.state), name).toEqual(
        STATE_ORDER.filter((state) => reading!.runs.some((run) => run.state === state)),
      );
    }
  });

  it("accounts for every silent rule exactly once", () => {
    for (const name of REFERENCES) {
      const reading = readCoverage(recorded(name))!;
      const silent = reading.runs.find((run) => run.state === "NotApplicable")?.count ?? 0;
      const grouped = reading.muted.flatMap((group) => group.codes);

      expect(grouped, name).toHaveLength(silent);
      expect(new Set(grouped).size, name).toBe(silent);
    }
  });

  it("never leaves a recorded reason in the fallback group", () => {
    for (const name of REFERENCES) {
      expect(readCoverage(recorded(name))!.muted.map((group) => group.cause), name).not.toContain(OTHER_CAUSE);
    }
  });

  it("says of minimal-api-db exactly what the panel will print", () => {
    const reading = readCoverage(recorded("minimal-api-db"))!;

    expect(reading.runs).toEqual([
      { state: "Fired", count: 1 },
      { state: "Passed", count: 12 },
      { state: "NotApplicable", count: 12 },
    ]);

    expect(reading.muted).toEqual([
      { cause: "noLoad", codes: ["TRAFFIC_REJECTED", "MISSING_CDN", "UNDERSIZED_COMPONENT", "OVERPROVISIONED"] },
      { cause: "noQueue", codes: ["QUEUE_NO_DLQ", "QUEUE_NO_CONSUMER"] },
      { cause: "noTrafficAtDoor", codes: ["NO_RATE_LIMIT", "UNAUTHENTICATED_ENTRY"] },
      { cause: "noAsyncCall", codes: ["ASYNC_NO_QUEUE"] },
      { cause: "noCache", codes: ["CACHE_AS_STORE"] },
      { cause: "noExternalCall", codes: ["EXTERNAL_NO_FALLBACK"] },
      { cause: "noRetries", codes: ["RETRY_AMPLIFICATION"] },
    ]);
  });
});

const written = Object.keys(es).filter((key) => key.startsWith("rule.na."));

describe("the words the bar reads off the state, which check:i18n cannot see either", () => {
  it("has a line for every run the bar can draw, in both languages", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const state of STATE_ORDER) {
        const key = `ui.analysis.coverage.${state === "NotApplicable" ? "notApplicable" : state.toLowerCase()}`;

        expect(t(key, { count: 1 }), `${key} in ${locale}`).not.toBe(key);
        expect(t(key, { count: 7 }), `${key} in ${locale}`).toContain("7");
      }
    }
  });
});

describe("the table of causes, which check:i18n cannot see", () => {
  it("covers every sentence the analyzer can stand a rule down with", () => {
    expect(written.length).toBeGreaterThan(0);

    for (const key of written) {
      expect(causeOf({ key }), `${key} has no cause of its own`).not.toBe(OTHER_CAUSE);
    }
  });

  it("names no cause the analyzer never sends", () => {
    const sent = new Set(written.map((key) => causeOf({ key })));

    for (const cause of MUTED_CAUSES) expect(sent, `${cause} is written for nobody`).toContain(cause);
  });

  it("carries what is missing and what to do about it, in both languages", () => {
    for (const cause of ALL_CAUSES) {
      for (const catalogue of [es, en] as Record<string, unknown>[]) {
        expect(missingKey(cause) in catalogue, `${missingKey(cause)} missing`).toBe(true);
        expect(actionKey(cause) in catalogue, `${actionKey(cause)} missing`).toBe(true);
      }
    }
  });

  it("asks the catalogue for nothing it has to fill in", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const cause of ALL_CAUSES) {
        for (const key of [missingKey(cause), actionKey(cause)]) {
          const sentence = t(key);

          expect(sentence, `${key} in ${locale}`).not.toBe(key);
          expect(sentence, `${key} in ${locale} asks for an argument`).not.toMatch(/\{[A-Za-z]/);
        }
      }
    }
  });
});
