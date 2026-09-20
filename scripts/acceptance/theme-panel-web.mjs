import playwright from "/home/juanp/archForge/node_modules/playwright/index.js";

const { chromium } = playwright;

const BASE = process.env.BONDSTONE_URL ?? "http://localhost:8080";
const API = `${BASE}/api`;
const G = (n) => `${n.toString(16).padStart(8, "0")}-0000-4000-8000-000000000000`;

const node = (i, type, name, x, y, configuration = {}) => ({ id: G(i), type, name, position: { x, y }, configuration });
const edge = (i, a, b, configuration = {}) => ({ id: G(100 + i), sourceNodeId: G(a), targetNodeId: G(b), synchronous: true, configuration });

// Primero un dibujo flojo y después uno mejor: dos mediciones con áreas distintas, que es lo que
// «Qué llevas aprendido» necesita para tener algo que decir.
const WEAK = {
  nodes: [node(1, "Client", "Navegador", 0, 0, { requestsPerSecond: 400 }), node(2, "Gateway", "Gateway", 240, 0), node(3, "Api", "API", 480, 0), node(4, "Service", "Pedidos", 720, -120), node(5, "Service", "Pagos", 720, 120), node(6, "Database", "Postgres", 960, 0), node(7, "Service", "Huerfano", 240, 320)],
  connections: [edge(0, 1, 2), edge(1, 2, 3), edge(2, 3, 4), edge(3, 3, 5), edge(4, 4, 6), edge(5, 5, 6), edge(6, 6, 3)],
};
const BETTER = {
  nodes: [node(1, "Client", "Navegador", 0, 0, { requestsPerSecond: 50 }), node(3, "Api", "API", 240, 0), node(6, "Database", "Postgres", 480, 0)],
  connections: [edge(0, 1, 3), edge(1, 3, 6, { access: "both" })],
};

const results = [];
let failed = 0;
async function check(label, fn) {
  try {
    await fn();
    results.push(["ok", label]);
  } catch (e) {
    failed += 1;
    results.push(["FALLO", `${label} — ${String(e.message ?? e).split("\n")[0].slice(0, 220)}`]);
  }
}
const must = (cond, msg) => { if (!cond) throw new Error(msg); };
async function waitFor(fn, { timeout = 15000, step = 150, what = "condición" } = {}) {
  const until = Date.now() + timeout;
  let last;
  for (;;) {
    try { if (await fn()) return; } catch (e) { last = e; }
    if (Date.now() > until) throw new Error(`agotado esperando ${what}${last ? ` (${last.message})` : ""}`);
    await new Promise((r) => setTimeout(r, step));
  }
}
const visible = (loc) => loc.first().isVisible().catch(() => false);
const countOf = (loc) => loc.count();

function watch(page, bag) {
  page.on("console", (m) => { if (m.type() === "error") bag.console.push(m.text().slice(0, 300)); });
  page.on("pageerror", (e) => bag.console.push(`pageerror: ${String(e.message).slice(0, 300)}`));
  page.on("requestfailed", (r) => {
    const err = r.failure()?.errorText ?? "";
    if (!err.includes("ERR_ABORTED")) bag.requests.push(`${r.method()} ${r.url()} — ${err}`);
  });
  page.on("response", (r) => { if (r.status() >= 400) bag.requests.push(`${r.request().method()} ${new URL(r.url()).pathname} ${r.status()}`); });
}
async function noOverflow(page, where) {
  const o = await page.evaluate(() => ({ sw: Math.round(document.documentElement.scrollWidth), iw: Math.round(window.innerWidth) }));
  must(o.sw <= o.iw + 1, `desbordamiento horizontal en ${where}: ${o.sw} > ${o.iw}`);
}
const bg = (loc) => loc.first().evaluate((el) => getComputedStyle(el).backgroundColor);
const prefs = (page) => page.evaluate(() => JSON.parse(localStorage.getItem("bondstone.editor.ui") || "{}"));
const appearanceOf = (page) => page.evaluate(() => document.documentElement.getAttribute("data-appearance"));
const modeButton = (page, name) => page.getByRole("button", { name, exact: true });

