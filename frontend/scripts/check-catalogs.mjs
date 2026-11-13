#!/usr/bin/env node
// Paridad de los catálogos: las mismas claves en los dos idiomas, los mismos argumentos, ningún
// formato desconocido, y cada frase del analizador que las grabaciones traen existe y se rellena.
import { existsSync, readdirSync, readFileSync } from "node:fs";
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

const exercised = new Set();

for (const [file, text] of recordedTexts()) {
  exercised.add(text.key);

  for (const locale of LOCALES) {
    const entry = loaded[locale][text.key];

    if (entry === undefined) {
      problems.push(`'${file}' carries '${text.key}' and '${locale}.json' does not have it`);
      continue;
    }

    const missing = argumentsOf(entry).filter((name) => !(name in (text.args ?? {})));
    if (missing.length > 0) {
      problems.push(`'${text.key}' is rendered with {${missing.join(", ")}} unfilled — '${locale}.json' asks for it and the recording in '${file}' does not carry it`);
    }
  }
}

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

    if (typeof entry === "string") continue;
    if (typeof entry.other !== "string") problems.push(`'${key}' in ${locale} has no plural form`);
    if (typeof entry.one !== "string") problems.push(`'${key}' in ${locale} has no singular form`);
    const driver = entry.plural ?? "count";
    if (typeof entry.other === "string" && !argumentsOf(entry.other).includes(driver)) {
      problems.push(`'${key}' in ${locale} branches on '${driver}', which its plural form never uses`);
    }
  }
}

if (problems.length > 0) {
  console.error(`\n${problems.length} problem${problems.length === 1 ? "" : "s"} with the catalogues:\n`);
  for (const p of problems) console.error(`  · ${p}`);
  process.exit(1);
}

const web = Object.keys(reference).filter((k) => k.startsWith("web.")).length;
const editor = Object.keys(reference).filter((k) => k.startsWith("editor.")).length;
console.log(`✓ ${Object.keys(reference).length} keys × ${LOCALES.length} locales in step (${web} web.* keys, ${editor} editor.* keys)`);

const sent = Object.keys(reference).filter((key) => key.startsWith("finding.") || key.startsWith("rule.na."));
const uncovered = sent.filter((key) => !exercised.has(key));
console.log(`  ${sent.length - uncovered.length}/${sent.length} analyzer sentences exercised by the recordings` + (uncovered.length === 0 ? "" : `, ${uncovered.length} not:`));
for (const key of uncovered) console.log(`    · ${key}`);

function* recordedTexts() {
  const fixtures = join(here, "..", "src", "export", "__fixtures__");
  if (!existsSync(fixtures)) return;

  for (const file of readdirSync(fixtures).filter((f) => f.endsWith(".json"))) {
    const seen = new Map();
    walk(JSON.parse(readFileSync(join(fixtures, file), "utf8")), seen);
    for (const text of seen.values()) yield [file, text];
  }
}

function walk(value, seen) {
  if (Array.isArray(value)) {
    for (const item of value) walk(item, seen);
    return;
  }
  if (value === null || typeof value !== "object") return;
  if (typeof value.key === "string") {
    const names = Object.keys(value.args ?? {}).sort();
    const shape = `${value.key}|${names.join(",")}`;
    if (!seen.has(shape)) seen.set(shape, value);
  }
  for (const nested of Object.values(value)) walk(nested, seen);
}
