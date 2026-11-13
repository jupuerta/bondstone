#!/usr/bin/env node
// La pasada de accesibilidad, con axe sobre la pila levantada. axe es lo que hay debajo de la
// puntuación de accesibilidad de Lighthouse, así que esto es esa pasada sin el resto del informe.
//
// Cada página se mira **cuatro veces**: claro y oscuro, español e inglés. Las tres primeras cosas
// que rompen el contraste en este producto son el modo, el tema del editor y una frase larga que
// en el otro idioma no cabe, y una pasada en un solo estado no las ve.
//
//   node scripts/a11y/run.mjs [origen]        (por omisión http://localhost:8080)
//
// Devuelve 1 si hay una sola infracción, con la regla, el impacto y el selector.
import { chromium } from "/home/juanp/archForge/node_modules/playwright/index.mjs";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const ORIGIN = process.argv[2] ?? process.env.BONDSTONE_ORIGIN ?? "http://localhost:8080";
const AXE = readFileSync(fileURLToPath(new URL("../../node_modules/axe-core/axe.min.js", import.meta.url)), "utf8");

// Las cuatro que nombra la fase 3, más las tres de referencia: todo lo que un visitante puede abrir
// sin tener un enlace de nadie.
//
// `/a/<token>` es el editor en lectura, y entra solo si se le pasa un token — `BONDSTONE_SHARE`, o
// el de la demostración de la portada (`VITE_DEMO_BEFORE_TOKEN` del `.env`). Es la única forma de
// que esta pasada vea la paleta del editor, que es distinta de la de la web y tiene sus propios
// problemas de contraste: el claro del editor fallaba por 4,25:1 y ninguna página de la web lo
// enseñaba. Sin token se salta, y lo dice.
const SHARE = process.env.BONDSTONE_SHARE ?? "";

const PAGES = [
  { name: "portada", path: "/" },
  { name: "retos", path: "/challenges" },
  { name: "reto", path: "/c/million-a-day" },
  { name: "reglas", path: "/rules" },
  { name: "modelo", path: "/model" },
  { name: "mecanismos", path: "/mechanisms" },
  { name: "legal", path: "/privacidad" },
  ...(SHARE ? [{ name: "editor (lectura)", path: `/a/${encodeURIComponent(SHARE)}`, settle: ".react-flow__node" }] : []),
];

const STATES = [
  { name: "claro · es", appearance: "light", locale: "es" },
  { name: "oscuro · es", appearance: "dark", locale: "es" },
  { name: "claro · en", appearance: "light", locale: "en" },
  { name: "oscuro · en", appearance: "dark", locale: "en" },
];

const TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

const browser = await chromium.launch();
const problems = [];
let checked = 0;

for (const state of STATES) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });

  // El idioma y el aspecto se leen del almacenamiento local antes del primer dibujo, así que hay
  // que sembrarlos en el origen antes de navegar: ponerlos después solo prueba el estado por
  // omisión cuatro veces.
  await context.addInitScript(
    ([appearance, locale]) => {
      try {
        window.localStorage.setItem("bondstone.locale", locale);
        window.localStorage.setItem("bondstone.editor.ui", JSON.stringify({ appearance }));
      } catch {}
    },
    [state.appearance, state.locale],
  );

  const page = await context.newPage();

  for (const target of PAGES) {
    const response = await page.goto(`${ORIGIN}${target.path}`, { waitUntil: "networkidle" });
    if (!response?.ok()) {
      problems.push({ state: state.name, page: target.name, id: "http", impact: "critical", help: `la página respondió ${response?.status()}`, nodes: [] });
      continue;
    }

    // El lienzo dibuja después de la primera respuesta: sin esperar a un nodo, axe mira el
    // esqueleto de carga y da todo por bueno.
    if (target.settle) await page.waitForSelector(target.settle, { timeout: 20000 });

    await page.evaluate(AXE);
    const result = await page.evaluate(
      (tags) => window.axe.run(document, { runOnly: { type: "tag", values: tags }, resultTypes: ["violations"] }),
      TAGS,
    );

    checked += 1;
    for (const violation of result.violations) {
      problems.push({
        state: state.name,
        page: target.name,
        id: violation.id,
        impact: violation.impact,
        help: violation.help,
        nodes: violation.nodes.slice(0, 3).map((node) => ({ target: node.target.join(" "), summary: node.failureSummary?.split("\n")[1]?.trim() ?? "" })),
      });
    }
  }

  await context.close();
}

await browser.close();

console.log(`axe — ${checked} páginas revisadas (${PAGES.length} × ${STATES.length}), reglas ${TAGS.join(", ")}`);
if (!SHARE) console.log("Sin BONDSTONE_SHARE: el editor en lectura (/a/<token>) no se ha mirado.");

if (problems.length === 0) {
  console.log("Sin infracciones.");
  process.exit(0);
}

for (const problem of problems) {
  console.log(`\n· ${problem.page} (${problem.state}) — ${problem.id} [${problem.impact}]`);
  console.log(`  ${problem.help}`);
  for (const node of problem.nodes) console.log(`    ${node.target}\n      ${node.summary}`);
}

console.log(`\n${problems.length} infracción(es).`);
process.exit(1);
