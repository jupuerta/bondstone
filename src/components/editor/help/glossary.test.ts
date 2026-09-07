import { describe, expect, it } from "vitest";
import { GLOSSARY, TERM_IDS, termDefinition, termLabel } from "./glossary";
import { PATTERN_IDS } from "../patterns/patterns";
import { NODE_META } from "../palette/nodeMeta";
import type { NodeType } from "../../../types/architecture";
import { createTranslator } from "../../../i18n";

const LOCALES = ["es", "en"] as const;
const TYPES = Object.keys(NODE_META) as NodeType[];

describe("the glossary (C10)", () => {
  it("has a name and a definition for every term, in both languages", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const id of TERM_IDS) {
        expect(t(termLabel(id)), `${locale} · ${id}`).not.toBe(termLabel(id));
        expect(t(termDefinition(id)), `${locale} · ${id}`).not.toBe(termDefinition(id));
      }
    }
  });

  it("keys every entry by its own id", () => {
    for (const id of TERM_IDS) expect(GLOSSARY[id].id).toBe(id);
  });

  it("only ever points at a drawing the pattern library has", () => {
    for (const id of TERM_IDS) {
      const { pattern } = GLOSSARY[id];
      if (pattern !== undefined) expect(PATTERN_IDS).toContain(pattern);
    }
  });

  it("defines a term for every box the call inspector asks you to tick", () => {
    expect(TERM_IDS).toEqual(
      expect.arrayContaining(["synchronous", "timeout", "retries", "circuitBreaker", "fallback"]),
    );
  });
});

describe("the component sheet (C1)", () => {
  it("says what every one of the sixteen types is, and when not to use it", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const type of TYPES) {
        const { help, avoid } = NODE_META[type];

        expect(t(help), `${locale} · ${type}`).not.toBe(help);
        expect(t(avoid), `${locale} · ${type}`).not.toBe(avoid);
      }
    }
  });

  it("does not answer 'when not to use it' with what it is", () => {
    const t = createTranslator("es");

    for (const type of TYPES) {
      expect(t(NODE_META[type].avoid), type).not.toBe(t(NODE_META[type].help));
    }
  });

  it("has no wiki chip left to promise anything", () => {
    const t = createTranslator("es");

    expect(t("ui.help.wiki")).toBe("ui.help.wiki");
    expect(t("ui.help.wikiPending")).toBe("ui.help.wikiPending");
  });
});
