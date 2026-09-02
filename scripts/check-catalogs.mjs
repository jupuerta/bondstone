#!/usr/bin/env node
// Paridad de los catálogos: las mismas claves en los dos idiomas, los mismos argumentos y
// ningún formato desconocido. Es la versión corta del script del frontend actual.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const dir = join(here, "..", "src", "i18n");
const LOCALES = ["es", "en"];
const DEFAULT_LOCALE = "es";
const FORMATS = new Set(["number", "amount", "duration", "durationPrecise", "percent", "percentPrecise"]);
const PLACEHOLDER = /\{([A-Za-z][A-Za-z0-9]*)(?::([A-Za-z][A-Za-z0-9]*))?\}/g;

const loaded = Object.fromEntries(LOCALES.map((l) => [l, JSON.parse(readFileSync(join(dir, `${l}.json`), "utf8"))]));
const reference = loaded[DEFAULT_LOCALE];
const problems = [];

const forms = (entry) => (typeof entry === "string" ? [entry] : [entry.one, entry.other].filter((f) => typeof f === "string"));
const argumentsOf = (entry) => {
  const names = new Set();
  for (const form of forms(entry)) for (const [, name] of form.matchAll(PLACEHOLDER)) names.add(name);
  return [...names].sort();
};

for (const locale of LOCALES.filter((l) => l !== DEFAULT_LOCALE)) {
  for (const key of Object.keys(reference)) if (!(key in loaded[locale])) problems.push(`'${locale}.json' is missing '${key}'`);
  for (const key of Object.keys(loaded[locale])) if (!(key in reference)) problems.push(`'${locale}.json' has '${key}', which '${DEFAULT_LOCALE}.json' does not`);
  for (const [key, entry] of Object.entries(loaded[locale])) {
    if (!(key in reference)) continue;
    if (argumentsOf(entry).join() !== argumentsOf(reference[key]).join()) problems.push(`'${key}' takes different arguments in ${DEFAULT_LOCALE} and ${locale}`);
  }
}
for (const locale of LOCALES) {
  for (const [key, entry] of Object.entries(loaded[locale])) {
    for (const form of forms(entry)) for (const [, , format] of form.matchAll(PLACEHOLDER)) if (format && !FORMATS.has(format)) problems.push(`'${key}' in ${locale} asks for the unknown format '${format}'`);
  }
}

if (problems.length > 0) {
  console.error(`\n${problems.length} problem${problems.length === 1 ? "" : "s"} with the catalogues:\n`);
  for (const p of problems) console.error(`  · ${p}`);
  process.exit(1);
}
const web = Object.keys(reference).filter((k) => k.startsWith("web.")).length;
console.log(`✓ ${Object.keys(reference).length} keys × ${LOCALES.length} locales in step (${web} web.* keys)`);
