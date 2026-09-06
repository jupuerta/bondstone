import { AREA_ORDER, type Area, type ArchitectureSnapshot } from "../types/architecture";

export interface AreaGain {
  area: Area;
  from: number;
  to: number;
  gain: number;
}

export function areasImproved(history: ArchitectureSnapshot[]): AreaGain[] {
  const measured = history.filter((entry) => hasBreakdown(entry));
  const latest = measured.at(-1);
  if (!latest) return [];

  const comparable = measured.filter((entry) => entry.scoringVersion === latest.scoringVersion);
  const first = comparable[0];
  if (!first || first === latest) return [];

  const gains: AreaGain[] = [];

  for (const area of AREA_ORDER) {
    const from = first.areas?.[area];
    const to = latest.areas?.[area];

    if (from === undefined || to === undefined || to <= from) continue;

    gains.push({ area, from, to, gain: to - from });
  }

  return gains.sort(byBiggestGain);
}

export function bestGains(histories: ArchitectureSnapshot[][]): AreaGain[] {
  const best = new Map<Area, AreaGain>();

  for (const history of histories) {
    for (const gain of areasImproved(history)) {
      const standing = best.get(gain.area);
      if (!standing || gain.gain > standing.gain) best.set(gain.area, gain);
    }
  }

  return [...best.values()].sort(byBiggestGain);
}

function hasBreakdown(entry: ArchitectureSnapshot): boolean {
  return entry.areas !== null && entry.areas !== undefined && Object.keys(entry.areas).length > 0;
}

function byBiggestGain(a: AreaGain, b: AreaGain): number {
  return b.gain - a.gain || AREA_ORDER.indexOf(a.area) - AREA_ORDER.indexOf(b.area);
}
