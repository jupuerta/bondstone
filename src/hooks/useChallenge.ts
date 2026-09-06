import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "../api/architectureClient";
import { evaluateChallenge, getChallenge } from "../api/challengeClient";
import { rememberPassed } from "../domain/progress";
import { useT } from "../i18n";
import type { ChallengeDetail, ChallengeResult } from "../types/architecture";

export type EvaluationStatus = "idle" | "evaluating" | "done" | "error";

interface UseChallengeOptions {
  slug: string | null;
  shareToken: string;
  own: boolean;
}

export interface ChallengeReading {
  definition: ChallengeDetail | null;
  result: ChallengeResult | null;
  status: EvaluationStatus;
  error: string | null;
  evaluate: () => Promise<void>;
  invalidate: () => void;
}

export function useChallenge({ slug, shareToken, own }: UseChallengeOptions): ChallengeReading {
  const t = useT();
  const [definition, setDefinition] = useState<ChallengeDetail | null>(null);
  const [result, setResult] = useState<ChallengeResult | null>(null);
  const [status, setStatus] = useState<EvaluationStatus>("idle");
  const [error, setError] = useState<string | null>(null);
  const inFlight = useRef<AbortController | null>(null);

  useEffect(() => {
    setDefinition(null);
    if (!slug) return;
    let cancelled = false;
    getChallenge(slug)
      .then((loaded) => {
        if (!cancelled) setDefinition(loaded);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [slug]);

  const invalidate = useCallback(() => {
    inFlight.current?.abort();
    setResult(null);
    setStatus("idle");
    setError(null);
  }, []);

  const evaluate = useCallback(async () => {
    if (!slug) return;
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    setStatus("evaluating");
    setError(null);
    try {
      const evaluated = await evaluateChallenge(slug, shareToken, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setResult(evaluated);
      setStatus("done");
      if (evaluated.passed && own) rememberPassed(slug);
    } catch (err: unknown) {
      if (controller.signal.aborted) return;
      setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.error.evaluate"));
      setStatus("error");
    }
  }, [slug, shareToken, own, t]);

  useEffect(() => () => inFlight.current?.abort(), []);

  return { definition, result, status, error, evaluate, invalidate };
}
