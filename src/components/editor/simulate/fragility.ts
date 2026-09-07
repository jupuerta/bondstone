import type { ElementState, FragilityMap, FragilityRow } from "../../../types/architecture";

export type FragilityLatency = { kind: "noRoute" } | { kind: "slower"; from: number; to: number };

export interface FragilityBar {
  row: FragilityRow;
  downShare: number;
  degradedShare: number;
  contained: boolean;
  latency: FragilityLatency | null;
}

export function fragilityBars(map: FragilityMap | null): FragilityBar[] {
  if (!map || map.componentsTotal === 0) return [];

  return map.rows.map((row) => ({
    row,
    downShare: row.componentsDown / map.componentsTotal,
    degradedShare: row.componentsDegraded / map.componentsTotal,
    contained: row.componentsDown > 0 && row.entryPointsAffected === 0 && row.requestsAffected === 0,
    latency: latencyOf(row, map.nominalLatencyMs),
  }));
}

function latencyOf(row: FragilityRow, nominalMs: number): FragilityLatency | null {
  if (row.faultedLatencyMs === null) return { kind: "noRoute" };
  if (row.faultedLatencyMs > nominalMs) return { kind: "slower", from: nominalMs, to: row.faultedLatencyMs };
  return null;
}

export function worstBar(bars: FragilityBar[]): FragilityBar | null {
  return bars.length > 0 ? bars[0] : null;
}

export function containedBar(bars: FragilityBar[]): FragilityBar | null {
  return bars.find((bar) => bar.contained) ?? null;
}

export function fragilityTint(row: FragilityRow, name: string): Map<string, ElementState> {
  const tinted = new Map<string, ElementState>();

  for (const element of row.degraded) tinted.set(element.id, tint(row, name, element.type, element.id, "Degraded"));
  for (const element of row.down) tinted.set(element.id, tint(row, name, element.type, element.id, "Down"));

  return tinted;
}

function tint(
  row: FragilityRow,
  name: string,
  type: ElementState["type"],
  id: string,
  state: Exclude<ElementState["state"], "Healthy">,
): ElementState {
  return {
    type,
    id,
    state,
    reason: { key: "ui.fragility.tint", args: { name } },
    mechanism: null,
    causedBy: id === row.nodeId ? null : { type: "Node", id: row.nodeId },
    propagationStep: 0,
    utilization: null,
  };
}
