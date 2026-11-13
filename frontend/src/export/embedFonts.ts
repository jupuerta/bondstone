import { Font, Glyph, parse } from "opentype.js";
import bricolage from "@fontsource/bricolage-grotesque/files/bricolage-grotesque-latin-600-normal.woff?url";
import bricolageExt from "@fontsource/bricolage-grotesque/files/bricolage-grotesque-latin-ext-600-normal.woff?url";
import mono400 from "@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff?url";
import mono400Ext from "@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-ext-400-normal.woff?url";
import mono600 from "@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-600-normal.woff?url";
import mono600Ext from "@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-ext-600-normal.woff?url";
import sans600 from "@fontsource/instrument-sans/files/instrument-sans-latin-600-normal.woff?url";
import sans600Ext from "@fontsource/instrument-sans/files/instrument-sans-latin-ext-600-normal.woff?url";
import { charactersOf, subsetFont, toDataUrl, type EmbeddedFace, type FontSource } from "./fonts";

export const POSTCARD_FACES = {
  display: { family: "Bricolage Grotesque", weight: 600, urls: [bricolage, bricolageExt] },
  mono: { family: "IBM Plex Mono", weight: 400, urls: [mono400, mono400Ext] },
  monoBold: { family: "IBM Plex Mono", weight: 600, urls: [mono600, mono600Ext] },
  sans: { family: "Instrument Sans", weight: 600, urls: [sans600, sans600Ext] },
} satisfies Record<string, FontSource>;

export type PostcardFace = keyof typeof POSTCARD_FACES;

export async function fetchArrayBuffer(url: string): Promise<ArrayBuffer> {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`the font at ${url} could not be loaded`);
  return response.arrayBuffer();
}

export async function embedFonts(
  wanted: ReadonlyArray<{ source: FontSource; text: string }>,
  fetchBuffer: (url: string) => Promise<ArrayBuffer> = fetchArrayBuffer,
): Promise<EmbeddedFace[]> {
  const faces: EmbeddedFace[] = [];
  const parsed = new Map<string, Promise<Font>>();
  const load = (url: string) => {
    let pending = parsed.get(url);
    if (!pending) {
      pending = fetchBuffer(url).then((buffer) => parse(buffer));
      parsed.set(url, pending);
    }
    return pending;
  };

  for (const { source, text } of wanted) {
    if (charactersOf(text).length === 0) continue;
    const fonts = await Promise.all(source.urls.map(load));
    const subset = subsetFont({ Font, Glyph }, fonts, text);
    if (subset === null) continue;
    faces.push({ family: source.family, weight: source.weight, bytes: subset.byteLength, dataUrl: toDataUrl(subset) });
  }

  return faces;
}

export function embedPostcardFonts(texts: Record<PostcardFace, string>, fetchBuffer?: (url: string) => Promise<ArrayBuffer>): Promise<EmbeddedFace[]> {
  return embedFonts(
    (Object.keys(POSTCARD_FACES) as PostcardFace[]).map((face) => ({ source: POSTCARD_FACES[face], text: texts[face] })),
    fetchBuffer,
  );
}
