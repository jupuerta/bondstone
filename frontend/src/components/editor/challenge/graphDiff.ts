import type { ArchitectureDto, LocalizedText, NodeType, ResilienceConfiguration } from "../../../types/architecture";
import { NODE_META } from "../palette/nodeMeta";

export type ComparableGraph = Pick<ArchitectureDto, "nodes" | "connections">;

type ComparableNode = ComparableGraph["nodes"][number];
type ComparableCall = ComparableGraph["connections"][number];

export type DifferenceKind =
  | "componentInterposed"
  | "componentAdded"
  | "componentRemoved"
  | "componentRetyped"
  | "componentPairedByType"
  | "callAdded"
  | "callRemoved"
  | "callAsync"
  | "callSync"
  | "callSetting"
  | "componentSetting";

const KIND_ORDER: DifferenceKind[] = [
  "componentInterposed",
  "componentAdded",
  "componentRemoved",
  "componentRetyped",
  "componentPairedByType",
  "callAdded",
  "callRemoved",
  "callAsync",
  "callSync",
  "callSetting",
  "componentSetting",
];

export interface Difference {
  kind: DifferenceKind;
  text: LocalizedText;
}

export const REFERENCE_VOICE = "ui.compare.diff";
export const CHANGE_VOICE = "ui.change.diff";

const COMPONENT_SETTINGS: Record<string, string> = {
  requestsPerSecond: "ui.config.requestsPerSecond",
  jobsPerSecond: "ui.config.jobsPerSecond",
  rateLimit: "ui.config.rateLimit",
  instances: "ui.config.instances",
  authentication: "ui.config.authentication",
  healthCheck: "ui.config.healthCheck",
  replicas: "ui.config.replicas",
  replicated: "ui.config.replicated",
  readReplicas: "ui.config.readReplicas",
  maxRetries: "ui.config.maxRetries",
  deadLetterQueue: "ui.config.deadLetterQueue",
  hitRatio: "ui.config.hitRatio",
  type: "ui.config.engine",
};

const CALL_SETTINGS: Array<keyof ResilienceConfiguration> = [
  "timeoutSeconds",
  "retries",
  "circuitBreaker",
  "fallback",
];

const CALL_SETTING_LABEL: Record<string, string> = {
  timeoutSeconds: "ui.inspector.timeoutSeconds",
  retries: "ui.inspector.retries",
  circuitBreaker: "ui.inspector.circuitBreaker",
  fallback: "ui.inspector.fallback",
};

interface Pair {
  yours: ComparableNode;
  theirs: ComparableNode;
  byName: boolean;
}

