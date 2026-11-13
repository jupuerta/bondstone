import { applyFormat } from "./format";
import type { LocalizedText } from "../types/architecture";

export type CatalogEntry =
  | string
  | {
      one: string;
      other: string;
      plural?: string;
    };

export type Catalog = Record<string, CatalogEntry>;

const PLACEHOLDER = /\{([A-Za-z][A-Za-z0-9]*)(?::([A-Za-z][A-Za-z0-9]*))?\}/g;

export function render(
  text: LocalizedText,
  catalog: Catalog,
  locale: string,
  fallback?: Catalog,
): string {
  const entry = catalog[text.key] ?? fallback?.[text.key];
  if (entry === undefined) return text.key;

  const args = text.args ?? {};
  const template = typeof entry === "string" ? entry : selectForm(entry, args);

  return template.replace(PLACEHOLDER, (whole, name: string, format?: string) => {
    if (!(name in args)) return whole;

    const value = args[name];

    return isLocalizedText(value)
      ? render(value, catalog, locale, fallback)
      : applyFormat(value, format ?? null, locale);
  });
}

function selectForm(entry: Exclude<CatalogEntry, string>, args: Record<string, unknown>): string {
  const driver = entry.plural ?? "count";
  const count = Number(args[driver]);

  return Number.isFinite(count) && Math.abs(count - 1) < 0.0001 ? entry.one : entry.other;
}

function isLocalizedText(value: unknown): value is LocalizedText {
  return typeof value === "object" && value !== null && "key" in value && typeof (value as LocalizedText).key === "string";
}
