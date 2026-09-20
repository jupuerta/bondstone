import playwright from "/home/juanp/archForge/node_modules/playwright/index.js";

const { chromium } = playwright;

const BASE = process.env.BONDSTONE_URL ?? "http://localhost:8080";
const API = `${BASE}/api`;
const G = (n) => `${n.toString(16).padStart(8, "0")}-0000-4000-8000-000000000000`;

const NODES = [
  ["Client", "Navegador", 0, 0, { requestsPerSecond: 400 }],
  ["Gateway", "Gateway", 240, 0, {}],
  ["Api", "API", 480, 0, {}],
  ["Service", "Pedidos", 720, -120, {}],
  ["Service", "Pagos", 720, 120, {}],
  ["Database", "Postgres", 960, 0, {}],
  ["Service", "Huerfano", 240, 320, {}],
];
const EDGES = [[1, 2], [2, 3], [3, 4], [3, 5], [4, 6], [5, 6], [6, 3]];

const seed = (name, version) => ({
  name,
  version,
  nodes: NODES.map(([type, nm, x, y, configuration], i) => ({
    id: G(i + 1), type, name: nm, position: { x, y }, configuration,
  })),
  connections: EDGES.map(([a, b], i) => ({
    id: G(100 + i), sourceNodeId: G(a), targetNodeId: G(b), synchronous: true, configuration: {},
  })),
});

