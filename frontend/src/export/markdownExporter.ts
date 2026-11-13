import type {
  AnalysisResult,
  ArchitectureDto,
  AreaScore,
  ConnectionDto,
  ElementState,
  Fault,
  FindingElement,
  HealthState,
  NodeConfiguration,
  NodeDto,
  RuleFinding,
  Severity,
  SimulationMetrics,
  SimulationResult,
} from "../types/architecture";
import type { Translator } from "../i18n";
import { diffArchitectures, hasChanges, nameResolver, type ConnectionChange, type NodeChange } from "./baseline";
import { emitMermaid } from "./mermaidEmitter";
import { tableCell } from "./markdownEscape";
import { orderConnections, orderNodes, ordinal } from "./ordering";

export function composeMarkdown({
  architecture,
  baseline,
  analysis,
  exportedAt,
  t,
  simulation,
  faults,
}: {
  architecture: ArchitectureDto;
  baseline?: ArchitectureDto | null;
  analysis: AnalysisResult;
  exportedAt: Date;
  t: Translator;
  simulation?: SimulationResult | null;
  faults?: Fault[] | null;
}): string {
  const nodes = orderNodes(architecture.nodes);
  const namesById = new Map(nodes.map((n) => [n.id, n.name]));
  const nameOf = (id: string) => namesById.get(id) ?? t("export.unknown");
  const connections = orderConnections(architecture.connections, nameOf);
  const connectionsById = new Map(connections.map((c) => [c.id, c]));

  const health = simulation
    ? new Map(simulation.states.filter((s) => s.type === "Node").map((s) => [s.id, s.state]))
    : undefined;

  const out: string[] = [];

  header(out, architecture, analysis, exportedAt, t);
  diagram(out, nodes, connections, health, t);
  components(out, nodes, t);
  connectionsTable(out, connections, nameOf, t);
  analysisSection(out, analysis, nameOf, connectionsById, t);
  simulationSection(out, simulation, faults, nameOf, connectionsById, t);
  const startingPoint = baselineSection(out, baseline, architecture, t);
  prompt(out, t, startingPoint);

  return `${out.join("\n").replace(/\s+$/, "")}\n`;
}

const DASH = "—";

function header(
  out: string[],
  architecture: ArchitectureDto,
  analysis: AnalysisResult,
  exportedAt: Date,
  t: Translator,
): void {
  out.push(`# ${architecture.name}`, "");
  out.push(
    `| ${t("export.header.architecture")} | \`${tableCell(architecture.shareToken)}\` · ` +
      `${t("export.header.version", { version: architecture.version })} |`,
    "|---|---|",
  );
  out.push(`| ${t("export.header.exported")} | ${exportedAt.toISOString().slice(0, 10)} |`);
  out.push(`| ${t("export.header.engine")} | \`scoringVersion: ${tableCell(analysis.scoringVersion)}\` |`);
  out.push(`| ${t("export.header.format")} | \`exportVersion: ${EXPORT_VERSION}\` |`, "");
}

export const EXPORT_VERSION = "3";

function diagram(
  out: string[],
  nodes: NodeDto[],
  connections: ConnectionDto[],
  health: ReadonlyMap<string, HealthState> | undefined,
  t: Translator,
): void {
  out.push(`## ${t("export.section.diagram")}`, "");

  if (nodes.length === 0) {
    out.push(t("export.diagram.empty"), "");
    return;
  }

  out.push(emitMermaid(nodes, connections, health), "");
}

function components(out: string[], nodes: NodeDto[], t: Translator): void {
  out.push(`## ${t("export.section.components")}`, "");

  if (nodes.length === 0) {
    out.push(t("export.components.none"), "");
    return;
  }

  out.push(
    `| ${t("export.column.name")} | ${t("export.column.type")} | ${t("export.column.configuration")} |`,
    "|---|---|---|",
  );

  for (const node of nodes) {
    out.push(`| ${tableCell(node.name)} | ${node.type} | ${configuration(node.configuration, t)} |`);
  }

  out.push("");
}

function configuration(config: NodeConfiguration | null | undefined, t: Translator): string {
  if (!config) return DASH;

  const entries = Object.keys(config)
    .filter((key) => config[key] !== null && config[key] !== undefined)
    .sort(ordinal)
    .map((key) => `\`${tableCell(key)}: ${tableCell(scalar(config[key], t))}\``);

  return entries.length === 0 ? DASH : entries.join(", ");
}

