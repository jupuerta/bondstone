import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, sweepArchitectureLoad } from "../api/architectureClient";
import { useT } from "../i18n";
import type { LoadCurve } from "../types/architecture";

const LOAD_CURVE_DEBOUNCE_MS = 250;

export type LoadCurveStatus = "idle" | "loading" | "error";

interface UseLoadCurveOptions {
  shareToken: string;
  enabled: boolean;
  savedRevision: number;
}

interface UseLoadCurveResult {
  status: LoadCurveStatus;
  curve: LoadCurve | null;
  error: string | null;
  retry: () => void;
}

export function useLoadCurve({ shareToken, enabled, savedRevision }: UseLoadCurveOptions): UseLoadCurveResult {
  const t = useT();
  const [status, setStatus] = useState<LoadCurveStatus>("idle");
  const [curve, setCurve] = useState<LoadCurve | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  const timerRef = useRef<number | undefined>(undefined);
  const abortRef = useRef<AbortController | null>(null);

  const run = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setStatus("loading");

    sweepArchitectureLoad(shareToken, { signal: controller.signal })
      .then((next) => {
        if (controller.signal.aborted) return;
        setCurve(next);
        setError(null);
        setStatus("idle");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.error.loadCurve"));
        setStatus("error");
      });
  }, [shareToken, t]);

  useEffect(() => {
    if (!enabled) {
      abortRef.current?.abort();
      return;
    }

    window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(run, LOAD_CURVE_DEBOUNCE_MS);

    return () => window.clearTimeout(timerRef.current);
  }, [run, enabled, savedRevision, attempt]);

  useEffect(() => () => abortRef.current?.abort(), []);

  const retry = useCallback(() => setAttempt((current) => current + 1), []);

  return { status, curve, error, retry };
}
