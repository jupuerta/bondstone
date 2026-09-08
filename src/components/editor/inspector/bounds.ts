export const INT_MAX = 2147483647;

export interface NumberBounds {
  min?: number;
  max?: number;
  step?: number;
}

export const TIMEOUT_SECONDS_BOUNDS: NumberBounds = { min: 1, max: 600 };

export const RETRIES_BOUNDS: NumberBounds = { min: 0 };

export function boundedNumber(raw: string, bounds: NumberBounds): number | null {
  const value = Number(raw);
  if (raw.trim() === "" || !Number.isFinite(value)) return null;

  const bounded = Math.min(Math.max(value, bounds.min ?? 0), bounds.max ?? INT_MAX);
  return bounds.step ? bounded : Math.round(bounded);
}
