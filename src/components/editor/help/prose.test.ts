import { describe, expect, it } from "vitest";
import { ruleCodesIn } from "./prose";
import { TERM_IDS, termDefinition } from "./glossary";
import { RULES } from "../../../domain/rulesCatalog";
import { NODE_META } from "../palette/nodeMeta";
import type { NodeType } from "../../../types/architecture";
import { createTranslator } from "../../../i18n";

const CODES = new Set(RULES.map((rule) => rule.code));
const TYPES = Object.keys(NODE_META) as NodeType[];
const LOCALES = ["es", "en"] as const;

describe("the rule codes inside a sentence", () => {
  it("finds a code wherever it is written", () => {
    expect(ruleCodesIn("una caché no promete conservar nada (CACHE_AS_STORE).")).toEqual(["CACHE_AS_STORE"]);
    expect(ruleCodesIn("la diferencia entre EXTERNAL_NO_FALLBACK y MISSING_RESILIENCE")).toEqual([
      "EXTERNAL_NO_FALLBACK",
      "MISSING_RESILIENCE",
    ]);
    expect(ruleCodesIn("es el punto único de fallo más caro del dibujo (SPOF).")).toEqual(["SPOF"]);
  });

  it("leaves the acronyms that are not rules alone", () => {
    expect(ruleCodesIn("No la encadenes delante de otra API tuya")).toEqual([]);
    expect(ruleCodesIn("un HTTP que no espera respuesta")).toEqual([]);
    expect(ruleCodesIn("lo normal contra un CDN sería Sql, no HTTP ni AMQP")).toEqual([]);
  });

  it("marks nothing in these texts that is not one of the catalogue's rules", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      const sentences = [
        ...TYPES.map((type) => t(NODE_META[type].avoid)),
        ...TERM_IDS.map((id) => t(termDefinition(id))),
      ];

      for (const sentence of sentences) {
        for (const code of ruleCodesIn(sentence)) {
          expect(CODES, sentence).toContain(code);
        }
      }
    }
  });

  it("names a rule in most of the sixteen 'when not to use it' sentences", () => {
    const t = createTranslator("es");
    const naming = TYPES.filter((type) => ruleCodesIn(t(NODE_META[type].avoid)).length > 0);

    expect(naming.length).toBeGreaterThanOrEqual(12);
  });
});
