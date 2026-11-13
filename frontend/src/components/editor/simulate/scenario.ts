import { isZoneFault, sameFault, zoneFault, type ScenarioFault } from "../../../domain/scenarioUrl";
import type { Zone } from "../../../domain/zone";
import type { ElementType, Fault, FaultKind, FragilityRow } from "../../../types/architecture";

export const DEFAULT_LATENCY_MS = 2_000;

export const FAULT_KINDS: readonly FaultKind[] = ["down", "degraded", "latency"];

export function kindsFor(target: ElementType): FaultKind[] {
  return FAULT_KINDS.filter((kind) => kind !== "degraded" || target === "Node");
}

export function faultOn(faults: readonly ScenarioFault[], target: ElementType, id: string, kind: FaultKind): Fault | null {
  const found = faults.find((fault) => !isZoneFault(fault) && fault.target === target && fault.id === id && fault.kind === kind);
  return found && !isZoneFault(found) ? found : null;
}

export function addFault(faults: readonly ScenarioFault[], target: ElementType, id: string, kind: FaultKind): ScenarioFault[] {
  if (faultOn(faults, target, id, kind)) return [...faults];
  const fault: Fault = kind === "latency" ? { target, id, kind, value: DEFAULT_LATENCY_MS } : { target, id, kind };
  return [...faults, fault];
}

export function removeFault(faults: readonly ScenarioFault[], fault: ScenarioFault): ScenarioFault[] {
  return faults.filter((candidate) => !sameFault(candidate, fault));
}

export function setLatency(faults: readonly ScenarioFault[], fault: ScenarioFault, value: number): ScenarioFault[] {
  const ms = Math.max(1, Math.round(value));
  return faults.map((candidate) => (sameFault(candidate, fault) && !isZoneFault(candidate) ? { ...candidate, value: ms } : candidate));
}

export function toggleZone(faults: readonly ScenarioFault[], zone: Zone): ScenarioFault[] {
  const fault = zoneFault(zone);
  return faults.some((candidate) => sameFault(candidate, fault)) ? removeFault(faults, fault) : [...faults, fault];
}

export function pruneFaults(faults: readonly ScenarioFault[], alive: ReadonlySet<string>): { kept: ScenarioFault[]; dropped: number } {
  const kept = faults.filter((fault) => isZoneFault(fault) || alive.has(fault.id));
  return { kept, dropped: faults.length - kept.length };
}

export function loadedRow(faults: readonly Fault[], row: FragilityRow): boolean {
  return faults.length === 1 && faults[0].target === "Node" && faults[0].kind === "down" && faults[0].id === row.nodeId;
}

export function rowScenario(row: FragilityRow): ScenarioFault[] {
  return [{ target: "Node", id: row.nodeId, kind: "down" }];
}
