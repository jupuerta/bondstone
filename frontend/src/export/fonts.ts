import type { Font, Glyph } from "opentype.js";

export interface OpenType {
  Font: typeof Font;
  Glyph: typeof Glyph;
}

export interface FontSource {
  family: string;
  weight: number;
  urls: readonly string[];
}

export interface EmbeddedFace {
  family: string;
  weight: number;
  bytes: number;
  dataUrl: string;
}

export function charactersOf(text: string): string[] {
  return [...new Set([...text])].filter((character) => character !== "\n" && character !== "\r");
}

export function subsetFont(opentype: OpenType, sources: readonly Font[], text: string): ArrayBuffer | null {
  const [first] = sources;
  if (!first) return null;

  const notdef = first.glyphs.get(0);
  const kept = new Map<string, Glyph>();

  for (const character of charactersOf(text)) {
    for (const font of sources) {
      const index = font.charToGlyphIndex(character);
      if (index === 0) continue;
      const glyph = font.glyphs.get(index);
      const key = `${sources.indexOf(font)}:${index}`;
      if (!kept.has(key)) kept.set(key, glyph);
      break;
    }
  }

  if (kept.size === 0) return null;

  const names = first.names.windows ?? first.names;
  const glyphs = [
    new opentype.Glyph({ name: ".notdef", advanceWidth: notdef.advanceWidth, path: notdef.path }),
    ...[...kept.values()].map(
      (glyph, position) =>
        new opentype.Glyph({
          name: glyph.name ?? `g${position + 1}`,
          unicode: glyph.unicode,
          unicodes: glyph.unicodes,
          advanceWidth: glyph.advanceWidth,
          path: glyph.path,
        }),
    ),
  ];

  return new opentype.Font({
    familyName: names.fontFamily?.en ?? "Subset",
    styleName: names.fontSubfamily?.en ?? "Regular",
    unitsPerEm: first.unitsPerEm,
    ascender: first.ascender,
    descender: first.descender,
    glyphs,
  }).toArrayBuffer();
}

export function toDataUrl(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let at = 0; at < bytes.length; at += 0x8000) binary += String.fromCharCode(...bytes.subarray(at, at + 0x8000));
  return `data:font/otf;base64,${btoa(binary)}`;
}

export function fontFaceRule(face: EmbeddedFace): string {
  return `@font-face{font-family:"${face.family}";font-weight:${face.weight};font-style:normal;src:url(${face.dataUrl}) format("opentype")}`;
}

export function fontFaceStyle(faces: readonly EmbeddedFace[]): string {
  return faces.map(fontFaceRule).join("\n");
}
