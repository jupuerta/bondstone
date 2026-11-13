import { describe, expect, it } from "vitest";
import es from "../../../i18n/es.json";
import type { RuleFinding } from "../../../types/architecture";
import { MECHANISMS } from "../../../domain/mechanisms";
import { PATTERNS, PATTERN_FOR_CODE, PATTERN_IDS, marksOf, type PatternId } from "../patterns/patterns";
import { NO_PATTERN, PATTERN_FOR_SUGGESTION, patternForFinding } from "./findingPattern";

const suggestionKeys = Object.keys(es).filter(
  (key) => key.startsWith("finding.") && key.split(".").includes("suggestion"),
);

const codeOf = (key: string) => key.split(".")[1];

const DRAWN_CODES = new Set([
  ...Object.keys(PATTERN_FOR_CODE),
  ...Object.values(MECHANISMS).flatMap((sheet) => sheet.rules),
]);

const NO_FINDING_ASKS_FOR: PatternId[] = ["retried-call", "read-replicas", "breaker-on-call"];

const covered = suggestionKeys.filter((key) => DRAWN_CODES.has(codeOf(key)));

const marksIn = (scene: PatternId, panel: "before" | "after") =>
  marksOf(PATTERNS[scene][panel]).map((mark) => mark.mark);

const finding = (suggestion: string | null): RuleFinding => ({
  code: "SPOF",
  area: "Availability",
  severity: "Critical",
  message: { key: "finding.SPOF.shared.message" },
  explanation: { key: "finding.SPOF.explanation.measured" },
  suggestion: suggestion === null ? null : { key: suggestion },
  elements: [],
});

describe("the drawing under a finding (V16 C8, wired; V19 close, widened)", () => {
  it("has an answer for every sentence a code with a drawing can say", () => {
    expect(covered.length).toBeGreaterThan(PATTERN_IDS.length);
    expect([...Object.keys(PATTERN_FOR_SUGGESTION), ...NO_PATTERN].sort()).toEqual([...covered].sort());
  });

  it("never lists a sentence twice, once as drawn and once as not", () => {
    expect(Object.keys(PATTERN_FOR_SUGGESTION).filter((key) => NO_PATTERN.includes(key))).toEqual([]);
  });

  it("stays inside the codes some drawing was drawn for, by one table or the other", () => {
    for (const key of [...Object.keys(PATTERN_FOR_SUGGESTION), ...NO_PATTERN]) {
      expect(codeOf(key), key).toSatisfy((code: string) => DRAWN_CODES.has(code));
    }
  });

  it("puts every drawing on screen except the three no sentence asks for", () => {
    expect([...new Set(Object.values(PATTERN_FOR_SUGGESTION))].sort()).toEqual(
      PATTERN_IDS.filter((id) => !NO_FINDING_ASKS_FOR.includes(id)).sort(),
    );
  });

  it("only ever names a drawing the library has", () => {
    for (const id of Object.values(PATTERN_FOR_SUGGESTION)) expect(PATTERN_IDS).toContain(id);
  });

  it("disagrees with the code-level table where the sentence disagrees with it", () => {
    expect(PATTERN_FOR_CODE.SPOF).toBe("balanced-instances");
    expect(patternForFinding(finding("finding.SPOF.suggestion.database"))).toBeNull();

    expect(PATTERN_FOR_CODE.MISSING_RESILIENCE).toBe("queue-between");
    expect(patternForFinding(finding("finding.MISSING_RESILIENCE.exposedToSpof.suggestion"))).toBe(
      "external-fallback",
    );

    expect(PATTERN_FOR_CODE.SYNC_CHAIN).toBe("queue-between");
    expect(patternForFinding(finding("finding.SYNC_CHAIN.long.suggestion.read"))).toBe("cache-in-front");
    expect(patternForFinding(finding("finding.SYNC_CHAIN.long.suggestion"))).toBe("queue-between");

    expect(PATTERN_FOR_CODE.ASYNC_NO_QUEUE).toBe("queue-between");
    expect(patternForFinding(finding("finding.ASYNC_NO_QUEUE.unbuffered.suggestion"))).toBe("queue-between");
  });

  it("draws the three mechanisms whose sentence asks for exactly the move they show", () => {
    expect(patternForFinding(finding("finding.MISSING_RESILIENCE.noTimeout.suggestion"))).toBe("bounded-call");
    expect(patternForFinding(finding("finding.QUEUE_NO_DLQ.missing.suggestion"))).toBe("dead-letter-branch");
    expect(patternForFinding(finding("finding.NO_RATE_LIMIT.unlimited.suggestion"))).toBe("capped-entry");
    expect(patternForFinding(finding("finding.NO_RATE_LIMIT.unlimited.suggestion.api"))).toBe("capped-entry");
  });

  it("reaches codes the code-level table has no drawing for at all", () => {
    expect(PATTERN_FOR_CODE.QUEUE_NO_DLQ).toBeUndefined();
    expect(PATTERN_FOR_CODE.NO_RATE_LIMIT).toBeUndefined();
    expect(PATTERNS["dead-letter-branch"].codes).toEqual([]);
    expect(PATTERNS["capped-entry"].codes).toEqual([]);
  });

  it("refuses a drawing whose before panel the finding contradicts", () => {
    expect(marksIn("bounded-call", "before")).toEqual([]);
    expect(patternForFinding(finding("finding.TIMEOUT_TOO_SHORT.expires.suggestion"))).toBeNull();
    expect(patternForFinding(finding("finding.TIMEOUT_TOO_LONG.unbounded.suggestion"))).toBeNull();

    expect(marksIn("breaker-on-call", "before")).toEqual(["retries"]);
    expect(patternForFinding(finding("finding.MISSING_RESILIENCE.noTimeoutAndSpof.suggestion"))).not.toBe(
      "breaker-on-call",
    );

    expect(patternForFinding(finding("finding.CACHE_AS_STORE.noOrigin.suggestion"))).toBeNull();
    expect(patternForFinding(finding("finding.QUEUE_NO_CONSUMER.unconsumed.suggestion"))).toBeNull();
  });

  it("refuses a drawing whose after panel is not what the sentence asks for", () => {
    expect(marksIn("retried-call", "after")).toEqual(["retries"]);
    expect(patternForFinding(finding("finding.RETRY_AMPLIFICATION.suggestion"))).toBeNull();

    expect(patternForFinding(finding("finding.NO_RATE_LIMIT.unlimited.suggestion.service"))).toBeNull();
  });

  it("draws nothing where there is nothing to fix or nothing to fix it with", () => {
    expect(patternForFinding(finding(null))).toBeNull();
    expect(patternForFinding(finding("finding.UNAUTHENTICATED_ENTRY.undeclared.suggestion"))).toBeNull();
    expect(patternForFinding(finding("finding.CYCLE.detected.suggestion.synchronous"))).toBeNull();
    expect(patternForFinding(finding("finding.SPOF.suggestion.somethingNew"))).toBeNull();
  });
});
