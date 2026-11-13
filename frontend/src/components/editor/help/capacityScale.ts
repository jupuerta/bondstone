export interface CapacityScale {
  min: number;
  max: number;
  decades: number[];
  positionOf: (value: number) => number;
}

export function capacityScale(values: number[]): CapacityScale {
  const positive = values.filter((value) => value > 0);
  const low = Math.min(...positive);
  const high = Math.max(...positive);

  const minExponent = Math.floor(Math.log10(low));
  const maxExponent = Math.max(Math.ceil(Math.log10(high)), minExponent + 1);

  const min = 10 ** minExponent;
  const max = 10 ** maxExponent;
  const span = maxExponent - minExponent;

  return {
    min,
    max,
    decades: Array.from({ length: span + 1 }, (_, index) => 10 ** (minExponent + index)),
    positionOf: (value: number) => (value <= min ? 0 : Math.min(1, (Math.log10(value) - minExponent) / span)),
  };
}

export function decadeLabel(decade: number): string {
  if (decade >= 1_000_000) return `${decade / 1_000_000}M`;
  if (decade >= 1_000) return `${decade / 1_000}k`;
  return String(decade);
}
