import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, simulateArchitecture } from "../api/architectureClient";
import { scenarioKey, type ScenarioFault } from "../domain/scenarioUrl";
import { useT } from "../i18n";
import type { SimulationResult } from "../types/architecture";

const SIMULATION_DEBOUNCE_MS = 250;

export type SimulationStatus = "idle" | "loading" | "error";

interface UseSimulationOptions {
  shareToken: string;
  faults: ScenarioFault[];
  enabled: boolean;
  savedRevision: number;
}

export interface SimulationReading {
  status: SimulationStatus;
  result: SimulationResult | null;
  error: string | null;
  resultKey: string;
  retry: () => void;
}

export function useSimulation({ shareToken, faults, enabled, savedRevision }: UseSimulationOptions): SimulationReading {
  const t = useT();
  const [status, setStatus] = useState<SimulationStatus>("idle");
  const [result, setResult] = useState<SimulationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [resultKey, setResultKey] = useState("");
  const [attempt, setAttempt] = useState(0);

  const timerRef = useRef<number | undefined>(undefined);
  const abortRef = useRef<AbortController | null>(null);
  const faultsRef = useRef(faults);
  faultsRef.current = faults;

  const key = scenarioKey(faults);

  const run = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setStatus("loading");

    simulateArchitecture(shareToken, faultsRef.current, { signal: controller.signal })
      .then((next) => {
        if (controller.signal.aborted) return;
        setResult(next);
        setResultKey(key);
        setError(null);
        setStatus("idle");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.error.simulate"));
        setStatus("error");
      });
  }, [shareToken, key, t]);

  useEffect(() => {
    if (!enabled) {
      abortRef.current?.abort();
      return;
    }

    window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(run, SIMULATION_DEBOUNCE_MS);

    return () => window.clearTimeout(timerRef.current);
  }, [run, enabled, savedRevision, attempt]);

  useEffect(() => () => abortRef.current?.abort(), []);

  const retry = useCallback(() => setAttempt((current) => current + 1), []);

  return { status, result, error, resultKey, retry };
}
