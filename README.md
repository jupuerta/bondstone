# arch-forge-frontend-next

El segundo frontend de ArchForge: rediseño desde cero de la interfaz, contra **la misma API sin
tocar**. El frontend actual (`../arch-forge-frontend/`) sigue vivo, es otro repo y no se toca; los
dos corren a la vez contra el mismo backend.

## Estado: la cara pública, en la dirección B1

Lo que hay es la **web pública** tal como la fijaron los cuatro entregables de diseño
(`docs/Design/`): portada, retos, un reto, el enlace compartido, las tres páginas de referencia,
«cómo está hecho», las legales y el 404. Escritorio y móvil, español e inglés.

Lo que **no** hay todavía es el editor: `/e/{token}` abre el del frontend actual
(`VITE_LEGACY_APP_URL`). Cuando el editor se reescriba aquí, esa página desaparece.

| Ruta | Qué es | De dónde salen los datos |
| --- | --- | --- |
| `/` | Portada. La demostración es el motor de verdad sobre «la tienda», con Postgres caído, antes y después | `GET /architectures/{token}`, `POST …/analyze`, `POST …/simulate` |
| `/challenges` | Los 23 retos en tres bandas, con filtros, progreso y «Tu trabajo» | `GET /challenges` + `localStorage` |
| `/c/:slug` | Un reto: historia, lección, requisitos y «Empezar el reto». **No crea nada al cargar**; solo el botón | `GET /challenges/{slug}`, `POST …/start` |
| `/a/:token` | La arquitectura de otra persona, en solo lectura, con la franja pública encima y el fallo de la URL aplicado | Los tres de arriba + `POST …/fork` |
| `/rules`, `/rules/:token` | Las 25 reglas; con token, qué hizo cada una sobre esa arquitectura | Catálogo local + `POST …/analyze` |
| `/model` | El modelo: capacidades, red, umbrales, encolamiento | `GET /model` |
| `/mechanisms` | Los once mecanismos, sin el «Pruébalo» (que necesita el editor) | Catálogo local |
| `/about`, `/privacidad`, `/terminos`, `/aviso-de-recoleccion`, `*` | Estáticas. Las legales llevan el texto marcado como pendiente | — |

## Stack

React 19 · Vite 6 · TypeScript · Tailwind 4 · react-router 7 · vitest. El mismo que el frontend
actual, a propósito: comparten el catálogo de textos (`src/i18n/{es,en}.json`, las 1 691 claves del
producto más las `web.*` de la web), los clientes HTTP (`src/api/`), el contrato (`src/types/`) y
la forma de guardar el trabajo en el navegador (`src/domain/savedWork.ts`, misma clave).

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
tipos. El `Dockerfile` y `nginx.conf` son los de siempre: el servicio `frontend-next` del
`docker-compose.yml` de la raíz lo sirve en `:8082` bajo el perfil `next`.

### La demostración de la portada

Son dos arquitecturas guardadas en la base de datos de la API, «La tienda» y «La tienda ·
arreglada», identificadas por su enlace de solo lectura en `VITE_DEMO_BEFORE_TOKEN` y
`VITE_DEMO_AFTER_TOKEN`. `scripts/create-demo.mjs` las crea contra la API que le indiques y comprueba
que el simulador dice lo que la portada promete (la rota pierde la entrada; la arreglada la
conserva). Los tokens son de esa base de datos: en otro entorno hay que volver a crearlas.

## Lo que queda

- El editor, y con él el modo Simulate, las zonas, el coste, exportar: todo lo que hoy vive en el
  frontend actual.
- El «Pruébalo» de los mecanismos y los dibujos de patrón de sus fichas.
- «Qué llevas aprendido» en el catálogo (necesita el historial de puntuaciones que escribe el editor).
- Los textos legales.
- Partir el bundle: hoy el catálogo entero viaja en el primer JS (~750 KB sin comprimir).