export function compareGraphs(
  yours: ComparableGraph,
  theirs: ComparableGraph,
  voice: string = REFERENCE_VOICE,
): Difference[] {
  const pairs = matchComponents(yours.nodes, theirs.nodes);

  const slotOfYours = new Map(pairs.map((pair, index) => [pair.yours.id, index]));
  const slotOfTheirs = new Map(pairs.map((pair, index) => [pair.theirs.id, index]));

  const mine = callsBySlot(yours.connections, slotOfYours);
  const other = callsBySlot(theirs.connections, slotOfTheirs);

  const ambiguous = new Set([...mine.ambiguous, ...other.ambiguous]);
  const yourCalls = without(mine.calls, ambiguous);
  const theirCalls = without(other.calls, ambiguous);

  const differences: Difference[] = [];

  const explained = interposed(theirs, pairs, slotOfTheirs, yourCalls, differences, voice);

  for (const node of theirs.nodes) {
    if (slotOfTheirs.has(node.id) || explained.components.has(node.id)) continue;
    differences.push({
      kind: "componentAdded",
      text: { key: `${voice}.componentAdded`, args: { name: node.name, type: typeName(node.type) } },
    });
  }

  for (const node of yours.nodes) {
    if (slotOfYours.has(node.id)) continue;
    differences.push({
      kind: "componentRemoved",
      text: { key: `${voice}.componentRemoved`, args: { name: node.name, type: typeName(node.type) } },
    });
  }

  for (const pair of pairs) {
    if (pair.yours.type !== pair.theirs.type) {
      differences.push({
        kind: "componentRetyped",
        text: {
          key: `${voice}.componentRetyped`,
          args: {
            name: pair.theirs.name,
            type: typeName(pair.theirs.type),
            yourType: typeName(pair.yours.type),
          },
        },
      });
    }

    if (!pair.byName && pair.yours.name.trim() !== pair.theirs.name.trim()) {
      differences.push({
        kind: "componentPairedByType",
        text: {
          key: `${voice}.componentPairedByType`,
          args: { yourName: pair.yours.name, name: pair.theirs.name, type: typeName(pair.theirs.type) },
        },
      });
    }

    componentSettings(pair, differences, voice);
  }

  for (const slot of theirCalls.keys()) {
    if (yourCalls.has(slot)) continue;
    differences.push({ kind: "callAdded", text: { key: `${voice}.callAdded`, args: ends(pairs, slot, true) } });
  }

  for (const slot of yourCalls.keys()) {
    if (theirCalls.has(slot) || explained.calls.has(slot)) continue;
    differences.push({
      kind: "callRemoved",
      text: { key: `${voice}.callRemoved`, args: ends(pairs, slot, false) },
    });
  }

  for (const [slot, theirCall] of theirCalls) {
    const yourCall = yourCalls.get(slot);
    if (!yourCall) continue;
    callDifferences(pairs, slot, yourCall, theirCall, differences, voice);
  }

  return differences.sort((a, b) => KIND_ORDER.indexOf(a.kind) - KIND_ORDER.indexOf(b.kind));
}

function matchComponents(yours: readonly ComparableNode[], theirs: readonly ComparableNode[]): Pair[] {
  const pairs: Pair[] = [];
  const takenYours = new Set<string>();
  const takenTheirs = new Set<string>();

  const yoursByName = group(yours, (node) => node.name.trim().toLowerCase());
  const theirsByName = group(theirs, (node) => node.name.trim().toLowerCase());

  for (const node of theirs) {
    const key = node.name.trim().toLowerCase();
    const mine = yoursByName.get(key);
    if (mine?.length === 1 && theirsByName.get(key)?.length === 1) {
      pairs.push({ yours: mine[0], theirs: node, byName: true });
      takenYours.add(mine[0].id);
      takenTheirs.add(node.id);
    }
  }

  const restYours = group(yours.filter((node) => !takenYours.has(node.id)), (node) => node.type);
  const restTheirs = group(theirs.filter((node) => !takenTheirs.has(node.id)), (node) => node.type);

  for (const [type, other] of restTheirs) {
    const mine = restYours.get(type);
    if (mine?.length === 1 && other.length === 1) {
      pairs.push({ yours: mine[0], theirs: other[0], byName: false });
    }
  }

  return pairs;
}

function callsBySlot(
  calls: readonly ComparableCall[],
  slots: Map<string, number>,
): { calls: Map<string, ComparableCall>; ambiguous: Set<string> } {
  const found = new Map<string, ComparableCall[]>();

  for (const call of calls) {
    const from = slots.get(call.sourceNodeId);
    const to = slots.get(call.targetNodeId);
    if (from === undefined || to === undefined) continue;

    const key = `${from}>${to}`;
    found.set(key, [...(found.get(key) ?? []), call]);
  }

  return {
    calls: new Map([...found].filter(([, list]) => list.length === 1).map(([key, list]) => [key, list[0]])),
    ambiguous: new Set([...found].filter(([, list]) => list.length > 1).map(([key]) => key)),
  };
}

function without(calls: Map<string, ComparableCall>, dropped: Set<string>): Map<string, ComparableCall> {
  return new Map([...calls].filter(([key]) => !dropped.has(key)));
}

