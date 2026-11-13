import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { SimulationResult } from "../../../types/architecture";

const STEP_MS = 300;

export interface PropagationWave {
  visibleStep: number;
  maxStep: number;
  playing: boolean;
  skip: () => void;
  replay: () => void;
  freeze: (step: number) => void;
  animatable: boolean;
}

function prefersReducedMotion(): boolean {
  return typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true;
}

export function usePropagationWave(result: SimulationResult | null, resultKey: string): PropagationWave {
  const maxStep = useMemo(
    () => result?.states.reduce((highest, state) => Math.max(highest, state.propagationStep), 0) ?? 0,
    [result],
  );

  const animatable = maxStep > 0 && !prefersReducedMotion();

  const [visibleStep, setVisibleStep] = useState(maxStep);
  const [playing, setPlaying] = useState(false);
  const timerRef = useRef<number | undefined>(undefined);
  const stepRef = useRef(maxStep);

  const playedKeyRef = useRef<string | null>(null);

  const stop = useCallback(() => {
    window.clearInterval(timerRef.current);
    timerRef.current = undefined;
  }, []);

  const freeze = useCallback(
    (step: number) => {
      stop();
      const held = Math.max(0, Math.min(step, maxStep));
      stepRef.current = held;
      setVisibleStep(held);
      setPlaying(false);
    },
    [maxStep, stop],
  );

  const start = useCallback(() => {
    stop();
    stepRef.current = 0;
    setVisibleStep(0);

    if (maxStep === 0) return;

    setPlaying(true);
    timerRef.current = window.setInterval(() => {
      const next = stepRef.current + 1;
      stepRef.current = next;
      setVisibleStep(next);

      if (next >= maxStep) {
        stop();
        setPlaying(false);
      }
    }, STEP_MS);
  }, [maxStep, stop]);

  useEffect(() => {
    if (!result) return;

    if (!animatable || playedKeyRef.current === resultKey) {
      playedKeyRef.current = resultKey;
      freeze(maxStep);
      return;
    }

    playedKeyRef.current = resultKey;
    start();
    return stop;
  }, [result, resultKey, animatable, maxStep, start, stop, freeze]);

  const skip = useCallback(() => freeze(maxStep), [freeze, maxStep]);

  return { visibleStep, maxStep, playing, skip, replay: start, freeze, animatable };
}
