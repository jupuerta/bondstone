import type { NodeType, ProtocolType } from "../types/architecture";

const BROKERS: ReadonlySet<NodeType> = new Set<NodeType>(["Queue", "Topic"]);

export function protocolOf(source: NodeType | undefined, target: NodeType | undefined): ProtocolType {
  if (target === "Observability") return "Http";
  if (target === "Database") return "Sql";
  if ((source && BROKERS.has(source)) || (target && BROKERS.has(target))) return "Amqp";
  return "Http";
}
