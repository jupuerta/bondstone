import type { NodeType } from "../../../types/architecture";

export interface NodeMeta {
  label: string;
  help: string;
  avoid: string;
  rules: string[];
}

const meta = (type: NodeType, rules: string[]): NodeMeta => ({
  label: `ui.node.${type}`,
  help: `ui.node.${type}.help`,
  avoid: `ui.node.${type}.avoid`,
  rules,
});

export const NODE_META: Record<NodeType, NodeMeta> = {
  Gateway: meta("Gateway", ["SPOF", "NO_RATE_LIMIT", "UNAUTHENTICATED_ENTRY"]),
  LoadBalancer: meta("LoadBalancer", ["SPOF", "UNDERSIZED_COMPONENT"]),
  Api: meta("Api", ["UNAUTHENTICATED_ENTRY", "EXPOSED_DATABASE", "NO_RATE_LIMIT"]),
  Service: meta("Service", ["HIGH_COUPLING", "SYNC_CHAIN", "ASYNC_NO_QUEUE", "SHARED_DATABASE"]),
  Worker: meta("Worker", ["UNDERSIZED_COMPONENT", "RETRY_AMPLIFICATION", "UNAUTHENTICATED_ENTRY"]),
  Database: meta("Database", ["MISSING_CACHE", "EXPOSED_DATABASE", "SPOF", "SHARED_DATABASE"]),
  Cache: meta("Cache", ["MISSING_CACHE", "CACHE_AS_STORE", "UNAUTHENTICATED_ENTRY"]),
  ObjectStorage: meta("ObjectStorage", ["SPOF", "EXPOSED_DATABASE", "UNAUTHENTICATED_ENTRY", "SHARED_DATABASE"]),
  Queue: meta("Queue", ["QUEUE_NO_DLQ", "QUEUE_NO_CONSUMER", "ASYNC_NO_QUEUE", "RETRY_AMPLIFICATION", "SYNC_CHAIN", "UNAUTHENTICATED_ENTRY"]),
  Topic: meta("Topic", ["QUEUE_NO_CONSUMER", "SPOF", "UNAUTHENTICATED_ENTRY"]),
  Observability: meta("Observability", ["NO_OBSERVABILITY"]),
  ExternalService: meta("ExternalService", ["EXTERNAL_NO_FALLBACK", "MISSING_RESILIENCE"]),
  Client: meta("Client", ["UNAUTHENTICATED_ENTRY", "EXPOSED_DATABASE", "NO_RATE_LIMIT"]),
  Cdn: meta("Cdn", ["UNDERSIZED_COMPONENT", "SPOF"]),
  Scheduler: meta("Scheduler", ["UNDERSIZED_COMPONENT"]),
  IdentityProvider: meta("IdentityProvider", ["UNAUTHENTICATED_ENTRY", "SPOF"]),
};

export const NODE_GROUPS: Array<{ label: string; types: NodeType[] }> = [
  { label: "ui.palette.outside", types: ["Client", "ExternalService"] },
  { label: "ui.palette.edge", types: ["Gateway", "LoadBalancer", "Cdn", "IdentityProvider"] },
  { label: "ui.palette.compute", types: ["Api", "Service", "Worker"] },
  { label: "ui.palette.state", types: ["Database", "Cache", "ObjectStorage"] },
  { label: "ui.palette.flowOps", types: ["Queue", "Topic", "Scheduler", "Observability"] },
];

export const NODE_GROUP_OF = Object.fromEntries(NODE_GROUPS.flatMap((group) => group.types.map((type) => [type, group.label]))) as Record<NodeType, string>;

export const DND_NODE_TYPE_FORMAT = "application/bondstone-node-type";
