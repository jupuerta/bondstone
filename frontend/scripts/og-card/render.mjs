#!/usr/bin/env node
// Rasteriza `card.html` a `public/og.png` con el Chromium de Playwright, que es el que rasteriza
// todo lo demás en este proyecto. 1200x630 es el tamaño que piden Open Graph y la tarjeta de
// Twitter, y se dibuja a escala 1 —no a 2x— porque el límite de LinkedIn y WhatsApp para
// previsualizar está en unos pocos cientos de kilobytes y una imagen que no cabe no se enseña.
//
//   node scripts/og-card/render.mjs [--open]
import { chromium } from "/home/juanp/archForge/node_modules/playwright/index.mjs";
import { fileURLToPath } from "node:url";
import { statSync } from "node:fs";

const card = fileURLToPath(new URL("./card.html", import.meta.url));
const out = fileURLToPath(new URL("../../public/og.png", import.meta.url));

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });

await page.goto(`file://${card}`);
// Las cuatro tipografías van incrustadas desde node_modules: sin esperarlas, la primera captura
// sale con la sustituta del sistema y nadie lo nota hasta verla publicada.
await page.evaluate(() => document.fonts.ready);
await page.screenshot({ path: out, type: "png" });

await browser.close();

const kb = Math.round(statSync(out).size / 1024);
console.log(`og.png — 1200x630, ${kb} KB`);
if (kb > 300) console.warn("Pesa más de 300 KB: algunos previsualizadores dejan de enseñarla.");