function interposed(
  theirs: ComparableGraph,
  pairs: Pair[],
  slotOfTheirs: Map<string, number>,
  yourCalls: Map<string, ComparableCall>,
  into: Difference[],
  voice: string,
): { components: Set<string>; calls: Set<string> } {
  const components = new Set<string>();
  const calls = new Set<string>();

  for (const node of theirs.nodes) {
    if (slotOfTheirs.has(node.id)) continue;

    const inbound = theirs.connections.filter((call) => call.targetNodeId === node.id);
    const outbound = theirs.connections.filter((call) => call.sourceNodeId === node.id);
    if (inbound.length !== 1 || outbound.length !== 1) continue;

    const before = slotOfTheirs.get(inbound[0].sourceNodeId);
    const after = slotOfTheirs.get(outbound[0].targetNodeId);
    if (before === undefined || after === undefined) continue;

    const direct = `${before}>${after}`;
    if (!yourCalls.has(direct)) continue;

    into.push({
      kind: "componentInterposed",
      text: {
        key: `${voice}.componentInterposed`,
        args: {
          name: node.name,
          type: typeName(node.type),
          from: pairs[before].theirs.name,
          to: pairs[after].theirs.name,
        },
      },
    });

    components.add(node.id);
    calls.add(direct);
  }

  return { components, calls };
}

function componentSettings(pair: Pair, into: Difference[], voice: string) {
  const mine = pair.yours.configuration ?? {};
  const other = pair.theirs.configuration ?? {};

  for (const key of Object.keys(COMPONENT_SETTINGS)) {
    if (!(key in mine) && !(key in other)) continue;

    const a = settingValue(mine[key]);
    const b = settingValue(other[key]);
    if (same(a, b)) continue;

    into.push({
      kind: "componentSetting",
      text: {
        key: `${voice}.componentSetting`,
        args: {
          name: pair.theirs.name,
          setting: { key: COMPONENT_SETTINGS[key] },
          yours: a,
          theirs: b,
        },
      },
    });
  }
}

function callDifferences(
  pairs: Pair[],
  slot: string,
  yourCall: ComparableCall,
  theirCall: ComparableCall,
  into: Difference[],
  voice: string,
) {
  const named = ends(pairs, slot, true);

  if (yourCall.synchronous !== theirCall.synchronous) {
    into.push({
      kind: theirCall.synchronous ? "callSync" : "callAsync",
      text: { key: theirCall.synchronous ? `${voice}.callSync` : `${voice}.callAsync`, args: named },
    });
  }

  if (yourCall.synchronous !== theirCall.synchronous) return;

  const mine = yourCall.configuration ?? {};
  const other = theirCall.configuration ?? {};

  for (const key of CALL_SETTINGS) {
    const flag = key === "circuitBreaker" || key === "fallback";
    const a = flag ? booleanValue(mine[key]) : settingValue(mine[key]);
    const b = flag ? booleanValue(other[key]) : settingValue(other[key]);
    if (same(a, b)) continue;

    into.push({
      kind: "callSetting",
      text: {
        key: `${voice}.callSetting`,
        args: { ...named, setting: { key: CALL_SETTING_LABEL[key] }, yours: a, theirs: b },
      },
    });
  }
}

function settingValue(value: unknown): string | LocalizedText {
  if (value === undefined || value === null) return { key: "ui.config.tristate.undeclared" };
  if (typeof value === "boolean") return { key: value ? "ui.config.tristate.yes" : "ui.config.tristate.no" };
  if (typeof value === "number") return { key: "ui.compare.value", args: { value } };
  return String(value);
}

function booleanValue(value: unknown): LocalizedText {
  return { key: value === true ? "ui.config.tristate.yes" : "ui.config.tristate.no" };
}

function same(a: string | LocalizedText, b: string | LocalizedText): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

function ends(pairs: Pair[], slot: string, theirs: boolean): Record<string, string> {
  const [from, to] = slot.split(">").map(Number);
  return theirs
    ? { from: pairs[from].theirs.name, to: pairs[to].theirs.name }
    : { from: pairs[from].yours.name, to: pairs[to].yours.name };
}

function typeName(type: NodeType): LocalizedText {
  return { key: NODE_META[type].label };
}

function group<T, K>(items: readonly T[], keyOf: (item: T) => K): Map<K, T[]> {
  const grouped = new Map<K, T[]>();
  for (const item of items) grouped.set(keyOf(item), [...(grouped.get(keyOf(item)) ?? []), item]);
  return grouped;
}
