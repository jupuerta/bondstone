import type { ConnectionLoad } from "../../../types/architecture";

export type FlowTone = "quiet" | "busiest";

export interface CallFlow {
  rps: number;
  share: number;
  tone: FlowTone;
}

export function callFlows(callLoad: readonly ConnectionLoad[] | undefined | null): Map<string, CallFlow> {
  const flows = new Map<string, CallFlow>();
  if (!callLoad || callLoad.length === 0) return flows;

  const busiest = callLoad.reduce((top, call) => Math.max(top, call.rps), 0);
  if (busiest <= 0) return flows;

  const unanimous = callLoad.every((call) => call.rps >= busiest);

  for (const call of callLoad) {
    const share = Math.min(Math.max(call.rps, 0) / busiest, 1);

    flows.set(call.connectionId, {
      rps: call.rps,
      share: Math.round(share * 100) / 100,
      tone: share >= 1 && !unanimous ? "busiest" : "quiet",
    });
  }

  return flows;
}
