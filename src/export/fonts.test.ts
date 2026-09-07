import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { describe, expect, it } from "vitest";
import * as opentype from "opentype.js";
import { charactersOf, fontFaceRule, subsetFont, toDataUrl } from "./fonts";
import { embedFonts } from "./embedFonts";

const require = createRequire(import.meta.url);

function woff(specifier: string): ArrayBuffer {
  const bytes = readFileSync(require.resolve(specifier));
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

const BRICOLAGE = [
  "@fontsource/bricolage-grotesque/files/bricolage-grotesque-latin-600-normal.woff",
  "@fontsource/bricolage-grotesque/files/bricolage-grotesque-latin-ext-600-normal.woff",
];

const MONO = ["@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff"];

describe("the characters a face has to carry", () => {
  it("are each written once, whatever the text repeats", () => {
    expect(charactersOf("Tienda de la demo")).toEqual([..."Tienda lmo"]);
  });

  it("do not include line breaks, which no glyph draws", () => {
    expect(charactersOf("a\nb\r\n")).toEqual(["a", "b"]);
  });
});

describe("the subset that travels inside the postcard", () => {
  const sources = BRICOLAGE.map((specifier) => opentype.parse(woff(specifier)));

  it("carries exactly the glyphs the text uses, and .notdef", () => {
    const subset = opentype.parse(subsetFont(opentype, sources, "Tienda 61")!);

    expect(subset.glyphs.length).toBe(charactersOf("Tienda 61").length + 1);
    for (const character of charactersOf("Tienda 61")) {
      expect(subset.charToGlyphIndex(character), character).not.toBe(0);
    }
    expect(subset.charToGlyphIndex("z")).toBe(0);
  });

  it("keeps the advance of every glyph it copies, so the line breaks where the preview broke it", () => {
    const subset = opentype.parse(subsetFont(opentype, sources, "Tienda")!);

    for (const character of charactersOf("Tienda")) {
      expect(subset.charToGlyph(character).advanceWidth).toBe(sources[0].charToGlyph(character).advanceWidth);
    }
    expect(subset.unitsPerEm).toBe(sources[0].unitsPerEm);
  });

  it("reaches into the extended file for a letter the first one does not have", () => {
    expect(sources[0].charToGlyphIndex("Ł")).toBe(0);
    expect(sources[1].charToGlyphIndex("Ł")).not.toBe(0);

    const subset = opentype.parse(subsetFont(opentype, sources, "Łódź")!);

    expect(subset.charToGlyphIndex("Ł")).not.toBe(0);
    expect(subset.charToGlyphIndex("ź")).not.toBe(0);
    expect(subset.charToGlyphIndex("ó")).not.toBe(0);
    expect(subset.glyphs.length).toBe(5);
  });

  it("weighs a fraction of the file it came from", () => {
    const subset = subsetFont(opentype, sources, "Tienda de la demo · 61 / 100")!;

    expect(subset.byteLength).toBeLessThan(woff(BRICOLAGE[0]).byteLength / 3);
  });

  it("has nothing to say about a text no source can draw", () => {
    expect(subsetFont(opentype, sources, "漢字")).toBeNull();
    expect(subsetFont(opentype, [], "abc")).toBeNull();
  });
});

describe("the @font-face that declares it", () => {
  it("names the family and the weight the postcard asks for, and points at a data URL", () => {
    const rule = fontFaceRule({ family: "Bricolage Grotesque", weight: 600, bytes: 3, dataUrl: toDataUrl(new Uint8Array([1, 2, 3]).buffer) });

    expect(rule).toContain('font-family:"Bricolage Grotesque"');
    expect(rule).toContain("font-weight:600");
    expect(rule).toContain("src:url(data:font/otf;base64,AQID)");
    expect(rule).not.toMatch(/https?:/);
  });

  it("embeds one face per source, each with its own text, and skips a face with nothing to write", async () => {
    const faces = await embedFonts(
      [
        { source: { family: "Bricolage Grotesque", weight: 600, urls: BRICOLAGE }, text: "Tienda" },
        { source: { family: "IBM Plex Mono", weight: 400, urls: MONO }, text: "61 / 100" },
        { source: { family: "IBM Plex Mono", weight: 600, urls: MONO }, text: "" },
      ],
      async (url) => woff(url),
    );

    expect(faces.map((face) => `${face.family} ${face.weight}`)).toEqual(["Bricolage Grotesque 600", "IBM Plex Mono 400"]);
    for (const face of faces) {
      expect(face.dataUrl.startsWith("data:font/otf;base64,")).toBe(true);
      expect(face.bytes).toBeGreaterThan(0);
    }
  });
});
