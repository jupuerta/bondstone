import type { ElementType, Fault, FaultKind } from "../types/architecture";
import { ZONES, type Zone } from "./zone";

const FAULT_PARAM = "fault";

const TARGETS: Record<string, ElementType> = { node: "Node", connection: "Connection" };
const KINDS: FaultKind[] = ["down", "degraded", "latency"];

const ZONE_TARGET = "zone";

const DASHLESS = /^[0-9a-f]{32}$/i;
const DASHED = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// A zone has no id to match, so it travels beside the faults rather than inside one. Everything
// downstream that only understands elements keeps reading Fault[]; the split happens once, on the
// way to the API.
export interface ZoneFault {
  target: "Zone";
  id: Zone;
  kind: "down";
}

export type ScenarioFault = Fault | ZoneFault;

export function isZoneFault(fault: ScenarioFault): fault is ZoneFault {
  return fault.target === "Zone";
}

export function nodeFaults(faults: ScenarioFault[]): Fault[] {
  return faults.filter((fault): fault is Fault => !isZoneFault(fault));
}

export function downZones(faults: ScenarioFault[]): Zone[] {
  return canonical(faults)
    .filter(isZoneFault)
    .map((fault) => fault.id);
}

export function zoneFault(zone: Zone): ZoneFault {
  return { target: "Zone", id: zone, kind: "down" };
}

function withoutDashes(id: string): string {
  return id.replace(/-/g, "");
}

function withDashes(id: string): string | null {
  if (DASHED.test(id)) return id.toLowerCase();
  if (!DASHLESS.test(id)) return null;

  const hex = id.toLowerCase();
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export function faultToParam(fault: ScenarioFault): string {
  if (isZoneFault(fault)) return `${ZONE_TARGET}:${fault.id}:${fault.kind}`;

  const head = `${fault.target.toLowerCase()}:${withoutDashes(fault.id)}:${fault.kind}`;
  return fault.kind === "latency" ? `${head}:${fault.value ?? 0}` : head;
}

export function parseFaultParam(raw: string): ScenarioFault | null {
  const [target, id, kind, value] = raw.split(":");

  if ((target ?? "").toLowerCase() === ZONE_TARGET) {
    const zone = ZONES.find((candidate) => candidate === id);
    return zone && kind === "down" ? zoneFault(zone) : null;
  }

  const elementType = TARGETS[(target ?? "").toLowerCase()];
  const canonicalId = id ? withDashes(id) : null;
  const faultKind = KINDS.find((candidate) => candidate === kind);

  if (!elementType || !canonicalId || !faultKind) return null;
  if (faultKind === "degraded" && elementType !== "Node") return null;

  if (faultKind === "latency") {
    const ms = Number(value);
    if (!Number.isFinite(ms) || ms <= 0) return null;
    return { target: elementType, id: canonicalId, kind: faultKind, value: Math.round(ms) };
  }

  return { target: elementType, id: canonicalId, kind: faultKind };
}

export function scenarioFromSearch(search: string): ScenarioFault[] {
  return new URLSearchParams(search)
    .getAll(FAULT_PARAM)
    .map(parseFaultParam)
    .filter((fault): fault is ScenarioFault => fault !== null);
}

export function searchWithScenario(search: string, faults: ScenarioFault[]): string {
  const params = new URLSearchParams(search);
  params.delete(FAULT_PARAM);

  for (const fault of canonical(faults)) params.append(FAULT_PARAM, faultToParam(fault));

  const query = params.toString();
  return query ? `?${query}` : "";
}

export function canonical<T extends ScenarioFault>(faults: T[]): T[] {
  return [...faults].sort(
    (a, b) => a.target.localeCompare(b.target) || a.id.localeCompare(b.id) || a.kind.localeCompare(b.kind),
  );
}

export function scenarioKey(faults: ScenarioFault[]): string {
  return canonical(faults).map(faultToParam).join("|");
}

export function sameFault(a: ScenarioFault, b: ScenarioFault): boolean {
  return a.target === b.target && a.id === b.id && a.kind === b.kind;
}
