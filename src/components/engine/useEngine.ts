import { useCallback, useEffect, useState } from "react";
import { analyzeArchitecture, ApiError, getArchitectureByShareToken, simulateArchitecture } from "../../api/architectureClient";
import { nodeFaults, type ScenarioFault } from "../../domain/scenarioUrl";
import { useT } from "../../i18n";
import type { AnalysisResult, ArchitectureDto, Fault, SimulationResult } from "../../types/architecture";

/** O un escenario ya resuelto (viene en la URL), o el nombre del nodo que hay que tumbar (la demo). */
export type EngineScenario = ScenarioFault[] | { nodeName: string };

export interface EngineData {
  architecture: ArchitectureDto;
  analysis: AnalysisResult;
  simulation: SimulationResult | null;
  faults: ScenarioFault[];
}

export type EngineState = { status: "loading" } | { status: "error"; message: string } | { status: "ready"; data: EngineData };

export function useEngine(shareToken: string | null, scenario: EngineScenario | null): { state: EngineState; retry: () => void } {
  const t = useT();
  const [state, setState] = useState<EngineState>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);
  const retry = useCallback(() => setAttempt((n) => n + 1), []);
  const scenarioKey = JSON.stringify(scenario);

  useEffect(() => {
    if (shareToken === null) return;
    const controller = new AbortController();
    const { signal } = controller;
    setState({ status: "loading" });

    (async () => {
      const architecture = await getArchitectureByShareToken(shareToken);
      const faults = resolveFaults(architecture, JSON.parse(scenarioKey) as EngineScenario | null);
      const [analysis, simulation] = await Promise.all([
        analyzeArchitecture(shareToken, { signal }),
        faults.length > 0 ? simulateArchitecture(shareToken, faults, { signal }) : Promise.resolve(null),
      ]);
      if (!signal.aborted) setState({ status: "ready", data: { architecture, analysis, simulation, faults } });
    })().catch((error: unknown) => {
      if (signal.aborted) return;
      setState({ status: "error", message: error instanceof ApiError ? t(error.key, error.args) : t("web.engine.error.title") });
    });

    return () => controller.abort();
  }, [shareToken, scenarioKey, attempt, t]);

  return { state, retry };
}

function resolveFaults(architecture: ArchitectureDto, scenario: EngineScenario | null): ScenarioFault[] {
  if (scenario === null) return [];
  if (Array.isArray(scenario)) return scenario;
  const wanted = scenario.nodeName.trim().toLowerCase();
  const node = architecture.nodes.find((candidate) => candidate.name.trim().toLowerCase() === wanted);
  if (!node) return [];
  const fault: Fault = { target: "Node", id: node.id, kind: "down" };
  return [fault];
}

export { nodeFaults };
