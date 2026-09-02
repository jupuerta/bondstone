export type NodeType =
  | "Api"
  | "Database"
  | "Service"
  | "Queue"
  | "Cache"
  | "Gateway"
  | "LoadBalancer"
  | "Observability"
  | "Worker"
  | "ObjectStorage"
  | "ExternalService"
  | "Client"
  | "Cdn"
  | "Scheduler"
  | "IdentityProvider"
  | "Topic";

export type ProtocolType = "Http" | "Sql" | "Amqp";

export type Severity = "Good" | "Info" | "Warning" | "Critical";

export interface PositionDto {
  x: number;
  y: number;
}

export type NodeConfiguration = Record<string, unknown>;

export interface NodeDto {
  id: string;
  type: NodeType;
  name: string;
  position: PositionDto;
  configuration?: NodeConfiguration | null;
}

export interface ResilienceConfiguration {
  timeoutSeconds?: number | null;
  retries?: number | null;
  circuitBreaker?: boolean;
  fallback?: boolean;
  access?: "read" | "write" | "both" | null;
}

export interface ConnectionDto {
  id: string;
  sourceNodeId: string;
  targetNodeId: string;
  protocol: ProtocolType;
  synchronous: boolean;
  configuration?: ResilienceConfiguration | null;
}

export interface ArchitectureDto {
  name: string;
  shareToken: string;
  version: number;
  createdAt: string;
  updatedAt: string;
  nodes: NodeDto[];
  connections: ConnectionDto[];
}

export interface ArchitectureCreatedDto extends ArchitectureDto {
  editToken: string;
}

export interface UpdateArchitectureRequest {
  name: string;
  version: number;
  nodes: NodeDto[];
  connections: ConnectionDto[];
}

export interface UpdateArchitectureResult {
  version: number;
  updatedAt: string;
}

export type Area =
  | "Availability"
  | "Resilience"
  | "Coupling"
  | "Scalability"
  | "Security"
  | "Observability"
  | "Cost";

export const AREA_ORDER: Area[] = [
  "Availability",
  "Resilience",
  "Coupling",
  "Scalability",
  "Security",
  "Observability",
  "Cost",
];

export type AreaState = "Scored" | "NoRules" | "NotApplicable";

export type ElementType = "Node" | "Connection";

export interface LocalizedText {
  key: string;
  args?: Record<string, string | number | LocalizedText> | null;
}

export interface FindingElement {
  type: ElementType;
  id: string;
}

export interface PenaltyLine {
  code: string;
  severity: Severity;
  findings: number;
  penalty: number;
  capped: boolean;
}

export interface AreaScore {
  area: Area;
  score: number | null;
  state: AreaState;
  penalties?: PenaltyLine[];
}

export interface AnalysisSummary {
  critical: number;
  warning: number;
  info: number;
  passed: number;
}

export type FixKind =
  | "setEdgeResilience"
  | "setNodeFlag"
  | "insertCache"
  | "removeNode"
  | "connectTo"
  | "makeEdgeAsynchronous"
  | "insertQueue";

export interface AppliedFix {
  kind: FixKind;
  element: FindingElement;
  label: LocalizedText;
  timeoutSeconds?: number | null;
  circuitBreaker?: boolean | null;
  fallback?: boolean | null;
  configurationKey?: string | null;
  configurationValue?: boolean | null;
  targetNodeId?: string | null;
}

export interface RuleFinding {
  code: string;
  area: Area;
  severity: Severity;
  message: LocalizedText;
  explanation: LocalizedText;
  suggestion?: LocalizedText | null;
  elements: FindingElement[];
  fix?: AppliedFix | null;
}

export interface ArchitectureSnapshot {
  version: number;
  scoringVersion: string;
  score: number | null;
  summary: AnalysisSummary | null;
  createdAt: string;
  areas?: Partial<Record<Area, number>> | null;
}

export interface ComponentLoad {
  nodeId: string;
  rps: number;
  capacityRps: number;
  utilization: number;
}

export interface ConnectionLoad {
  connectionId: string;
  rps: number;
}

export interface ComponentCost {
  nodeId: string;
  name: string;
  perInstance: number;
  instances: number;
  total: number;
}

export interface ArchitectureCost {
  total: number;
  components: ComponentCost[];
}

export type RuleState = "Fired" | "Passed" | "NotApplicable";

