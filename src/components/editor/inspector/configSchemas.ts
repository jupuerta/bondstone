import type { NodeType } from "../../../types/architecture";

export type FieldKind = "number" | "text" | "boolean" | "tristate";

export interface ConfigField {
  key: string;
  label: string;
  kind: FieldKind;
  entryPointOnly?: boolean;
  min?: number;
  max?: number;
  step?: number;
}

export const CONFIG_SCHEMAS: Record<NodeType, ConfigField[]> = {
  Api: [
    { key: "requestsPerSecond", label: "ui.config.requestsPerSecond", kind: "number", entryPointOnly: true },
    { key: "rateLimit", label: "ui.config.rateLimit", kind: "number", min: 1 },
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1 },
    { key: "authentication", label: "ui.config.authentication", kind: "tristate" },
  ],
  Database: [
    { key: "type", label: "ui.config.engine", kind: "text" },
    { key: "replicas", label: "ui.config.replicas", kind: "number" },
    { key: "readReplicas", label: "ui.config.readReplicas", kind: "number" },
  ],
  Service: [
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1 },
    { key: "authentication", label: "ui.config.authentication", kind: "tristate" },
  ],
  Queue: [
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1 },
    { key: "maxRetries", label: "ui.config.maxRetries", kind: "number" },
    { key: "deadLetterQueue", label: "ui.config.deadLetterQueue", kind: "boolean" },
  ],
  Cache: [
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1 },
    { key: "hitRatio", label: "ui.config.hitRatio", kind: "number", min: 0, max: 1, step: 0.01 },
  ],
  Gateway: [
    { key: "requestsPerSecond", label: "ui.config.requestsPerSecond", kind: "number", entryPointOnly: true },
    { key: "rateLimit", label: "ui.config.rateLimit", kind: "number", min: 1 },
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1 },
    { key: "authentication", label: "ui.config.authentication", kind: "tristate" },
  ],
  Observability: [],
  Topic: [{ key: "instances", label: "ui.config.instances", kind: "number", min: 1 }],
  Worker: [{ key: "instances", label: "ui.config.instances", kind: "number", min: 1 }],
  ObjectStorage: [{ key: "replicated", label: "ui.config.replicated", kind: "boolean" }],
  ExternalService: [],
  LoadBalancer: [
    { key: "requestsPerSecond", label: "ui.config.requestsPerSecond", kind: "number", entryPointOnly: true },
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1 },
    { key: "healthCheck", label: "ui.config.healthCheck", kind: "boolean" },
  ],
  Client: [{ key: "requestsPerSecond", label: "ui.config.requestsPerSecond", kind: "number" }],
  Cdn: [{ key: "hitRatio", label: "ui.config.hitRatio", kind: "number", min: 0, max: 1, step: 0.01 }],
  Scheduler: [{ key: "jobsPerSecond", label: "ui.config.jobsPerSecond", kind: "number" }],
  IdentityProvider: [],
};
