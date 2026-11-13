import type { ConnectionDto, NodeConfiguration, NodeDto, NodeType } from "../../../types/architecture";
import { CONFIG_SCHEMAS } from "../inspector/configSchemas";

export function isTelemetryCall(source: NodeType | undefined, target: NodeType | undefined): boolean {
  return source === "Observability" || target === "Observability";
}

export function isEntryPoint(nodeId: string, nodes: readonly NodeDto[], connections: readonly ConnectionDto[]): boolean {
  const typeOf = (id: string) => nodes.find((node) => node.id === id)?.type;
  return !connections.some(
    (call) => call.targetNodeId === nodeId && !isTelemetryCall(typeOf(call.sourceNodeId), typeOf(call.targetNodeId)),
  );
}

export function declaredTrafficFields(type: NodeType): string[] {
  return CONFIG_SCHEMAS[type].filter((field) => field.entryPointOnly).map((field) => field.key);
}

export function staleTrafficFields(source: NodeType, target: NodeType, configuration: NodeConfiguration | null | undefined): string[] {
  if (isTelemetryCall(source, target)) return [];
  return declaredTrafficFields(target).filter((key) => key in (configuration ?? {}));
}

export function forgetDeclaredTraffic(nodes: NodeDto[], source: string, target: string): NodeDto[] {
  const from = nodes.find((node) => node.id === source);
  const to = nodes.find((node) => node.id === target);
  if (!from || !to) return nodes;

  const configuration: NodeConfiguration = to.configuration ?? {};
  const stale = staleTrafficFields(from.type, to.type, configuration);
  if (stale.length === 0) return nodes;

  const kept = Object.fromEntries(Object.entries(configuration).filter(([key]) => !stale.includes(key)));
  return nodes.map((node) => (node.id === target ? { ...node, configuration: kept } : node));
}
