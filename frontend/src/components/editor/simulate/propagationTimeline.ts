import type { ElementState, HealthState } from "../../../types/architecture";

const WORST_FIRST: Record<HealthState, number> = { Down: 0, Degraded: 1, Healthy: 2 };

export interface TimelineFrame {
  step: number;
  changed: ElementState[];
  contained: boolean;
  down: number;
  degraded: number;
}

export function propagationFrames(states: readonly ElementState[]): TimelineFrame[] {
  const byStep = new Map<number, ElementState[]>();

  for (const state of states) {
    if (!moved(state)) continue;

    const frame = byStep.get(state.propagationStep);
    if (frame) frame.push(state);
    else byStep.set(state.propagationStep, [state]);
  }

  return [...byStep.entries()]
    .sort(([a], [b]) => a - b)
    .map(([step, changed]) => ({
      step,
      changed: [...changed].sort((a, b) => WORST_FIRST[a.state] - WORST_FIRST[b.state]),
      contained: changed.some((state) => state.mechanism !== null),
      down: changed.filter((state) => state.state === "Down").length,
      degraded: changed.filter((state) => state.state === "Degraded").length,
    }));
}

export function frameIndexAt(frames: readonly TimelineFrame[], visibleStep: number): number {
  let index = 0;
  for (let i = 0; i < frames.length; i++) {
    if (frames[i].step <= visibleStep) index = i;
  }
  return index;
}

function moved(state: ElementState): boolean {
  return state.state !== "Healthy" || state.mechanism !== null;
}
