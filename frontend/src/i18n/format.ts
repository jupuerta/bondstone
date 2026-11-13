export type TextFormat = "number" | "amount" | "duration" | "durationPrecise" | "percent" | "percentPrecise";

const KNOWN: TextFormat[] = ["number", "amount", "duration", "durationPrecise", "percent", "percentPrecise"];

export function isKnownFormat(format: string | null): format is TextFormat | null {
  return format === null || KNOWN.includes(format as TextFormat);
}

export function applyFormat(value: unknown, format: string | null, locale: string): string {
  if (format === null) return String(value);

  const n = typeof value === "number" ? value : Number(value);
  if (Number.isNaN(n)) return String(value);

  switch (format) {
    case "number":
      return decimals(n, locale, 0);

    case "amount":
      return decimals(n, locale, 2);

    case "duration":
      return n >= 1000 ? `${decimals(Math.trunc(n / 1000), locale, 0)} s` : `${decimals(n, locale, 0)} ms`;

    case "durationPrecise":
      return n >= 1000 ? `${decimals(n / 1000, locale, 2)} s` : `${decimals(n, locale, 0)} ms`;

    case "percent":
      return percent(n, locale, 0);

    case "percentPrecise":
      return percent(n, locale, 1);

    default:
      return String(value);
  }
}

const GROUPING_FROM = 10_000;

function decimals(value: number, locale: string, maximumFractionDigits: number): string {
  const magnitude = Math.abs(Number(value.toFixed(maximumFractionDigits)));
  return value.toLocaleString(locale, { useGrouping: magnitude >= GROUPING_FROM, maximumFractionDigits });
}

function percent(fraction: number, locale: string, maximumFractionDigits: number): string {
  const value = decimals(fraction * 100, locale, maximumFractionDigits);
  return locale.startsWith("en") ? `${value}%` : `${value} %`;
}
