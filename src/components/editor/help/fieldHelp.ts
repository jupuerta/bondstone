export type FieldId =
  | "access"
  | "authentication"
  | "circuitBreaker"
  | "deadLetterQueue"
  | "fallback"
  | "healthCheck"
  | "hitRatio"
  | "instances"
  | "jobsPerSecond"
  | "maxRetries"
  | "rateLimit"
  | "readReplicas"
  | "replicas"
  | "replicated"
  | "requestsPerSecond"
  | "retries"
  | "timeoutSeconds"
  | "type";

export type FieldReader = "both" | "load" | "rules" | "simulation" | "nobody";

export type MechanismId =
  | "cache"
  | "circuitBreaker"
  | "deadLetterQueue"
  | "fallback"
  | "healthCheck"
  | "instances"
  | "queue"
  | "rateLimit"
  | "readReplicas"
  | "retries"
  | "timeout";

export const MECHANISM_IDS: MechanismId[] = [
  "timeout",
  "retries",
  "circuitBreaker",
  "fallback",
  "queue",
  "deadLetterQueue",
  "cache",
  "readReplicas",
  "rateLimit",
  "healthCheck",
  "instances",
];

export interface FieldHelp {
  id: FieldId;
  reader: FieldReader;
  rules: string[];
  mechanism: MechanismId | null;
  formula: boolean;
}

export const FIELD_HELP: Record<FieldId, FieldHelp> = {
  instances: {
    id: "instances",
    reader: "both",
    rules: ["SPOF", "UNDERSIZED_COMPONENT"],
    mechanism: "instances",
    formula: true,
  },
  requestsPerSecond: {
    id: "requestsPerSecond",
    reader: "load",
    rules: [],
    mechanism: null,
    formula: true,
  },
  jobsPerSecond: {
    id: "jobsPerSecond",
    reader: "load",
    rules: [],
    mechanism: null,
    formula: true,
  },
  rateLimit: {
    id: "rateLimit",
    reader: "both",
    rules: ["NO_RATE_LIMIT", "TRAFFIC_REJECTED"],
    mechanism: "rateLimit",
    formula: true,
  },
  authentication: {
    id: "authentication",
    reader: "rules",
    rules: ["UNAUTHENTICATED_ENTRY"],
    mechanism: null,
    formula: false,
  },
  hitRatio: {
    id: "hitRatio",
    reader: "load",
    rules: [],
    mechanism: "cache",
    formula: true,
  },
  replicas: {
    id: "replicas",
    reader: "both",
    rules: ["SPOF", "UNDERSIZED_COMPONENT"],
    mechanism: null,
    formula: true,
  },
  replicated: {
    id: "replicated",
    reader: "rules",
    rules: ["SPOF"],
    mechanism: null,
    formula: false,
  },
  readReplicas: {
    id: "readReplicas",
    reader: "both",
    rules: ["MISSING_CACHE", "UNDERSIZED_COMPONENT"],
    mechanism: "readReplicas",
    formula: true,
  },
  type: {
    id: "type",
    reader: "nobody",
    rules: [],
    mechanism: null,
    formula: false,
  },
  healthCheck: {
    id: "healthCheck",
    reader: "simulation",
    rules: [],
    mechanism: "healthCheck",
    formula: false,
  },
  maxRetries: {
    id: "maxRetries",
    reader: "simulation",
    rules: [],
    mechanism: "queue",
    formula: true,
  },
  deadLetterQueue: {
    id: "deadLetterQueue",
    reader: "rules",
    rules: ["QUEUE_NO_DLQ"],
    mechanism: "deadLetterQueue",
    formula: false,
  },
  timeoutSeconds: {
    id: "timeoutSeconds",
    reader: "both",
    rules: [
      "TIMEOUT_TOO_SHORT",
      "TIMEOUT_TOO_LONG",
      "MISSING_RESILIENCE",
      "EXTERNAL_NO_FALLBACK",
      "RETRY_AMPLIFICATION",
    ],
    mechanism: "timeout",
    formula: true,
  },
  retries: {
    id: "retries",
    reader: "both",
    rules: ["RETRY_AMPLIFICATION"],
    mechanism: "retries",
    formula: true,
  },
  circuitBreaker: {
    id: "circuitBreaker",
    reader: "both",
    rules: ["MISSING_RESILIENCE", "EXTERNAL_NO_FALLBACK", "TIMEOUT_TOO_LONG"],
    mechanism: "circuitBreaker",
    formula: false,
  },
  fallback: {
    id: "fallback",
    reader: "both",
    rules: ["MISSING_RESILIENCE", "EXTERNAL_NO_FALLBACK", "TIMEOUT_TOO_LONG", "SPOF"],
    mechanism: "fallback",
    formula: false,
  },
  access: {
    id: "access",
    reader: "rules",
    rules: ["MISSING_CACHE", "SHARED_DATABASE", "SYNC_CHAIN"],
    mechanism: null,
    formula: false,
  },
};

export const FIELD_IDS = Object.keys(FIELD_HELP) as FieldId[];

export function fieldHelpOf(key: string): FieldHelp | null {
  return Object.hasOwn(FIELD_HELP, key) ? FIELD_HELP[key as FieldId] : null;
}

export function fieldEffect(id: FieldId): string {
  return `ui.field.${id}.effect`;
}

export function fieldFormula(id: FieldId): string | null {
  return FIELD_HELP[id].formula ? `ui.field.${id}.formula` : null;
}

export function readerLabel(reader: FieldReader): string {
  return `ui.field.reads.${reader}`;
}

export type DestinationKind = "rule" | "catalog" | "mechanism" | "model";

export interface FieldDestination {
  kind: DestinationKind;
  to: string;
  code: string | null;
}

export function destinationsOf(id: FieldId): FieldDestination[] {
  const { reader, rules, mechanism, formula } = FIELD_HELP[id];
  const destinations: FieldDestination[] = [];

  for (const code of rules) destinations.push({ kind: "rule", to: `/rules#${code}`, code });

  if (rules.length === 0 && reader === "load") {
    destinations.push({ kind: "catalog", to: "/rules", code: null });
  }

  if (mechanism !== null) {
    destinations.push({ kind: "mechanism", to: `/mechanisms#${mechanism}`, code: null });
  }

  if (formula) destinations.push({ kind: "model", to: "/model", code: null });

  return destinations;
}

export function movesTheScore(id: FieldId): boolean {
  return FIELD_HELP[id].reader === "both" || FIELD_HELP[id].reader === "rules"
    || FIELD_HELP[id].reader === "load";
}
