import type { NodeType } from "../../../types/architecture";
import type { TermId } from "../help/glossary";

export type FieldKind = "number" | "text" | "boolean" | "tristate";

export interface ConfigField {
  key: string;
  label: string;
  kind: FieldKind;
  term?: TermId;
  entryPointOnly?: boolean;
  min?: number;
  max?: number;
  step?: number;
}

export const CONFIG_SCHEMAS: Record<NodeType, ConfigField[]> = {
  Api: [
    { key: "requestsPerSecond", label: "ui.config.requestsPerSecond", kind: "number", entryPointOnly: true },
    { key: "rateLimit", label: "ui.config.rateLimit", kind: "number", min: 1, term: "rateLimit" },
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1, term: "instances" },
    { key: "authentication", label: "ui.config.authentication", kind: "tristate" },
  ],
  Database: [
    { key: "type", label: "ui.config.engine", kind: "text" },
    { key: "replicas", label: "ui.config.replicas", kind: "number" },
    { key: "readReplicas", label: "ui.config.readReplicas", kind: "number", term: "readReplicas" },
  ],
  Service: [
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1, term: "instances" },
    { key: "authentication", label: "ui.config.authentication", kind: "tristate" },
  ],
  Queue: [
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1, term: "instances" },
    { key: "maxRetries", label: "ui.config.maxRetries", kind: "number" },
    { key: "deadLetterQueue", label: "ui.config.deadLetterQueue", kind: "boolean", term: "deadLetterQueue" },
  ],
  Cache: [
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1, term: "instances" },
    { key: "hitRatio", label: "ui.config.hitRatio", kind: "number", min: 0, max: 1, step: 0.01, term: "hitRatio" },
  ],
  Gateway: [
    { key: "requestsPerSecond", label: "ui.config.requestsPerSecond", kind: "number", entryPointOnly: true },
    { key: "rateLimit", label: "ui.config.rateLimit", kind: "number", min: 1, term: "rateLimit" },
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1, term: "instances" },
    { key: "authentication", label: "ui.config.authentication", kind: "tristate" },
  ],
  Observability: [],
  Topic: [{ key: "instances", label: "ui.config.instances", kind: "number", min: 1, term: "instances" }],
  Worker: [{ key: "instances", label: "ui.config.instances", kind: "number", min: 1 }],
  ObjectStorage: [{ key: "replicated", label: "ui.config.replicated", kind: "boolean" }],
  ExternalService: [],
  LoadBalancer: [
    { key: "requestsPerSecond", label: "ui.config.requestsPerSecond", kind: "number", entryPointOnly: true },
    { key: "instances", label: "ui.config.instances", kind: "number", min: 1, term: "instances" },
    { key: "healthCheck", label: "ui.config.healthCheck", kind: "boolean" },
  ],
  Client: [{ key: "requestsPerSecond", label: "ui.config.requestsPerSecond", kind: "number" }],
  Cdn: [{ key: "hitRatio", label: "ui.config.hitRatio", kind: "number", min: 0, max: 1, step: 0.01, term: "hitRatio" }],
  Scheduler: [{ key: "jobsPerSecond", label: "ui.config.jobsPerSecond", kind: "number" }],
  IdentityProvider: [],
};