export interface RuleCoverage {
  code: string;
  area: Area;
  state: RuleState;
  reason?: LocalizedText | null;
  /** V22 §11. The rule talks about the drawing, not the architecture. See `Analysis/drawingErrors`. */
  describesTheDrawing?: boolean;
}

export interface AnalysisResult {
  scoringVersion: string;
  score: number | null;
  areas: AreaScore[];
  summary: AnalysisSummary;
  findings: RuleFinding[];
  load?: ComponentLoad[];
  coverage?: RuleCoverage[];
  callLoad?: ConnectionLoad[];
  cost?: ArchitectureCost | null;
}

export type HealthState = "Healthy" | "Degraded" | "Down";

export type FaultKind = "down" | "degraded" | "latency";

export interface Fault {
  target: ElementType;
  id: string;
  kind: FaultKind;
  value?: number | null;
}

export type Mechanism = "fallback" | "circuitBreaker" | "queue" | "healthCheck";

export interface ElementState {
  type: ElementType;
  id: string;
  state: HealthState;
  reason: LocalizedText;
  mechanism: Mechanism | null;
  causedBy?: FindingElement | null;
  propagationStep: number;
  utilization?: number | null;
}

export interface SimulationMetrics {
  componentsDown: number;
  componentsDegraded: number;
  componentsTotal: number;
  blastRadius: number;
  entryPointsAffected: number;
  entryPointsTotal: number;
  nominalLatencyMs: number;
  faultedLatencyMs: number | null;
  requestAmplification: number;

  requestsAffected: number;
  bottleneck?: FindingElement | null;
  backlogRps: number;
  loadAmplification: number;
}

export type HopOutcome = "Completed" | "Expired" | "Unreachable";

export interface LatencyHop {
  nodeId: string;
  connectionId?: string | null;
  outcome: HopOutcome;
  processingMs: number;
  queueingMs: number;
  queueingFactor: number;
  networkMs: number;
  injectedMs: number;
  totalMs: number;
  cumulativeMs: number;
  callMs: number;
  rawCallMs?: number | null;
  timeoutMs?: number | null;
  retries: number;
}

export interface SimulationResult {
  simulationVersion: string;
  states: ElementState[];
  metrics: SimulationMetrics;
  discardedFaults: Fault[];
  criticalPath?: LatencyHop[];
}

export interface FragilityRow {
  nodeId: string;
  componentsDown: number;
  componentsDegraded: number;
  blastRadius: number;
  entryPointsAffected: number;
  requestsAffected: number;
  faultedLatencyMs: number | null;
  down: FindingElement[];
  degraded: FindingElement[];
}

export interface FragilityMap {
  componentsTotal: number;
  entryPointsTotal: number;
  nominalLatencyMs: number;
  rows: FragilityRow[];
}

export interface LoadCurvePoint {
  scale: number;
  enteringRps: number;
  nominalLatencyMs: number;
  backlogRps: number;
  loadAmplification: number;
  components: ComponentLoad[];
}

export interface LoadBreakPoint {
  scale: number;
  nodeId: string;
  enteringRps: number;
  rps: number;
  utilization: number;
}

export interface LoadCurve {
  points: LoadCurvePoint[];
  breakPoint: LoadBreakPoint | null;
}

export interface ChallengeSolutionPreview {
  name: string;
  nodes: NodeDto[];
  connections: ConnectionDto[];
  analysis: AnalysisResult;
}

export interface ChallengeSummary {
  slug: string;
  title: LocalizedText;
  story: LocalizedText;
  difficulty: string;
  topic: Area;
  requirementCount: number;
}

export interface ChallengeRequirementInfo {
  kind: string;
  label: LocalizedText;
  latencyBudgetMs?: number | null;
}

export interface ChallengeDetail {
  slug: string;
  title: LocalizedText;
  story: LocalizedText;
  difficulty: string;
  topic: Area;
  requirements: ChallengeRequirementInfo[];
  optionalGoals: ChallengeRequirementInfo[];
  hints: LocalizedText[];
}

export interface ChallengeRequirementResult {
  kind: string;
  label: LocalizedText;
  met: boolean;
  detail?: LocalizedText | null;
  elements: FindingElement[];
}

export interface ChallengeResult {
  slug: string;
  passed: boolean;
  requirements: ChallengeRequirementResult[];
  optionalGoals: ChallengeRequirementResult[];
}