function scalar(value: unknown, t: Translator): string {
  if (value === true) return t("export.yes");
  if (value === false) return t("export.no");
  if (typeof value === "string") return value;
  return JSON.stringify(value) ?? "";
}

function connectionsTable(
  out: string[],
  connections: ConnectionDto[],
  nameOf: (id: string) => string,
  t: Translator,
): void {
  if (connections.length === 0) return;

  out.push(`## ${t("export.section.connections")}`, "");
  out.push(
    `| ${t("export.column.source")} | ${t("export.column.target")} | ${t("export.column.protocol")} ` +
      `| ${t("export.column.synchronous")} | ${t("export.column.timeout")} | ${t("export.column.retries")} ` +
      `| ${t("export.column.circuitBreaker")} | ${t("export.column.fallback")} |`,
    "|---|---|---|---|---|---|---|---|",
  );

  const yes = t("export.yes");
  const no = t("export.no");

  for (const connection of connections) {
    const r = connection.configuration ?? {};

    out.push(
      `| ${tableCell(nameOf(connection.sourceNodeId))} | ${tableCell(nameOf(connection.targetNodeId))} ` +
        `| ${connection.protocol} | ${connection.synchronous ? yes : no} ` +
        `| ${r.timeoutSeconds != null ? t("export.seconds", { seconds: r.timeoutSeconds }) : DASH} ` +
        `| ${r.retries != null ? String(r.retries) : DASH} ` +
        `| ${r.circuitBreaker ? yes : no} | ${r.fallback ? yes : no} |`,
    );
  }

  out.push("");
}

function analysisSection(
  out: string[],
  analysis: AnalysisResult,
  nameOf: (id: string) => string,
  connectionsById: ReadonlyMap<string, ConnectionDto>,
  t: Translator,
): void {
  out.push(`## ${t("export.section.analysis")}`, "");

  if (analysis.score !== null) {
    out.push(
      t("export.analysis.score", {
        score: analysis.score,
        criticals: { key: "export.analysis.criticals", args: { count: analysis.summary.critical } },
        warnings: { key: "export.analysis.warnings", args: { count: analysis.summary.warning } },
        passed: { key: "export.analysis.passed", args: { count: analysis.summary.passed } },
      }),
    );
  } else {
    out.push(t("export.analysis.noScore"));
  }

  out.push("", `| ${t("export.column.area")} | ${t("export.column.score")} |`, "|---|---|");

  for (const area of analysis.areas) {
    out.push(`| ${area.area} | ${areaScore(area, t)} |`);
  }

  out.push("");

  if (analysis.findings.length === 0) {
    out.push(t("export.analysis.nothing"), "");
    return;
  }

  for (const finding of analysis.findings) {
    out.push(`### ${mark(finding.severity)} ${finding.code} — ${finding.area}`, "");
    out.push(`**${t(finding.message)}**`);

    const explanation = t(finding.explanation);
    if (explanation.trim().length > 0) out.push("", explanation);

    if (finding.suggestion) out.push("", `${t("export.analysis.suggestion")} ${t(finding.suggestion)}`);

    const elements = elementList(finding, nameOf, connectionsById);
    if (elements.length > 0) out.push("", `${t("export.analysis.elements")} ${elements}`);

    out.push("");
  }
}

function areaScore(area: AreaScore, t: Translator): string {
  if (area.state === "NotApplicable") return t("export.area.notApplicable");
  if (area.state === "NoRules") return t("export.area.noRules");
  return area.score !== null ? String(area.score) : DASH;
}

function elementList(
  finding: RuleFinding,
  nameOf: (id: string) => string,
  connectionsById: ReadonlyMap<string, ConnectionDto>,
): string {
  const parts: string[] = [];

  for (const element of finding.elements) {
    if (element.type === "Node") {
      const name = nameOf(element.id);
      if (name !== undefined) parts.push(`\`${name}\``);
      continue;
    }

    const connection = connectionsById.get(element.id);
    if (connection) parts.push(`\`${nameOf(connection.sourceNodeId)} → ${nameOf(connection.targetNodeId)}\``);
  }

  return parts.join(", ");
}

