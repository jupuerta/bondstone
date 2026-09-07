import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { nodeFaults, scenarioFromSearch, searchWithScenario, type ScenarioFault } from "../../../domain/scenarioUrl";
import { useSimulation, type SimulationReading } from "../../../hooks/useSimulation";
import { useT } from "../../../i18n";
import type { ArchitectureDto, ChallengeDetail, ElementState, Fault, FindingElement, FragilityRow, SimulationMetrics } from "../../../types/architecture";
import type { EditorMode, Selection } from "../canvas/types";
import { fragilityTint } from "./fragility";
import { latencyBudgetOf } from "./latencyWaterfall";
import { pruneFaults, rowScenario } from "./scenario";
import { usePropagationWave, type PropagationWave } from "./usePropagationWave";
import { tintStates, waveStates } from "./waveStates";

interface UseSimulateOptions {
  shareToken: string;
  savedRevision: number;
  architecture: Pick<ArchitectureDto, "nodes" | "connections">;
  challenge: ChallengeDetail | null;
  ready?: boolean;
}

export interface SimulateReading {
  mode: EditorMode;
  setMode: (mode: EditorMode) => void;
  simulating: boolean;
  faults: ScenarioFault[];
  setFaults: (faults: ScenarioFault[]) => void;
  elementFaults: Fault[];
  faultIds: ReadonlySet<string>;
  simulation: SimulationReading;
  wave: PropagationWave;
  previous: SimulationMetrics | null;
  states: ElementState[] | null;
  budgetMs: number | null;
  dropped: number;
  dismissDropped: () => void;
  nameOf: (element: FindingElement) => string;
  stateOf: (selection: Selection | null) => ElementState | null;
  previewRow: (row: FragilityRow | null) => void;
  loadRow: (row: FragilityRow) => void;
}

export function useSimulate({ shareToken, savedRevision, architecture, challenge, ready = true }: UseSimulateOptions): SimulateReading {
  const t = useT();
  const location = useLocation();
  const navigate = useNavigate();
  const [faults, setFaults] = useState<ScenarioFault[]>(() => scenarioFromSearch(location.search));
  const [mode, setMode] = useState<EditorMode>(() => (scenarioFromSearch(location.search).length > 0 ? "simulate" : "design"));
  const simulating = mode === "simulate";

  useEffect(() => {
    const next = searchWithScenario(location.search, simulating ? faults : []);
    if (next !== location.search) navigate({ pathname: location.pathname, search: next }, { replace: true });
  }, [faults, simulating, location.pathname, location.search, navigate]);

  const simulation = useSimulation({ shareToken, faults, enabled: simulating && ready, savedRevision });
  const wave = usePropagationWave(simulation.result, simulation.resultKey);

  const [previous, setPrevious] = useState<SimulationMetrics | null>(null);
  const lastKey = useRef("");
  const lastMetrics = useRef<SimulationMetrics | null>(null);
  useEffect(() => {
    if (!simulation.result) return;
    if (simulation.resultKey !== lastKey.current) {
      setPrevious(lastMetrics.current);
      lastKey.current = simulation.resultKey;
    }
    lastMetrics.current = simulation.result.metrics;
  }, [simulation.result, simulation.resultKey]);

  const elements = useMemo<FindingElement[]>(
    () => [...architecture.nodes.map((node) => ({ type: "Node" as const, id: node.id })), ...architecture.connections.map((call) => ({ type: "Connection" as const, id: call.id }))],
    [architecture],
  );
  const alive = useMemo(() => new Set(elements.map((element) => element.id)), [elements]);

  const [dropped, setDropped] = useState(0);
  const pruned = useRef<{ faults: ScenarioFault[]; alive: ReadonlySet<string> } | null>(null);
  useEffect(() => {
    if (!ready || (pruned.current?.faults === faults && pruned.current.alive === alive)) return;
    pruned.current = { faults, alive };
    const { kept, dropped: gone } = pruneFaults(faults, alive);
    if (gone === 0) return;
    setDropped((count) => count + gone);
    setFaults(kept);
  }, [faults, alive, ready]);
  const dismissDropped = useCallback(() => setDropped(0), []);

  const nameOf = useCallback(
    (element: FindingElement) => {
      if (element.type === "Node") return architecture.nodes.find((node) => node.id === element.id)?.name ?? "?";
      const call = architecture.connections.find((candidate) => candidate.id === element.id);
      if (!call) return "?";
      const source = architecture.nodes.find((node) => node.id === call.sourceNodeId)?.name ?? "?";
      const target = architecture.nodes.find((node) => node.id === call.targetNodeId)?.name ?? "?";
      return t("editor.inspector.callBetween", { source, target });
    },
    [architecture, t],
  );

  const [tinted, setTinted] = useState<ReadonlyMap<string, ElementState> | null>(null);
  useEffect(() => {
    if (!simulating) setTinted(null);
  }, [simulating]);
  const previewRow = useCallback((row: FragilityRow | null) => setTinted(row === null ? null : fragilityTint(row, nameOf({ type: "Node", id: row.nodeId }))), [nameOf]);
  const loadRow = useCallback((row: FragilityRow) => {
    setTinted(null);
    setFaults(rowScenario(row));
  }, []);

  const states = useMemo(() => (tinted ? tintStates(tinted, elements) : waveStates(simulation.result, wave.visibleStep)), [tinted, elements, simulation.result, wave.visibleStep]);
  const elementFaults = useMemo(() => nodeFaults(faults), [faults]);
  const faultIds = useMemo(() => new Set(elementFaults.map((fault) => fault.id)), [elementFaults]);
  const budgetMs = faults.length === 0 ? latencyBudgetOf(challenge) : null;
  const stateOf = useCallback(
    (selection: Selection | null) => {
      if (!selection || !simulation.result) return null;
      const type = selection.kind === "node" ? "Node" : "Connection";
      return simulation.result.states.find((state) => state.type === type && state.id === selection.id) ?? null;
    },
    [simulation.result],
  );

  return { mode, setMode, simulating, faults, setFaults, elementFaults, faultIds, simulation, wave, previous, states, budgetMs, dropped, dismissDropped, nameOf, stateOf, previewRow, loadRow };
}
