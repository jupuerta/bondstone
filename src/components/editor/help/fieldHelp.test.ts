import { describe, expect, it } from "vitest";
import {
  FIELD_HELP,
  FIELD_IDS,
  MECHANISM_IDS,
  destinationsOf,
  fieldEffect,
  fieldFormula,
  fieldHelpOf,
  movesTheScore,
  readerLabel,
  type FieldId,
  type FieldReader,
} from "./fieldHelp";
import { CONFIG_SCHEMAS } from "../inspector/configSchemas";
import { MECHANISM_IDS as WORKSHOP_IDS, mechanismHref } from "../../../domain/mechanisms";
import { RULES } from "../../../domain/rulesCatalog";
import { createTranslator } from "../../../i18n";

const LOCALES = ["es", "en"] as const;

const CALL_FIELDS: FieldId[] = ["access", "timeoutSeconds", "retries", "circuitBreaker", "fallback"];

const READERS: FieldReader[] = ["both", "load", "rules", "simulation", "nobody"];

describe("the eighteen fields (F4)", () => {
  it("covers every field the two inspectors offer, and nothing else", () => {
    const offered = new Set<string>(CALL_FIELDS);
    for (const fields of Object.values(CONFIG_SCHEMAS)) {
      for (const field of fields) offered.add(field.key);
    }

    expect([...offered].sort()).toEqual([...FIELD_IDS].sort());
    expect(FIELD_IDS).toHaveLength(18);
  });

  it("says what every one of them changes, in both languages", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const id of FIELD_IDS) {
        expect(t(fieldEffect(id)), `${locale} · ${id}`).not.toBe(fieldEffect(id));
      }
    }
  });

  it("prints the arithmetic where there is arithmetic, in both languages", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const id of FIELD_IDS) {
        const formula = fieldFormula(id);
        if (formula === null) continue;

        expect(t(formula), `${locale} · ${id}`).not.toBe(formula);
      }
    }
  });

  it("has a sentence for each of the five ways of being read", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const reader of READERS) {
        expect(t(readerLabel(reader)), `${locale} · ${reader}`).not.toBe(readerLabel(reader));
      }
    }
  });

  it("keys every entry by its own id", () => {
    for (const id of FIELD_IDS) expect(FIELD_HELP[id].id).toBe(id);
  });

  it("answers with nothing for what is not one of the seventeen", () => {
    for (const key of ["name", "protocol", "synchronous", "", "toString"]) {
      expect(fieldHelpOf(key), key).toBeNull();
    }
  });
});

describe("what reads each field", () => {
  it("never claims a rule the catalogue does not have", () => {
    const known = new Set(RULES.map((rule) => rule.code));

    for (const id of FIELD_IDS) {
      for (const code of FIELD_HELP[id].rules) expect(known, `${id} · ${code}`).toContain(code);
    }
  });

  it("names the three that never reach the score, and only those three", () => {
    const unread = FIELD_IDS.filter((id) => !movesTheScore(id));

    expect(unread.sort()).toEqual(["healthCheck", "maxRetries", "type"]);
  });

  it("does not list a rule for a field no rule reads by name", () => {
    for (const id of FIELD_IDS) {
      const { reader, rules } = FIELD_HELP[id];
      if (reader === "load" || reader === "simulation" || reader === "nobody") {
        expect(rules, id).toHaveLength(0);
      } else {
        expect(rules.length, id).toBeGreaterThan(0);
      }
    }
  });

  it("keeps the two that reach the rules only through the load apart from those", () => {
    expect(FIELD_HELP.healthCheck.reader).toBe("simulation");
    expect(FIELD_HELP.maxRetries.reader).toBe("simulation");
    expect(FIELD_HELP.hitRatio.reader).toBe("load");
    expect(FIELD_HELP.jobsPerSecond.reader).toBe("load");
  });

  it("keeps the engine of a database as what it is: a note to the reader", () => {
    expect(FIELD_HELP.type).toEqual({
      id: "type",
      reader: "nobody",
      rules: [],
      mechanism: null,
      formula: false,
    });

    expect(destinationsOf("type")).toEqual([]);
  });
});

describe("where each field sends you", () => {
  it("points every rule at its own card in the catalogue", () => {
    for (const id of FIELD_IDS) {
      const rules = destinationsOf(id).filter((destination) => destination.kind === "rule");

      expect(rules.map((destination) => destination.to), id).toEqual(
        FIELD_HELP[id].rules.map((code) => `/rules#${code}`),
      );
    }
  });

  it("points every mechanism at one of the eleven cards of the workshop", () => {
    for (const id of FIELD_IDS) {
      const { mechanism } = FIELD_HELP[id];
      if (mechanism === null) continue;

      expect(MECHANISM_IDS, id).toContain(mechanism);
      expect(destinationsOf(id)).toContainEqual({
        kind: "mechanism",
        to: `/mechanisms#${mechanism}`,
        code: null,
      });
    }
  });

  it("opens the model exactly where there is a formula to read there", () => {
    for (const id of FIELD_IDS) {
      const model = destinationsOf(id).filter((destination) => destination.kind === "model");

      expect(model.length === 1, id).toBe(fieldFormula(id) !== null);
    }
  });

  it("offers the bare catalogue only when there is no rule to name", () => {
    for (const id of FIELD_IDS) {
      const catalog = destinationsOf(id).filter((destination) => destination.kind === "catalog");

      expect(catalog.length === 1, id).toBe(FIELD_HELP[id].reader === "load");
      if (catalog.length === 1) expect(FIELD_HELP[id].rules).toHaveLength(0);
    }
  });

  it("agrees with the workshop on the eleven cards and on where they live", () => {
    expect([...MECHANISM_IDS].sort()).toEqual([...WORKSHOP_IDS].sort());

    for (const id of FIELD_IDS) {
      const { mechanism } = FIELD_HELP[id];
      if (mechanism === null) continue;

      const destination = destinationsOf(id).find((candidate) => candidate.kind === "mechanism");
      expect(destination?.to, id).toBe(mechanismHref(mechanism));
    }
  });

  it("never repeats a destination inside one field", () => {
    for (const id of FIELD_IDS) {
      const destinations = destinationsOf(id).map((destination) => destination.to);

      expect(new Set(destinations).size, id).toBe(destinations.length);
    }
  });
});
