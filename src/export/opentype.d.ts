declare module "opentype.js" {
  export class Path {}

  export interface GlyphOptions {
    name?: string;
    unicode?: number;
    unicodes?: number[];
    advanceWidth?: number;
    path?: Path;
  }

  export class Glyph {
    constructor(options: GlyphOptions);
    index: number;
    name: string | null;
    unicode: number | undefined;
    unicodes: number[];
    advanceWidth: number;
    readonly path: Path;
  }

  export interface GlyphSet {
    length: number;
    get(index: number): Glyph;
  }

  export interface FontNames {
    fontFamily?: Record<string, string>;
    fontSubfamily?: Record<string, string>;
    windows?: { fontFamily?: Record<string, string>; fontSubfamily?: Record<string, string> };
  }

  export interface FontOptions {
    familyName: string;
    styleName: string;
    unitsPerEm: number;
    ascender: number;
    descender: number;
    glyphs: Glyph[];
  }

  export class Font {
    constructor(options: FontOptions);
    names: FontNames;
    unitsPerEm: number;
    ascender: number;
    descender: number;
    glyphs: GlyphSet;
    charToGlyphIndex(character: string): number;
    charToGlyph(character: string): Glyph;
    toArrayBuffer(): ArrayBuffer;
  }

  export function parse(buffer: ArrayBuffer): Font;

}