const results = [];
let failed = 0;
async function check(label, fn) {
  try {
    await fn();
    results.push(["ok", label]);
  } catch (e) {
    failed += 1;
    results.push(["FALLO", `${label} — ${String(e.message ?? e).split("\n")[0].slice(0, 200)}`]);
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

// El 409 de la comprobación del conflicto es provocado a propósito.
const expectedBadRequests = new Set();

function watch(page, bag) {
  page.on("console", (m) => {
    if (m.type() !== "error") return;
    const t = m.text();
    if (/status of 409/.test(t) && expectedBadRequests.size > 0) return; // el conflicto se provoca a propósito
    bag.console.push(t.slice(0, 300));
  });
  page.on("pageerror", (e) => bag.console.push(`pageerror: ${String(e.message).slice(0, 300)}`));
  page.on("requestfailed", (r) => {
    const err = r.failure()?.errorText ?? "";
    if (err.includes("ERR_ABORTED")) return; // navegación cancelada por el propio test
    bag.requests.push(`${r.method()} ${r.url()} — ${err}`);
  });
  page.on("response", (r) => {
    if (r.status() < 400) return;
    const key = `${r.request().method()} ${new URL(r.url()).pathname} ${r.status()}`;
    if (expectedBadRequests.has(key)) return;
    bag.requests.push(`${key}`);
  });
}

async function noOverflow(page, where) {
  const over = await page.evaluate(() => {
    const d = document.documentElement;
    return { sw: Math.round(d.scrollWidth), iw: Math.round(window.innerWidth) };
  });
  must(over.sw <= over.iw + 1, `desbordamiento horizontal en ${where}: scrollWidth ${over.sw} > innerWidth ${over.iw}`);
}

async function main() {
  const created = await fetch(`${API}/architectures`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name: "QA pasada rebanadas 2 y 3" }),
  }).then((r) => r.json());
  const EDIT = created.editToken;
  const SHARE = created.shareToken;
  console.log(`arquitectura de usar y tirar: share ${SHARE}`);

  const put = (body) =>
    fetch(`${API}/architectures/edit/${EDIT}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

  let r = await put(seed("QA pasada rebanadas 2 y 3", 1));
  must(r.ok, `la semilla no entró: ${r.status}`);

  const browser = await chromium.launch();
  const bag = { console: [], requests: [] };

  try {
    // ── 1440 px ────────────────────────────────────────────────────────────
    const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "es-ES" });
    const page = await ctx.newPage();
    watch(page, bag);

    await page.goto(`${BASE}/e/${EDIT}`);
    const canvas = page.locator(".react-flow");
    const panel = page.locator("aside").filter({ has: page.getByRole("heading", { name: "Análisis" }) });
    const status = page.getByRole("status");

    // ── Rebanada 2 · edición ──────────────────────────────────────────────
    await check("R2/1440 · el editor en edición monta con el lienzo y los 7 nodos sembrados", async () => {
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      await waitFor(async () => (await countOf(page.locator(".react-flow__node"))) === 7, { what: "los 7 nodos" });
      await waitFor(async () => (await countOf(page.locator(".react-flow__edge"))) === 7, { what: "las 7 llamadas" });
    });

    await check("R2/1440 · la paleta trae sus 5 grupos y 16 componentes arrastrables", async () => {
      must(await visible(page.getByText("Componentes", { exact: true })), "sin título de paleta");
      for (const g of ["Exterior", "Borde", "Cómputo", "Estado", "Flujo y operación"]) {
        must(await visible(page.getByText(g, { exact: true })), `sin grupo ${g}`);
      }
      must((await countOf(page.locator('aside [draggable="true"]'))) === 16, "no hay 16 arrastrables");
    });

    await check("R2/1440 · el estado de guardado arranca en «Sin cambios» y lleva la versión", async () => {
      await waitFor(async () => /Sin cambios/.test(await status.first().textContent()), { what: "«Sin cambios»" });
      must(/·\s*v\d+/.test(await status.first().textContent()), "sin «· vN»");
    });

    await check("R2/1440 · la paleta se pliega, se recuerda en bondstone.editor.ui y se despliega", async () => {
      await page.getByRole("button", { name: "Plegar la paleta" }).click();
      await waitFor(async () => await visible(page.getByRole("button", { name: "Desplegar la paleta" })), { what: "paleta plegada" });
      const pref = await page.evaluate(() => JSON.parse(localStorage.getItem("bondstone.editor.ui") || "{}").paletteCollapsed);
      must(pref === true, "paletteCollapsed no se guardó");
      await page.getByRole("button", { name: "Desplegar la paleta" }).click();
      await waitFor(async () => await visible(page.getByText("Componentes", { exact: true })), { what: "paleta desplegada" });
    });

    const nodo = (texto) => page.locator(".react-flow__node").filter({ hasText: texto });

    await check("R2/1440 · el inspector de componente se abre al seleccionar un nodo", async () => {
      await nodo("Pedidos").first().click();
      await waitFor(async () => await visible(page.getByText("Componente", { exact: true })), { what: "inspector" });
      must((await page.getByLabel("Nombre", { exact: true }).inputValue()) === "Pedidos", "el nombre no es Pedidos");
    });

    await check("R2/1440 · renombrar el componente se ve en el lienzo y dispara el autoguardado", async () => {
      const saved = page.waitForResponse((res) => res.request().method() === "PUT" && res.url().includes("/api/architectures/edit/"), { timeout: 20000 });
      await page.getByLabel("Nombre", { exact: true }).fill("Compras");
      await waitFor(async () => (await countOf(nodo("Compras"))) === 1, { what: "el nodo renombrado" });
      await saved;
      await waitFor(async () => /Guardado/.test(await status.first().textContent()), { what: "«Guardado»" });
    });

    await check("R2/1440 · un campo de configuración se edita y se guarda", async () => {
      await page.getByLabel("Instancias", { exact: true }).fill("3");
      await waitFor(async () => /Guardado/.test(await status.first().textContent()), { timeout: 20000, what: "«Guardado» tras Instancias" });
    });

    await check("R2/1440 · el inspector se cierra y vuelve la paleta", async () => {
      await page.getByRole("button", { name: "Cerrar" }).first().click();
      await waitFor(async () => await visible(page.getByText("Componentes", { exact: true })), { what: "la paleta de vuelta" });
    });

    await check("R2/1440 · el inspector de llamada muestra los dos extremos y el protocolo derivado", async () => {
      await page.locator(".react-flow__edge-interaction").first().click({ force: true });
      await waitFor(async () => await visible(page.getByText("Llamada", { exact: true })), { what: "inspector de llamada" });
      must(await visible(page.getByText("→", { exact: false }).first()), "sin la pareja origen → destino");
      must(await visible(page.getByLabel("Timeout (s)")), "sin campo de timeout");
      await page.keyboard.press("Escape");
    });

    await check("R2/1440 · crear un componente arrastrándolo de la paleta al lienzo", async () => {
      const antes = await countOf(page.locator(".react-flow__node"));
      await page.evaluate(() => {
        const pane = document.querySelector(".react-flow__pane");
        const rect = pane.getBoundingClientRect();
        const x = Math.round(rect.x + rect.width * 0.35);
        const y = Math.round(rect.y + rect.height * 0.8);
        const dt = new DataTransfer();
        dt.setData("application/bondstone-node-type", "Observability");
        const opts = { bubbles: true, cancelable: true, dataTransfer: dt, clientX: x, clientY: y };
        pane.dispatchEvent(new DragEvent("dragover", opts));
        pane.dispatchEvent(new DragEvent("drop", opts));
      });
      await waitFor(async () => (await countOf(page.locator(".react-flow__node"))) === antes + 1, { what: "el nodo nuevo" });
      must((await countOf(nodo("Observability 1"))) === 1, "no se llama Observability 1");
    });

    await check("R2/1440 · conectar dos componentes tirando del puerto", async () => {
      await page.getByRole("button", { name: "Encuadrar el dibujo" }).click();
      await new Promise((r) => setTimeout(r, 600));
      const antes = await countOf(page.locator(".react-flow__edge"));
      const src = nodo("Huerfano").first();
      const dst = nodo("Observability 1").first();
      await src.scrollIntoViewIfNeeded();
      await src.hover();
      const from = await src.locator(".react-flow__handle-bottom").boundingBox();
      const to = await dst.locator(".react-flow__handle-top").boundingBox();
      must(from && to, "no se ven los puertos");
      await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
      await page.mouse.down();
      await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 14 });
      await page.mouse.up();
      await waitFor(async () => (await countOf(page.locator(".react-flow__edge"))) === antes + 1, { what: "la llamada nueva" });
    });

    await check("R2/1440 · deshacer y rehacer con el teclado", async () => {
      const conLlamada = await countOf(page.locator(".react-flow__edge"));
      await page.locator(".react-flow__pane").click({ position: { x: 30, y: 30 } });
      await page.keyboard.press("Control+z");
      await waitFor(async () => (await countOf(page.locator(".react-flow__edge"))) === conLlamada - 1, { what: "deshacer" });
      await page.keyboard.press("Control+Shift+z");
      await waitFor(async () => (await countOf(page.locator(".react-flow__edge"))) === conLlamada, { what: "rehacer" });
    });

    await check("R2/1440 · borrar un componente desde el inspector y recuperarlo con deshacer", async () => {
      const antes = await countOf(page.locator(".react-flow__node"));
      await nodo("Observability 1").first().click();
      await page.getByRole("button", { name: "Borrar el componente" }).click();
      await waitFor(async () => (await countOf(page.locator(".react-flow__node"))) === antes - 1, { what: "el borrado" });
      await page.locator(".react-flow__pane").click({ position: { x: 30, y: 30 } });
      await page.keyboard.press("Control+z");
      await waitFor(async () => (await countOf(page.locator(".react-flow__node"))) === antes, { what: "recuperar el nodo" });
    });

    await check("R2/1440 · lo dibujado se ha guardado: sobrevive a la recarga", async () => {
      await waitFor(async () => /Guardado/.test(await status.first().textContent()), { timeout: 25000, what: "«Guardado» antes de recargar" });
      const nodos = await countOf(page.locator(".react-flow__node"));
      const llamadas = await countOf(page.locator(".react-flow__edge"));
      await page.reload();
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      await waitFor(async () => (await countOf(page.locator(".react-flow__node"))) === nodos, { what: "los nodos tras recargar" });
      must((await countOf(page.locator(".react-flow__edge"))) === llamadas, "las llamadas no sobrevivieron");
      must((await countOf(nodo("Compras"))) === 1, "el renombrado no sobrevivió");
    });

    await check("R2/1440 · sin desbordamiento horizontal en el editor de edición", async () => {
      await noOverflow(page, "/e/ a 1440");
    });

    // ── Rebanada 3 · análisis ─────────────────────────────────────────────
    await check("R3/1440 · el motor se lee dentro del editor: panel con la nota y su banda", async () => {
      await waitFor(async () => await visible(page.getByRole("heading", { name: "Análisis" })), { timeout: 25000, what: "el panel" });
      await waitFor(async () => /\/ 100 · \d+ áreas?/.test(await panel.first().textContent()), { timeout: 25000, what: "la nota" });
      const txt = await panel.first().textContent();
      must(/(sólida|aceptable|frágil|grave|sin evaluar)/.test(txt), "sin banda de nota");
    });

    await check("R3/1440 · el resumen dice cuántos problemas quedan", async () => {
      await waitFor(async () => /Quedan \d+ problemas?(, \d+ críticos?)?\./.test(await panel.first().textContent()), { what: "«Quedan N problemas»" });
    });

    await check("R3/1440 · «Arregla esto primero» con el hallazgo que describe el dibujo", async () => {
      const banda = panel.locator("div").filter({ hasText: "Arregla esto primero" }).last();
      must(await visible(banda), "no sale la banda");
      must((await countOf(panel.getByRole("link", { name: "IMPLAUSIBLE_DEPENDENCY" }))) >= 1, "sin IMPLAUSIBLE_DEPENDENCY");
    });

    await check("R3/1440 · las áreas traen su pregunta y su recuento de hallazgos", async () => {
      const txt = await panel.first().textContent();
      must(/¿Hay algo de lo que dependa todo lo demás\?/.test(txt), "sin la pregunta de Disponibilidad");
      must(/\d+ hallazgos?/.test(txt), "sin recuento de hallazgos");
    });

    await check("R3/1440 · puerta «Cómo se calcula»: la aritmética y los chips de penalización", async () => {
      const b = panel.getByRole("button", { name: "Cómo se calcula" });
      await b.click();
      await waitFor(async () => (await b.getAttribute("aria-expanded")) === "true", { what: "la puerta abierta" });
      const txt = await panel.first().textContent();
      must(/Cada área parte de 100/.test(txt), "sin la leyenda del cálculo");
      must(/−\s?\d/.test(txt), "sin chips de penalización");
    });

    await check("R3/1440 · puerta «Historial», que solo existe en el editor de edición", async () => {
      const b = panel.getByRole("button", { name: "Historial" });
      must((await countOf(b)) >= 1, "no hay puerta de historial");
      await b.first().click();
      await waitFor(async () => (await b.first().getAttribute("aria-expanded")) === "true", { what: "historial abierto" });
    });

    await check("R3/1440 · puerta «Coste» con el total en unidades", async () => {
      const b = panel.getByRole("button", { name: "Coste", exact: true });
      await b.first().click();
      await waitFor(async () => /\d+\s?u(?![a-záéíóúü])/.test(await panel.first().textContent()), { what: "el total de coste" });
    });

    await check("R3/1440 · la cobertura, sus cuentas y el enlace a las 25 reglas", async () => {
      const txt = await panel.first().textContent();
      must(/Cobertura/.test(txt), "sin cobertura");
      must(/\d+ saltaron/.test(txt) && /\d+ superadas/.test(txt), "sin las cuentas de cobertura");
      const reglas = panel.getByRole("link", { name: /Las 25 reglas/ });
      must((await countOf(reglas)) === 1, "sin enlace a las 25 reglas");
      must((await reglas.getAttribute("href")) === `/rules/${SHARE}`, "el enlace de reglas no lleva el token");
      must((await reglas.getAttribute("target")) === "bondstone-docs", "el enlace no abre la ventana de documentación");
    });

    await check("R3/1440 · pasar el cursor por un hallazgo lo resalta en el lienzo, y pulsarlo lo fija", async () => {
      const base = await countOf(canvas.locator(".af-node-highlighted"));
      const fila = panel.locator("li").filter({ hasText: "punto único de fallo" }).first();
      must((await countOf(fila)) === 1, "no encuentro un hallazgo de SPOF");
      await fila.hover();
      await waitFor(async () => (await countOf(canvas.locator(".af-node-highlighted"))) > base, { what: "el resaltado al pasar" });
      await page.mouse.move(700, 12);
      await waitFor(async () => (await countOf(canvas.locator(".af-node-highlighted"))) === base, { what: "que se apague" });
      await fila.click();
      await page.mouse.move(700, 12);
      await waitFor(async () => (await countOf(canvas.locator(".af-node-highlighted"))) > base, { what: "el fijado" });
      await fila.click();
      await page.mouse.move(700, 12);
      await waitFor(async () => (await countOf(canvas.locator(".af-node-highlighted"))) === base, { what: "soltar el fijado" });
    });

    await check("R3/1440 · el arreglo de un clic aplica, abre la comparación en dos diagramas y se deshace", async () => {
      const arreglar = panel.getByRole("button", { name: /^Poner una caché delante de/ });
      must((await countOf(arreglar)) >= 1, "no hay arreglo de caché");
      const nodosAntes = await countOf(page.locator(".react-flow__node"));
      await arreglar.first().click();
      const dlg = page.getByRole("dialog");
      await waitFor(async () => await visible(dlg.getByRole("heading", { name: "El arreglo, en dos diagramas" })), { timeout: 25000, what: "la comparación" });
      const txt = await dlg.first().textContent();
      must(/Resaltado: lo que el arreglo tocó\./.test(txt), "sin la leyenda del resaltado");
      must(/no una previsualización/.test(txt), "sin la frase de que ya está aplicado");
      must((await countOf(dlg.locator("svg"))) >= 2, "no hay dos diagramas");
      await waitFor(async () => (await countOf(page.locator(".react-flow__node"))) === nodosAntes + 1, { what: "la caché en el lienzo" });
      await dlg.getByRole("button", { name: "Deshacer" }).click();
      await waitFor(async () => (await countOf(page.getByRole("dialog"))) === 0, { what: "que se cierre" });
      await waitFor(async () => (await countOf(page.locator(".react-flow__node"))) === nodosAntes, { timeout: 25000, what: "deshacer el arreglo" });
    });

    await check("R3/1440 · la tira: la lente abre su menú con las siete preguntas más «Todas»", async () => {
      await page.getByRole("button", { name: /^Lente: / }).click();
      const menu = page.getByRole("menu", { name: "Lente" });
      await waitFor(async () => await visible(menu), { what: "el menú de la lente" });
      must((await countOf(menu.getByRole("menuitemradio"))) === 8, "no hay 8 opciones de lente");
      await menu.getByRole("menuitemradio").nth(1).click();
      await waitFor(async () => (await countOf(canvas.locator(".af-dim"))) > 0, { what: "el atenuado del resto" });
      await page.getByRole("button", { name: /^Lente: / }).click();
      await page.getByRole("menu", { name: "Lente" }).getByRole("menuitemradio").first().click();
    });

    await check("R3/1440 · la tira: «Marcas» conmuta con aria-pressed", async () => {
      const marcas = page.getByRole("button", { name: "Marcas" });
      const antes = await marcas.getAttribute("aria-pressed");
      await marcas.click();
      await waitFor(async () => (await marcas.getAttribute("aria-pressed")) !== antes, { what: "el conmutador de marcas" });
    });

    await check("R3/1440 · la tira: la leyenda abre su cajón de 300 px y deja de estar inerte", async () => {
      const cajon = page.locator("aside.af-legend");
      must((await cajon.getAttribute("aria-hidden")) === "true", "la leyenda no arranca oculta");
      await page.getByRole("button", { name: "Leyenda", exact: true }).click();
      await waitFor(async () => (await cajon.getAttribute("aria-hidden")) !== "true", { what: "la leyenda abierta" });
      const box = await cajon.boundingBox();
      must(box && Math.round(box.width) === 300, `el cajón mide ${box && Math.round(box.width)} px, no 300`);
      await page.getByRole("button", { name: "Leyenda", exact: true }).click();
    });

    await check("R3/1440 · la tira: el dial de tráfico pide su curva y pinta el escenario hipotético", async () => {
      await page.getByRole("button", { name: /tráfico se multiplica/ }).click();
      const dial = page.getByRole("dialog", { name: "Tráfico" });
      await waitFor(async () => await visible(dial), { what: "el cuadro del dial" });
      const slider = dial.getByRole("slider");
      await slider.first().fill("6");
      await waitFor(async () => /^×/.test((await slider.first().getAttribute("aria-valuetext")) ?? ""), { what: "el multiplicador" });
      must(/escenario hipotético/.test(await dial.first().textContent()), "sin el aviso de que no cuenta");
      await page.keyboard.press("Escape");
    });

    await check("R3/1440 · el medidor de carga en el nodo, con su lectura de rps", async () => {
      const titulos = await page.locator(".react-flow__node [title]").evaluateAll((els) => els.map((e) => e.getAttribute("title")));
      must(titulos.some((t) => /\d+\s*\/\s*\d+ rps/.test(t ?? "")), `ningún nodo lleva medidor: ${JSON.stringify(titulos.slice(0, 6))}`);
    });

    await check("R3/1440 · el zoom vive en la tira, no en los controles de la librería", async () => {
      must(await visible(page.getByRole("button", { name: "Alejar" })), "sin botón de alejar");
      must(await visible(page.getByRole("button", { name: "Acercar" })), "sin botón de acercar");
      must(await visible(page.getByRole("button", { name: "Encuadrar el dibujo" })), "sin encuadrar");
    });

    await check("R3/1440 · sin desbordamiento horizontal con el panel y la tira montados", async () => {
      await noOverflow(page, "/e/ con panel a 1440");
    });

    // ── /a/:token · lectura ───────────────────────────────────────────────
    await check("R3/1440 · en /a/:token el mismo panel se lee sin historial y sin arreglos", async () => {
      await page.goto(`${BASE}/a/${SHARE}`);
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      const ro = page.locator("aside").filter({ has: page.getByRole("heading", { name: "Análisis" }) });
      await waitFor(async () => /\/ 100 · \d+ áreas?/.test(await ro.first().textContent()), { timeout: 25000, what: "la nota en lectura" });
      must(await visible(ro.getByRole("button", { name: "Cómo se calcula" })), "sin la puerta del cálculo");
      must((await countOf(ro.getByRole("button", { name: "Historial" }))) === 0, "en lectura no debería haber historial");
      must((await countOf(ro.getByRole("button", { name: /^Poner una caché delante de/ }))) === 0, "en lectura no debería haber arreglos");
      await noOverflow(page, "/a/ a 1440");
    });

    // ── conflicto 409 (al final: bloquea el autoguardado) ─────────────────
    await check("R2/1440 · el conflicto 409 saca su diálogo y deja el estado en «Conflicto»", async () => {
      expectedBadRequests.add(`PUT /api/architectures/edit/${EDIT} 409`);
      await page.goto(`${BASE}/e/${EDIT}`);
      await page.waitForSelector(".react-flow__node", { timeout: 20000 });
      await waitFor(async () => /Sin cambios/.test(await status.first().textContent()), { what: "«Sin cambios»" });

      const actual = await fetch(`${API}/architectures/${SHARE}`).then((x) => x.json());
      const r2 = await put({ ...seed("QA pasada · otra sesión", actual.version), version: actual.version });
      must(r2.ok, `el PUT de la otra sesión falló: ${r2.status}`);

      await page.getByLabel("Nombre de la arquitectura").fill("choque");
      const dlg = page.getByRole("dialog");
      await waitFor(async () => await visible(dlg), { timeout: 25000, what: "el diálogo de conflicto" });
      must(/ha cambiado en otro sitio/.test(await dlg.first().textContent()), "el diálogo no es el del conflicto");
      await waitFor(async () => /Conflicto/.test(await status.first().textContent()), { what: "el estado en Conflicto" });
      await dlg.getByRole("button", { name: "Recargar" }).click();
      await waitFor(async () => (await countOf(page.getByRole("dialog"))) === 0, { timeout: 25000, what: "que «Recargar» cierre el diálogo" });
    });

    await ctx.close();

    // ── 390 px ────────────────────────────────────────────────────────────
    const ctx2 = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: "es-ES", isMobile: true, hasTouch: true });
    const p2 = await ctx2.newPage();
    watch(p2, bag);
    await p2.goto(`${BASE}/e/${EDIT}`);
    await p2.waitForSelector(".react-flow__node", { timeout: 20000 });

    await check("R2/390 · el editor se abre en lectura y lo dice, sin paleta ni columna derecha", async () => {
      must(await visible(p2.getByText(/Se abre en modo lectura/)), "sin la banda de solo lectura");
      must((await countOf(p2.getByText("Componentes", { exact: true }))) === 0, "a 390 no debería haber paleta");
      must((await countOf(p2.locator('aside[class*="336"]'))) === 0, "a 390 no debería haber columna derecha");
    });

    await check("R3/390 · el análisis vive en el cajón inferior, plegado de salida, y se despliega", async () => {
      const cajon = p2.locator("#editor-drawer");
      must((await cajon.getAttribute("hidden")) !== null, "el cajón no arranca plegado");
      await waitFor(async () => /Análisis · \d+/.test(await p2.locator("body").textContent()), { timeout: 25000, what: "el rótulo del cajón con la nota" });
      await p2.getByRole("button", { name: "Desplegar el cajón" }).click();
      await waitFor(async () => (await cajon.getAttribute("hidden")) === null, { what: "el cajón desplegado" });
      must(/\/ 100 · \d+ áreas?/.test(await cajon.textContent()), "el cajón no trae la nota");
      await p2.getByRole("button", { name: "Plegar el cajón" }).click();
    });

    await check("R3/390 · la tira de control sigue ahí y sus chips miden 44 px de alto", async () => {
      const lente = p2.getByRole("button", { name: "Todas", exact: true });
      must((await countOf(lente)) === 1, "sin el chip de lente en estrecho");
      const box = await lente.boundingBox();
      must(box && Math.round(box.height) >= 44, `el chip mide ${box && Math.round(box.height)} px de alto, no 44`);
    });

    await check("R2/390 · el menú estrecho se abre desde la barra", async () => {
      await p2.getByRole("button", { name: "Abrir el menú" }).click();
      await waitFor(async () => (await countOf(p2.getByRole("dialog"))) > 0, { what: "el menú estrecho" });
      await p2.keyboard.press("Escape");
      await waitFor(async () => (await countOf(p2.getByRole("dialog"))) === 0, { what: "que Escape lo cierre" });
    });

    await check("R3/390 · sin desbordamiento horizontal en el editor estrecho", async () => {
      await noOverflow(p2, "/e/ a 390");
    });

    await check("R3/390 · sin desbordamiento horizontal en /a/ estrecho", async () => {
      await p2.goto(`${BASE}/a/${SHARE}`);
      await p2.waitForSelector(".react-flow__node", { timeout: 20000 });
      await noOverflow(p2, "/a/ a 390");
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
