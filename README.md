# arch-forge-frontend-next

El frontend de ArchForge: la web y el editor, contra **la misma API sin tocar**. Nació como un
rediseño desde cero que convivía con el frontend anterior (`../arch-forge-frontend/`); ese se apagó
el **7 de septiembre de 2026**, al terminar la rebanada 6, y este quedó como el único.

## Estado: la web y el editor, en la dirección B1

Lo que hay es la **web pública** tal como la fijaron los cuatro entregables de diseño
(`docs/Design/`): portada, retos, un reto, el enlace compartido, las tres páginas de referencia,
«cómo está hecho», las legales y el 404. Escritorio y móvil, español e inglés.

Y está el **editor entero**, construido en las seis rebanadas de
`docs/Design/Editor/00_plan_y_prompts.md` §5: `/a/{token}` en lectura y `/e/{token}` en edición, con
el lienzo de `@xyflow/react`, la paleta y los dos inspectores, el autoguardado con deshacer y
conflicto, el panel de análisis con sus arreglos de un clic, los retos con su banda y su diario,
Simulate con la propagación y la fragilidad, y exportar —Markdown y postal con las tipografías
incrustadas—, los dibujos de patrón, la ficha de componente, el glosario y las glosas de campo.

Desde el **7 de septiembre de 2026** no queda nada del editor antiguo: ni proxy (`/e/*`, `/manual*`
y sus assets se retiraron de `nginx.conf` y de `vite.config.ts`), ni servicio — el
`docker-compose.yml` de la raíz tiene un solo frontend, que es este, en `FRONTEND_PORT` (8080).

| Ruta | Qué es | De dónde salen los datos |
| --- | --- | --- |
| `/` | Portada. La demostración es el motor de verdad sobre «la tienda», con Postgres caído, antes y después | `GET /architectures/{token}`, `POST …/analyze`, `POST …/simulate` |
| `/challenges` | Los 23 retos en tres bandas, con filtros, progreso y «Tu trabajo» | `GET /challenges` + `localStorage` |
| `/c/:slug` | Un reto: historia, lección, requisitos y «Empezar el reto». **No crea nada al cargar**; solo el botón | `GET /challenges/{slug}`, `POST …/start` |
| `/a/:token` | El editor en modo lectura: la franja pública encima y, debajo, la barra propia, el lienzo interactivo (pan, zoom, seleccionar), las bandas de zona, el fallo de la URL pintado sobre los nodos, la ficha de lo que tocas y la voz del motor. En pantalla estrecha, cajón inferior y menú | Los tres de arriba + `POST …/fork` (solo al pulsar «Abrir una copia»); `GET /challenges/{slug}` si la URL trae `?challenge=` |
| `/rules`, `/rules/:token` | Las 25 reglas; con token, qué hizo cada una sobre esa arquitectura | Catálogo local + `POST …/analyze` |
| `/model` | El modelo: capacidades, red, umbrales, encolamiento | `GET /model` |
| `/mechanisms` | Los once mecanismos, con «Pruébalo» | Catálogo local + `POST /architectures` al pulsar |
| `/about`, `/privacidad`, `/terminos`, `/aviso-de-recoleccion`, `*` | Estáticas. Las legales llevan el texto marcado como pendiente | — |

## Stack

React 19 · Vite 6 · TypeScript · Tailwind 4 · react-router 7 · vitest · `@xyflow/react` 12.11.2
(fijada, solo para el lienzo interactivo). El mismo stack que el frontend actual, a propósito:
comparten el catálogo de textos (`src/i18n/{es,en}.json`, las claves del producto más las `web.*`
de la web y las `editor.*` del editor nuevo), los clientes HTTP (`src/api/`), el contrato
(`src/types/`) y la forma de guardar el trabajo en el navegador (`src/domain/savedWork.ts`, misma
clave).

Las tipografías (Bricolage Grotesque, Instrument Sans, IBM Plex Mono) se sirven desde aquí, con
`@fontsource`. Nada llama a Google.

## Cómo se organiza

