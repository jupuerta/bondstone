import { describe, expect, it } from "vitest";
import { legendSections, unsizedTypes, type LegendRow } from "./legend";
import type { NodeType } from "../../../types/architecture";
import { NOT_YOURS_TO_SIZE } from "./loadMeter";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { NODE_GROUPS } from "../palette/nodeMeta";
import { LOCALES, createTranslator } from "../../../i18n";

const MODES = ["design", "simulate"] as const;

describe("the canvas legend", () => {
  it("names exactly the boxes the load meter says nothing about", () => {
    expect(new Set(unsizedTypes())).toEqual(NOT_YOURS_TO_SIZE);
    expect(unsizedTypes()).toHaveLength(NOT_YOURS_TO_SIZE.size);
  });

  it("lists them in the order the palette does, so the two read as the same catalogue", () => {
    const palette = NODE_GROUPS.flatMap((group) => group.types);
    const positions = unsizedTypes().map((type) => palette.indexOf(type));

    expect(positions).not.toContain(-1);
    expect([...positions].sort((a, b) => a - b)).toEqual(positions);
  });

  it("shows every component of the palette, once, whichever mode it is in", () => {
    for (const mode of MODES) {
      const families = legendSections(mode)
        .flatMap((section) => section.rows)
        .flatMap((row) => (row.mark.kind === "family" ? row.mark.types : []));

      expect(families).toEqual(NODE_GROUPS.flatMap((group) => group.types));
    }
  });

  it("is right about the one pair of types that shares a colour", () => {
    const css = readFileSync(join(__dirname, "..", "..", "..", "index.css"), "utf8");
    const tintOf = (type: NodeType) => css.match(new RegExp(`--af-t-${type}:\\s*([^;]+);`))?.[1].trim() ?? type;
    const byTint = new Map<string, NodeType[]>();
    for (const type of NODE_GROUPS.flatMap((group) => group.types)) {
      byTint.set(tintOf(type), [...(byTint.get(tintOf(type)) ?? []), type]);
    }

    const shared = [...byTint.values()].filter((types) => types.length > 1);
    expect(shared).toEqual([["Queue", "Topic"]]);
  });

  it("publishes one mode's vocabulary at a time — never the other's ink as well (D6.2)", () => {
    const design = legendSections("design");
    const simulate = legendSections("simulate");

    expect(design).toHaveLength(3);
    expect(simulate).toHaveLength(3);
    expect(design.slice(0, 2)).toEqual(simulate.slice(0, 2));
    expect(design[2].title).not.toBe(simulate[2].title);

    const boxes = (sections: ReturnType<typeof legendSections>) =>
      sections.flatMap((section) => section.rows).flatMap((row) => (row.mark.kind === "box" ? [row.mark.state] : []));

    expect(boxes(design)).toEqual(["highlighted"]);
    expect(boxes(simulate)).toEqual(["healthy", "degraded", "down", "contained"]);

    const bands = (sections: ReturnType<typeof legendSections>) =>
      sections.flatMap((section) => section.rows).flatMap((row) => (row.mark.kind === "zone" ? [row.mark.down] : []));

    expect(bands(design)).toEqual([false]);
    expect(bands(simulate)).toEqual([true]);
  });

  it("says something in both languages, with nothing left unfilled", () => {
    for (const locale of LOCALES) {
      const t = createTranslator(locale);

      for (const mode of MODES) {
        for (const section of legendSections(mode)) {
          for (const key of [section.title, section.note]) {
            if (key === undefined) continue;
            expect(t(key), `${key} in ${locale}`).not.toBe(key);
          }

          for (const row of section.rows) {
            expect(t(row.label), `${row.label} in ${locale}`).not.toBe(row.label);
            if (row.note === undefined) continue;

            const sentence = t(row.note, row.args);
            expect(sentence, `${row.note} in ${locale}`).not.toBe(row.note);
            expect(sentence, `${row.note} in ${locale}`).not.toMatch(/[{}]/);
          }
        }
      }
    }
  });

  it("quotes the saturation threshold and the toggle's own name rather than a copy of either", () => {
    const rows = legendSections("design").flatMap((section) => section.rows);
    const withArgs = rows.filter((row: LegendRow) => row.args !== undefined);

    expect(withArgs.map((row) => row.note)).toEqual(["editor.legend.loadNote", "editor.legend.badgesNote"]);
    expect(createTranslator("es")(withArgs[0].note!, withArgs[0].args)).toContain("80");
    expect(createTranslator("es")(withArgs[1].note!, withArgs[1].args)).toContain("Marcas");
    expect(createTranslator("en")(withArgs[1].note!, withArgs[1].args)).toContain("Badges");
  });
});
