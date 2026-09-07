import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, mapArchitectureFragility } from "../api/architectureClient";
import type { FragilityMap } from "../types/architecture";
import { useT } from "../i18n";

const FRAGILITY_DEBOUNCE_MS = 250;

export type FragilityStatus = "idle" | "loading" | "error";

interface UseFragilityOptions {
  shareToken: string;
  enabled: boolean;
  savedRevision: number;
}

interface UseFragilityResult {
  status: FragilityStatus;
  map: FragilityMap | null;
  error: string | null;
  retry: () => void;
}

export function useFragility({ shareToken, enabled, savedRevision }: UseFragilityOptions): UseFragilityResult {
  const t = useT();
  const [status, setStatus] = useState<FragilityStatus>("idle");
  const [map, setMap] = useState<FragilityMap | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  const timerRef = useRef<number | undefined>(undefined);
  const abortRef = useRef<AbortController | null>(null);

  const run = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setStatus("loading");

    mapArchitectureFragility(shareToken, { signal: controller.signal })
      .then((next) => {
        if (controller.signal.aborted) return;
        setMap(next);
        setError(null);
        setStatus("idle");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.error.fragility"));
        setStatus("error");
      });
  }, [shareToken]);

  useEffect(() => {
    if (!enabled) {
      abortRef.current?.abort();
      return;
    }

    window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(run, FRAGILITY_DEBOUNCE_MS);

    return () => window.clearTimeout(timerRef.current);
  }, [run, enabled, savedRevision, attempt]);

  useEffect(() => () => abortRef.current?.abort(), []);

  const retry = useCallback(() => setAttempt((current) => current + 1), []);

  return { status, map, error, retry };
}