```
src/
├── index.css            los tokens: colores, tipografías, escala, radios (docs/Design/UI, paso 6)
├── i18n/                catálogo compartido + web.*; el proveedor pone español por defecto
├── api/  types/  domain/    copiados del frontend actual, sin cambios; más rulesCatalog, mechanisms, challengeBands, progress
├── components/ui/       Button, Chip, FilterChip, Segmented, Callout, Skeleton, InlineError, Icons
├── components/layout/   SiteHeader (con el menú en pantalla estrecha), SiteFooter, PageShell, PageHead, ReferenceBack
├── components/engine/   EnginePanel y sus piezas: useEngine, Diagram (SVG), NodeList, FindingCard
├── components/editor/   el editor (docs/Design/Editor/06_ui.md §6): canvas/ (xyflow, nodo, arista, banda de zona,
│                        zoneBands, callFlow, mapping), shell/ (barra, menú estrecho, cajón, referencia),
│                        inspector/ (los dos inspectores), palette/ (los 16 iconos), panel/ (análisis),
│                        simulate/, challenge/, patterns/ (los once dibujos), help/ (ficha, glosario, glosas),
│                        overlays/ (conflicto, arreglo, referencia, exportar)
├── export/              la capa de export portada con sus goldens: Markdown, mermaid, postal, fuentes
└── pages/               una por ruta
```

Reglas que el código respeta y que vienen del diseño:

- **Un solo acento** (cobalto) fuera del motor; el panel oscuro solo donde está el motor.
- **Ningún estado depende solo del color**: los nodos llevan punto y texto; «superado» y «empezado»
  van en chip con texto.
- **Foco visible**: cerco cobalto de 2 px a 3 px del borde; blanco dentro del panel.
- **Nada se crea al cargar una página.** Solo al pulsar.
- **Textos**: nunca literales en el código; siempre una clave del catálogo, en los dos idiomas.
  `npm run check:i18n` falla si un idioma se queda atrás.

## Levantarlo

```
cp .env.example .env
npm install
docker compose up -d postgres api     # desde la raíz, una vez
node scripts/create-demo.mjs          # crea «la tienda» y devuelve los tokens para el .env
npm run dev                           # → http://localhost:5174
```

`npm run build` deja el sitio en `dist/`; `npm test` corre los tests; `npm run typecheck`, los
tipos; `npm run regenerate:goldens` reescribe los seis documentos de referencia de `src/export/__fixtures__`
cuando cambia una frase `finding.*` (y `check:i18n` comprueba que las grabaciones de esa carpeta ejercitan
las 260 frases del analizador). La CI (`.github/workflows/ci.yml`) ejecuta los tres sobre `master` y en cada pull request. El `Dockerfile` y `nginx.conf` son los de siempre: el servicio `frontend` del
`docker-compose.yml` de la raíz lo sirve en `FRONTEND_PORT` (`:8080`), sin perfil, desde el 7 de septiembre de 2026.

### La demostración de la portada

Son dos arquitecturas guardadas en la base de datos de la API, «La tienda» y «La tienda ·
arreglada», identificadas por su enlace de solo lectura en `VITE_DEMO_BEFORE_TOKEN` y
`VITE_DEMO_AFTER_TOKEN`. `scripts/create-demo.mjs` las crea contra la API que le indiques y comprueba
que el simulador dice lo que la portada promete (la rota pierde la entrada; la arreglada la
conserva). Los tokens son de esa base de datos: en otro entorno hay que volver a crearlas.

## Lo que queda

- Los dibujos de patrón en las fichas de mecanismo de la web (la librería ya vive en `components/editor/patterns/`).
- «Qué llevas aprendido» en el catálogo (necesita el historial de puntuaciones que escribe el editor).
- Los textos legales.
- Partir el bundle: hoy el primer JS pesa ~1,03 MB sin comprimir (293 KB gzip), con el catálogo
  entero dentro; xyflow (~197 KB, 64 KB gzip) y opentype.js (~243 KB, 68 KB gzip, solo para la
  postal) ya van en su propio chunk.
