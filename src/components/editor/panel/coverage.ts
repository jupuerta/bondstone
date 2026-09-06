import type { AnalysisResult, LocalizedText, RuleState } from "../../../types/architecture";

export const STATE_ORDER: RuleState[] = ["Fired", "Passed", "NotApplicable"];

export const MUTED_CAUSES = [
  "singleComponent",
  "noCalls",
  "noSyncCall",
  "noAsyncCall",
  "noDependency",
  "noLoad",
  "lowTraffic",
  "noLimit",
  "noTrafficAtDoor",
  "noTimeout",
  "noRetries",
  "noQueue",
  "noCache",
  "noDatabase",
  "noStore",
  "noExternalCall",
  "nothingExposed",
  "nothingToWatch",
] as const;

export const OTHER_CAUSE = "other";

export type MutedCause = (typeof MUTED_CAUSES)[number] | typeof OTHER_CAUSE;

const REASON_PREFIX = "rule.na.";

export interface CoverageRun {
  state: RuleState;
  count: number;
}

export interface MutedGroup {
  cause: MutedCause;
  codes: string[];
}

export interface CoverageReading {
  total: number;
  runs: CoverageRun[];
  muted: MutedGroup[];
}

export function causeOf(reason: LocalizedText | null | undefined): MutedCause {
  if (!reason || !reason.key.startsWith(REASON_PREFIX)) return OTHER_CAUSE;

  const tail = reason.key.slice(reason.key.lastIndexOf(".") + 1);

  return (MUTED_CAUSES as readonly string[]).includes(tail) ? (tail as MutedCause) : OTHER_CAUSE;
}

export function missingKey(cause: MutedCause): string {
  return `ui.coverage.missing.${cause}`;
}

export function actionKey(cause: MutedCause): string {
  return `ui.coverage.action.${cause}`;
}

export function readCoverage(result: AnalysisResult | null): CoverageReading | null {
  const coverage = result?.coverage;
  if (coverage === undefined || coverage.length === 0) return null;

  const runs = STATE_ORDER.map((state) => ({
    state,
    count: coverage.filter((entry) => entry.state === state).length,
  })).filter((run) => run.count > 0);

  const seen: MutedCause[] = [];
  const codes = new Map<MutedCause, string[]>();

  for (const entry of coverage) {
    if (entry.state !== "NotApplicable") continue;

    const cause = causeOf(entry.reason);
    const listed = codes.get(cause);

    if (listed === undefined) {
      codes.set(cause, [entry.code]);
      seen.push(cause);
    } else {
      listed.push(entry.code);
    }
  }

  const muted = seen
    .map((cause) => ({ cause, codes: codes.get(cause) ?? [] }))
    .sort((a, b) => b.codes.length - a.codes.length || seen.indexOf(a.cause) - seen.indexOf(b.cause));

  return { total: coverage.length, runs, muted };
}