async function main() {
  const created = await fetch(`${API}/architectures`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: "QA claro, asa y web" }) }).then((r) => r.json());
  const EDIT = created.editToken;
  const SHARE = created.shareToken;
  console.log(`arquitectura de usar y tirar: share ${SHARE}`);
  const put = (body, version) => fetch(`${API}/architectures/edit/${EDIT}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: "QA claro, asa y web", version, ...body }) });
  let r = await put(WEAK, 1);
  must(r.ok, `la primera semilla no entró: ${r.status}`);
  r = await put(BETTER, 2);
  must(r.ok, `la segunda semilla no entró: ${r.status}`);

  const browser = await chromium.launch();
  const bag = { console: [], requests: [] };
  const savedWork = JSON.stringify([{ editToken: EDIT, shareToken: SHARE, name: "QA claro, asa y web", challengeSlug: null, createdAt: 1789000000000, updatedAt: 1789000000000, score: 95 }]);

  try {
    // ── 1440 · el editor en edición ───────────────────────────────────────
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "es-ES" });
    const page = await ctx.newPage();
    watch(page, bag);
    await page.goto(`${BASE}/e/${EDIT}`);
    await page.waitForSelector(".react-flow__node", { timeout: 20000 });
    const root = page.locator("[data-theme]").first();
    const panel = page.locator("aside").filter({ has: page.getByRole("heading", { name: "Análisis" }) });
    const handle = page.getByRole("separator", { name: "Ajustar el ancho del panel" });

    await check("aspecto/1440 · el editor arranca oscuro, sin preferencia puesta, y la barra ofrece «Modo claro» tras ES/EN", async () => {
      must((await root.getAttribute("data-theme")) === "dark", "no arranca en oscuro");
      must((await appearanceOf(page)) === null, `la web arranca con una preferencia puesta: ${await appearanceOf(page)}`);
      must(await visible(modeButton(page, "Modo claro")), "sin el botón de modo claro");
      must((await bg(page.locator(".react-flow"))) === "rgb(10, 12, 14)", `el lienzo oscuro no es #0a0c0e: ${await bg(page.locator(".react-flow"))}`);
    });

    await check("aspecto/1440 · «Modo claro» cambia los tokens: lienzo #ebeae4, panel #fafaf7, y el botón pasa a «Modo oscuro»", async () => {
      await modeButton(page, "Modo claro").click();
      await waitFor(async () => (await root.getAttribute("data-theme")) === "light", { what: "data-theme=light" });
      must((await appearanceOf(page)) === "light", "la raíz no lleva data-appearance=light");
      must((await bg(page.locator(".react-flow"))) === "rgb(235, 234, 228)", `el suelo del lienzo no es #ebeae4: ${await bg(page.locator(".react-flow"))}`);
      must((await bg(panel)) === "rgb(250, 250, 247)", `el panel no es #fafaf7: ${await bg(panel)}`);
      const ink = await page.getByRole("heading", { name: "Análisis" }).evaluate((el) => getComputedStyle(el).color);
      must(ink === "rgb(16, 16, 18)", `el título del panel no es --af-ink en claro: ${ink}`);
      must(await visible(modeButton(page, "Modo oscuro")), "el botón no pasó a «Modo oscuro»");
    });

    await check("claro/1440 · el nodo lee los tokens nuevos: filete --af-line-2 y placa al 4 %", async () => {
      const border = () => page.locator(".af-node").first().evaluate((el) => getComputedStyle(el).borderColor);
      await waitFor(async () => (await border()) === "rgb(207, 207, 198)", { timeout: 3000, what: `el filete del nodo en #cfcfc6 (transiciona 240 ms; ahora ${await border()})` });
      const plate = await page.locator(".af-node").first().evaluate((el) => getComputedStyle(el).backgroundColor);
      must(plate !== "rgb(255, 255, 255)" && plate !== "rgb(20, 23, 29)", `la placa no lleva tinte sobre blanco: ${plate}`);
    });

    await check("aspecto/1440 · la preferencia se guarda como appearance:\"light\" y sobrevive a la recarga", async () => {
      must((await prefs(page)).appearance === "light", "appearance no se guardó");
      await page.reload();
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      must((await root.getAttribute("data-theme")) === "light", "tras recargar no sigue en claro");
    });

    await check("claro/1440 · el cerco de foco por teclado es cobalto en claro y blanco en oscuro", async () => {
      const focused = () => page.evaluate(() => {
        const el = document.activeElement;
        const cs = getComputedStyle(el);
        return { visible: el.matches(":focus-visible"), style: cs.outlineStyle, color: cs.outlineColor, name: (el.getAttribute("aria-label") || el.textContent || "").slice(0, 30) };
      });
      await page.keyboard.press("Tab");
      await new Promise((r) => setTimeout(r, 400));
      let f = await focused();
      must(f.visible && f.style === "solid", `sin cerco de foco por teclado en claro (${JSON.stringify(f)})`);
      must(f.color === "rgb(31, 63, 209)", `el cerco en claro no es cobalto: ${f.color} sobre «${f.name}»`);
      await modeButton(page, "Modo oscuro").click();
      await waitFor(async () => (await root.getAttribute("data-theme")) === "dark", { what: "volver a oscuro" });
      must((await prefs(page)).appearance === "dark", "appearance no pasó a dark");
      await page.reload();
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      await page.keyboard.press("Tab");
      await new Promise((r) => setTimeout(r, 400));
      f = await focused();
      must(f.visible && f.style === "solid", `sin cerco de foco por teclado en oscuro (${JSON.stringify(f)})`);
      must(f.color === "rgb(255, 255, 255)", `el cerco en oscuro no es blanco: ${f.color} sobre «${f.name}»`);
    });

    await check("asa/1440 · el panel arranca a 336 px con su asa accesible", async () => {
      must((await countOf(handle)) === 1, "no hay asa");
      must((await handle.getAttribute("aria-valuenow")) === "336", `aria-valuenow ${await handle.getAttribute("aria-valuenow")}`);
      const box = await panel.first().boundingBox();
      must(box && Math.round(box.width) === 336, `el panel mide ${box && Math.round(box.width)}`);
    });

    await check("asa/1440 · con el teclado: ← ensancha 16 px, → estrecha, y no baja de 336", async () => {
      await handle.focus();
      await page.keyboard.press("ArrowLeft");
      await waitFor(async () => (await handle.getAttribute("aria-valuenow")) === "352", { what: "352 tras ←" });
      await page.keyboard.press("ArrowRight");
      await page.keyboard.press("ArrowRight");
      await waitFor(async () => (await handle.getAttribute("aria-valuenow")) === "336", { what: "336 como mínimo" });
    });

    await check("asa/1440 · arrastrando con el ratón el panel se ensancha, el lienzo cede y la anchura se recuerda", async () => {
      const hb = await handle.boundingBox();
      const canvasBefore = (await page.locator(".react-flow").first().boundingBox()).width;
      await page.mouse.move(hb.x + hb.width / 2, hb.y + hb.height / 2);
      await page.mouse.down();
      await page.mouse.move(hb.x - 120, hb.y + hb.height / 2, { steps: 10 });
      must(await visible(page.getByText(/^\d+ px$/)), "mientras se arrastra no se ve la cifra");
      await page.mouse.up();
      await waitFor(async () => Number(await handle.getAttribute("aria-valuenow")) >= 440, { what: "un panel más ancho" });
      const width = Number(await handle.getAttribute("aria-valuenow"));
      const box = await panel.first().boundingBox();
      must(Math.round(box.width) === width, `el aside mide ${Math.round(box.width)} y el asa dice ${width}`);
      const canvasAfter = (await page.locator(".react-flow").first().boundingBox()).width;
      must(canvasAfter < canvasBefore, "el lienzo no cedió sitio");
      must((await prefs(page)).panelWidth === width, "panelWidth no se guardó");
      await page.reload();
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      must((await handle.getAttribute("aria-valuenow")) === String(width), "tras recargar no recuerda la anchura");
      must(Math.round((await panel.first().boundingBox()).width) === width, "el aside no volvió con su anchura");
    });

    await check("asa/1440 · no pasa de 560 px", async () => {
      const hb = await handle.boundingBox();
      await page.mouse.move(hb.x + hb.width / 2, hb.y + hb.height / 2);
      await page.mouse.down();
      await page.mouse.move(hb.x - 600, hb.y + hb.height / 2, { steps: 10 });
      await page.mouse.up();
      await waitFor(async () => (await handle.getAttribute("aria-valuenow")) === "560", { what: "el máximo de 560" });
      await noOverflow(page, "/e/ con el panel a 560");
      await handle.focus();
      for (let i = 0; i < 14; i += 1) await page.keyboard.press("ArrowRight");
      await waitFor(async () => (await handle.getAttribute("aria-valuenow")) === "336", { what: "volver a 336" });
    });

    await check("aspecto+asa/1440 · en /a/:token están el mismo botón de aspecto y la misma asa", async () => {
      await page.goto(`${BASE}/a/${SHARE}`);
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      must(await visible(modeButton(page, "Modo claro")), "sin botón de aspecto en lectura");
      must((await countOf(page.getByRole("separator", { name: "Ajustar el ancho del panel" }))) === 1, "sin asa en lectura");
      await modeButton(page, "Modo claro").click();
      await waitFor(async () => (await page.locator("[data-theme]").first().getAttribute("data-theme")) === "light", { what: "claro en lectura" });
      must((await bg(page.locator(".react-flow"))) === "rgb(235, 234, 228)", "el lienzo de lectura no se puso claro");
      await noOverflow(page, "/a/ en claro");
      await modeButton(page, "Modo oscuro").click();
    });

    // ── la web en oscuro ──────────────────────────────────────────────────
    await check("aspecto/1440 · en la web «Oscuro» baja el suelo a #0f1115 y sube el motor a #14171d", async () => {
      await page.goto(`${BASE}/`);
      await page.evaluate(() => localStorage.removeItem("bondstone.editor.ui"));
      await page.reload();
      await page.waitForSelector(".af-engine", { timeout: 20000 });
      must((await appearanceOf(page)) === null, "la web no arranca sin preferencia");
      must((await bg(page.locator("body"))) === "rgb(250, 250, 247)", `la web no arranca en #fafaf7: ${await bg(page.locator("body"))}`);
      must((await bg(page.locator(".af-engine"))) === "rgb(15, 17, 21)", `el motor en claro no es #0f1115: ${await bg(page.locator(".af-engine"))}`);
      await modeButton(page, "Modo oscuro").click();
      await waitFor(async () => (await appearanceOf(page)) === "dark", { what: "data-appearance=dark" });
      must((await bg(page.locator("body"))) === "rgb(15, 17, 21)", `el suelo oscuro no es #0f1115: ${await bg(page.locator("body"))}`);
      must((await bg(page.locator(".af-engine"))) === "rgb(20, 23, 29)", `el motor no subió a #14171d: ${await bg(page.locator(".af-engine"))}`);
      const ink = await page.getByRole("heading", { level: 1 }).first().evaluate((el) => getComputedStyle(el).color);
      must(ink === "rgb(238, 240, 244)", `el titular no es --af-panel-ink: ${ink}`);
      await noOverflow(page, "la portada en oscuro");
    });

    await check("aspecto/1440 · la web oscura sobrevive a la recarga y el editor va con ella", async () => {
      await page.reload();
      await page.waitForSelector(".af-engine", { timeout: 20000 });
      must((await appearanceOf(page)) === "dark", "tras recargar la web no sigue oscura");
      must((await prefs(page)).appearance === "dark", "appearance no se guardó como dark");
      await page.goto(`${BASE}/a/${SHARE}`);
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      must((await page.locator("[data-theme]").first().getAttribute("data-theme")) === "dark", "el editor no acompaña al oscuro");
      must((await bg(page.locator(".react-flow"))) === "rgb(10, 12, 14)", "el lienzo dejó de ser #0a0c0e");
      await page.evaluate(() => localStorage.removeItem("bondstone.editor.ui"));
      await page.goto(`${BASE}/`);
      await page.waitForSelector(".af-engine", { timeout: 20000 });
      must((await appearanceOf(page)) === null, "sin preferencia guardada la web no vuelve al claro");
      must((await bg(page.locator("body"))) === "rgb(250, 250, 247)", "sin preferencia el suelo no vuelve a #fafaf7");
    });

    // ── web · el umbral de los avisos y el aviso de «Empezar de cero» ─────
    await check("web/1440 · ningún aviso de pantalla estrecha en /challenges, /c/:slug ni la portada", async () => {
      await page.goto(`${BASE}/challenges`);
      await page.waitForSelector("text=Comenzar desde cero", { timeout: 20000 });
      must(!(await visible(page.getByText(/no hay herramientas de dibujo/))), "el aviso de «Empezar de cero» sale a 1440");
      await page.goto(`${BASE}/c/single-point-of-failure`);
      await page.waitForSelector("text=Comenzar el reto", { timeout: 20000 });
      must(!(await visible(page.getByText(/En una pantalla estrecha es posible consultar/))), "el aviso del reto sale a 1440");
    });

    await ctx.close();

    // ── 1000 px · la franja que antes no avisaba ──────────────────────────
    const ctx3 = await browser.newContext({ viewport: { width: 1000, height: 800 }, locale: "es-ES" });
    const p3 = await ctx3.newPage();
    watch(p3, bag);
    await check("web/1000 · entre 768 y 1023 los avisos ya salen: en el reto, en «Empezar de cero» y bajo la demo", async () => {
      await p3.goto(`${BASE}/c/single-point-of-failure`);
      await p3.waitForSelector("text=Comenzar el reto", { timeout: 20000 });
      must(await visible(p3.getByText(/En una pantalla estrecha es posible consultar/)), "el aviso del reto no sale a 1000");
      await p3.goto(`${BASE}/challenges`);
      await p3.waitForSelector("text=Comenzar desde cero", { timeout: 20000 });
      must(await visible(p3.getByText(/no hay herramientas de dibujo/)), "el aviso de «Empezar de cero» no sale a 1000");
      await p3.goto(`${BASE}/`);
      await p3.waitForSelector("text=Se abre en modo de solo lectura", { timeout: 20000 });
      must(await visible(p3.getByText(/Se abre en modo de solo lectura/)), "el aviso bajo la demo no sale a 1000");
      await noOverflow(p3, "/challenges a 1000");
    });
    await check("web/1000 · y a 1000 el editor, en efecto, no deja editar", async () => {
      await p3.goto(`${BASE}/e/${EDIT}`);
      await p3.waitForSelector(".react-flow__node", { timeout: 20000 });
      must((await countOf(p3.getByText("Componentes", { exact: true }))) === 0, "a 1000 hay paleta");
      must(await visible(p3.getByText(/Se abre en modo de solo lectura/)), "a 1000 no lo dice");
    });
    await ctx3.close();

    // ── «Qué llevas aprendido» ────────────────────────────────────────────
    const ctx4 = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "es-ES" });
    await ctx4.addInitScript((work) => localStorage.setItem("bondstone.architectures", work), savedWork);
    const p4 = await ctx4.newPage();
    watch(p4, bag);
    await check("web/1440 · «Progreso por áreas» aparece en /challenges con las subidas por área de tu trabajo", async () => {
      await p4.goto(`${BASE}/challenges`);
      await waitFor(async () => await visible(p4.getByRole("heading", { name: "Progreso por áreas" })), { timeout: 20000, what: "la sección" });
      const chips = p4.locator("li").filter({ hasText: "→" });
      must((await countOf(chips)) >= 1, "sin ninguna subida");
      const txt = await chips.first().textContent();
      must(/\d+ → \d+/.test(txt ?? ""), `el chip no lleva dos lecturas: ${txt}`);
      must(await visible(p4.getByText("Las áreas que han mejorado")), "sin la entradilla");
      await noOverflow(p4, "/challenges con aprendido");
    });
    await ctx4.close();
    const ctx5 = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "es-ES" });
    const p5 = await ctx5.newPage();
    watch(p5, bag);
    await check("web/1440 · sin trabajo guardado la sección no existe", async () => {
      await p5.goto(`${BASE}/challenges`);
      await p5.waitForSelector("text=Comenzar desde cero", { timeout: 20000 });
      await new Promise((r) => setTimeout(r, 800));
      must((await countOf(p5.getByRole("heading", { name: "Progreso por áreas" }))) === 0, "sale sin trabajo");
    });
    await ctx5.close();

    // ── 390 · el menú estrecho ────────────────────────────────────────────
    const ctx2 = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: "es-ES", isMobile: true, hasTouch: true });
    const p2 = await ctx2.newPage();
    watch(p2, bag);
    await p2.goto(`${BASE}/e/${EDIT}`);
    await p2.waitForSelector(".react-flow__node", { timeout: 20000 });
    await check("aspecto/390 · el menú trae la fila «Aspecto» bajo «Idioma» con dos opciones, y «Claro» cambia el tema", async () => {
      await p2.getByRole("button", { name: "Abrir el menú" }).click();
      const group = p2.getByRole("radiogroup", { name: "Aspecto" });
      await waitFor(async () => await visible(group), { what: "la fila Aspecto" });
      must((await countOf(group.getByRole("radio"))) === 2, "no hay dos opciones");
      await group.getByRole("radio", { name: "Claro" }).click();
      await waitFor(async () => (await p2.locator("[data-theme]").first().getAttribute("data-theme")) === "light", { what: "claro en estrecho" });
      must((await prefs(p2)).appearance === "light", "no se guardó en estrecho");
      await p2.keyboard.press("Escape");
      await waitFor(async () => (await countOf(p2.getByRole("dialog"))) === 0, { what: "cerrar el menú" });
      must((await bg(p2.locator(".react-flow"))) === "rgb(235, 234, 228)", "el lienzo estrecho no se puso claro");
      await noOverflow(p2, "/e/ a 390 en claro");
    });
    await check("web/390 · el aviso de «Empezar de cero» sale y el de la portada también", async () => {
      await p2.goto(`${BASE}/challenges`);
      await p2.waitForSelector("text=Comenzar desde cero", { timeout: 20000 });
      must(await visible(p2.getByText(/no hay herramientas de dibujo/)), "sin aviso a 390");
      await noOverflow(p2, "/challenges a 390");
    });
    await ctx2.close();
  } finally {
    await browser.close().catch(() => {});
    const del = await fetch(`${API}/architectures/edit/${EDIT}`, { method: "DELETE" });
    console.log(`\nborrado de la arquitectura de prueba: ${del.status}`);
  }

  console.log("");
  for (const [state, label] of results) console.log(`${state === "ok" ? "  ok " : "FALLO"} ${label}`);
  console.log(`\n${results.length - failed}/${results.length} comprobaciones en verde`);
  console.log(`errores de consola: ${bag.console.length}`);
  for (const c of bag.console.slice(0, 10)) console.log(`   · ${c}`);
  console.log(`peticiones fallidas: ${bag.requests.length}`);
  for (const c of bag.requests.slice(0, 10)) console.log(`   · ${c}`);
  const clean = failed === 0 && bag.console.length === 0 && bag.requests.length === 0;
  console.log(clean ? "\nPASADA EN VERDE" : "\nPASADA CON FALLOS");
  process.exit(clean ? 0 : 1);
}

main().catch((e) => { console.error(e); process.exit(1); });