function mark(severity: Severity | HealthState): string {
  if (severity === "Critical" || severity === "Down") return "🔴";
  if (severity === "Warning" || severity === "Degraded") return "🟡";
  if (severity === "Info") return "🔵";
  return "🟢";
}

function simulationSection(
  out: string[],
  simulation: SimulationResult | null | undefined,
  faults: Fault[] | null | undefined,
  nameOf: (id: string) => string,
  connectionsById: ReadonlyMap<string, ConnectionDto>,
  t: Translator,
): void {
  if (!simulation) return;

  const scenario = canonicalFaults(faults ?? []);

  out.push(`## ${t("export.section.simulation")}`, "");

  if (scenario.length === 0) {
    out.push(t("export.simulation.noFaults"));
  } else {
    out.push(t("export.simulation.scenario"), "");
    for (const fault of scenario) out.push(`- ${faultText(fault, nameOf, connectionsById, t)}`);
  }

  out.push("");

  if (simulation.discardedFaults.length > 0) {
    out.push(`> ${t("export.simulation.discarded", { count: simulation.discardedFaults.length })}`, "");
  }

  metrics(out, simulation.metrics, nameOf, connectionsById, t);

  const affected = simulation.states.filter((s) => s.state !== "Healthy");

  if (affected.length === 0) {
    out.push(t("export.simulation.unaffected"), "");
    return;
  }

  out.push(`### ${t("export.simulation.perComponent")}`, "");

  for (const state of affected) {
    out.push(`**${mark(state.state)} \`${elementName(state, nameOf, connectionsById, t)}\`** — ${t(state.reason)}`);

    if (state.mechanism) out.push(`  · ${t("export.simulation.containedBy")} \`${state.mechanism}\``);

    out.push("");
  }

  const healthy = simulation.states.length - affected.length;
  if (healthy > 0) out.push(t("export.simulation.rest", { count: healthy }), "");
}

function canonicalFaults(faults: readonly Fault[]): Fault[] {
  const targetOrder = (target: Fault["target"]) => (target === "Node" ? 0 : 1);

  return [...faults].sort(
    (a, b) => targetOrder(a.target) - targetOrder(b.target) || ordinal(a.id, b.id) || ordinal(a.kind, b.kind),
  );
}

function metrics(
  out: string[],
  m: SimulationMetrics,
  nameOf: (id: string) => string,
  connectionsById: ReadonlyMap<string, ConnectionDto>,
  t: Translator,
): void {
  const row = (metric: string, value: string) => out.push(`| ${t(`export.metric.${metric}`)} | ${value} |`);

  out.push(`| ${t("export.column.metric")} | ${t("export.column.value")} |`, "|---|---|");

  row("componentsDown", t("export.metric.outOf", { value: m.componentsDown, total: m.componentsTotal }));
  row("componentsDegraded", t("export.metric.outOf", { value: m.componentsDegraded, total: m.componentsTotal }));
  row("blastRadius", t("export.metric.percent", { value: m.blastRadius }));
  row("entryPointsAffected", t("export.metric.outOf", { value: m.entryPointsAffected, total: m.entryPointsTotal }));
  row("nominalLatency", t("export.metric.milliseconds", { value: m.nominalLatencyMs }));

  row(
    "faultedLatency",
    m.faultedLatencyMs !== null
      ? t("export.metric.milliseconds", { value: m.faultedLatencyMs })
      : t("export.metric.noRoute"),
  );

  row("requestsAffected", t("export.metric.atMost", { value: m.requestsAffected }));

  if (m.bottleneck) {
    row("bottleneck", `\`${elementName({ type: m.bottleneck.type, id: m.bottleneck.id }, nameOf, connectionsById, t)}\``);
  }

  row("requestAmplification", t("export.metric.times", { value: m.requestAmplification }));
  row("loadAmplification", t("export.metric.times", { value: m.loadAmplification }));
  row("backlog", t("export.metric.rps", { value: m.backlogRps }));
  out.push("");
}

