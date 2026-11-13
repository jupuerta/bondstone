const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ["year", 365 * 24 * 60 * 60 * 1000],
  ["month", 30 * 24 * 60 * 60 * 1000],
  ["day", 24 * 60 * 60 * 1000],
  ["hour", 60 * 60 * 1000],
  ["minute", 60 * 1000],
];

export function relativeTime(at: number, locale: string, now: number = Date.now()): string {
  const formatter = new Intl.RelativeTimeFormat(locale, { numeric: "auto" });
  const elapsed = now - at;

  for (const [unit, size] of UNITS) {
    if (elapsed >= size) return formatter.format(-Math.floor(elapsed / size), unit);
  }

  return formatter.format(0, "second");
}
