import { useCallback, useEffect, useRef, useState } from "react";
import { analyzeArchitecture, ApiError } from "../api/architectureClient";
import type { AnalysisResult } from "../types/architecture";
import { useT } from "../i18n";

const ANALYSIS_DEBOUNCE_MS = 800;

export type AnalysisStatus = "idle" | "loading" | "error";

interface UseLiveAnalysisOptions {
  shareToken: string;
  savedRevision: number;
  enabled?: boolean;
}

interface UseLiveAnalysisResult {
  status: AnalysisStatus;
  result: AnalysisResult | null;
  error: string | null;
}

export function useLiveAnalysis({ shareToken, savedRevision, enabled = true }: UseLiveAnalysisOptions): UseLiveAnalysisResult {
  const t = useT();
  const [status, setStatus] = useState<AnalysisStatus>("idle");
  const [result, setResult] = useState<AnalysisResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const timerRef = useRef<number | undefined>(undefined);
  const abortRef = useRef<AbortController | null>(null);

  const run = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setStatus("loading");

    analyzeArchitecture(shareToken, { signal: controller.signal })
      .then((next) => {
        if (controller.signal.aborted) return;
        setResult(next);
        setError(null);
        setStatus("idle");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.error.analyze"));
        setStatus("error");
      });
  }, [shareToken, t]);

  useEffect(() => {
    if (!enabled) {
      abortRef.current?.abort();
      return;
    }

    window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(run, ANALYSIS_DEBOUNCE_MS);

    return () => window.clearTimeout(timerRef.current);
  }, [run, savedRevision, enabled]);

  useEffect(() => () => abortRef.current?.abort(), []);

  return { status, result, error };
}