function faultText(
  fault: Fault,
  nameOf: (id: string) => string,
  connectionsById: ReadonlyMap<string, ConnectionDto>,
  t: Translator,
): string {
  const name = elementName({ type: fault.target, id: fault.id }, nameOf, connectionsById, t);

  if (fault.kind === "down") return t("export.fault.down", { name });
  if (fault.kind === "degraded") return t("export.fault.degraded", { name });
  if (fault.kind === "latency") return t("export.fault.latency", { name, ms: fault.value ?? 0 });
  return t("export.fault.other", { name, kind: fault.kind });
}

function elementName(
  element: FindingElement | ElementState,
  nameOf: (id: string) => string,
  connectionsById: ReadonlyMap<string, ConnectionDto>,
  t: Translator,
): string {
  if (element.type === "Node") return nameOf(element.id);

  const connection = connectionsById.get(element.id);
  return connection
    ? `${nameOf(connection.sourceNodeId)} → ${nameOf(connection.targetNodeId)}`
    : t("export.unknown");
}

function baselineSection(
  out: string[],
  baseline: ArchitectureDto | null | undefined,
  architecture: ArchitectureDto,
  t: Translator,
): boolean {
  if (!baseline) return false;

  const diff = diffArchitectures(baseline, architecture);
  if (!hasChanges(diff)) return false;

  out.push(`## ${t("export.section.baseline")}`, "");
  out.push(t("export.baseline.lead", { version: baseline.version }), "");

  const nameOf = nameResolver(baseline, architecture);
  const nodes = orderNodes(baseline.nodes);

  if (nodes.length === 0) out.push(t("export.diagram.empty"), "");
  else out.push(emitMermaid(nodes, orderConnections(baseline.connections, nameOf)), "");

  out.push(`### ${t("export.baseline.changes")}`, "");

  const connectionLabel = (connection: ConnectionDto) =>
    `${nameOf(connection.sourceNodeId)} → ${nameOf(connection.targetNodeId)}`;

  changeGroup(out, t("export.section.components"), [
    ...diff.addedNodes.map((node) => bullet(t("export.baseline.nodeAdded"), nodeLabel(node))),
    ...diff.removedNodes.map((node) => bullet(t("export.baseline.nodeRemoved"), nodeLabel(node))),
    ...diff.changedNodes.map((change) =>
      bullet(t("export.baseline.nodeChanged"), nodeLabel(change.after), nodeDetail(change, t)),
    ),
  ]);

  changeGroup(out, t("export.section.connections"), [
    ...diff.addedConnections.map((c) => bullet(t("export.baseline.connectionAdded"), connectionLabel(c))),
    ...diff.removedConnections.map((c) => bullet(t("export.baseline.connectionRemoved"), connectionLabel(c))),
    ...diff.changedConnections.map((change) =>
      bullet(t("export.baseline.connectionChanged"), connectionLabel(change.after), connectionDetail(change, t)),
    ),
  ]);

  return true;
}

function changeGroup(out: string[], heading: string, lines: string[]): void {
  if (lines.length === 0) return;
  out.push(`**${heading}**`, "", ...lines, "");
}

function nodeLabel(node: NodeDto): string {
  return `${node.type}: ${node.name}`;
}

function bullet(verb: string, element: string, detail?: string): string {
  const line = `- **${verb}** \`${element}\``;
  return detail ? `${line} ${DASH} ${detail}` : line;
}

function nodeDetail(change: NodeChange, t: Translator): string | undefined {
  const parts: string[] = [];

  if (change.renamed) parts.push(t("export.baseline.renamed", { name: change.before.name }));
  if (change.reconfigured) parts.push(t("export.baseline.reconfigured"));

  return parts.length > 0 ? parts.join(", ") : undefined;
}

function connectionDetail(change: ConnectionChange, t: Translator): string | undefined {
  const parts: string[] = [];

  if (change.synchronous) {
    parts.push(t(change.after.synchronous ? "export.baseline.toSynchronous" : "export.baseline.toAsynchronous"));
  }

  if (change.resilience) parts.push(t("export.baseline.resilience"));

  return parts.length > 0 ? parts.join(", ") : undefined;
}

function prompt(out: string[], t: Translator, startingPoint: boolean): void {
  out.push("---", "", `## ${t("export.section.prompt")}`, "", t("export.prompt.body"));

  if (startingPoint) out.push("", t("export.prompt.baseline"));
}
