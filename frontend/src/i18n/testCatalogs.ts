// Las pruebas piden traducciones de los dos idiomas sin poder esperar a nada: quince ficheros
// llaman a `createTranslator("en")` desde el cuerpo del módulo. En la aplicación el inglés llega
// por `import()` para que la portada no lo descargue; aquí se precarga una vez, antes de que
// vitest ejecute ningún fichero, y todas siguen siendo síncronas.
//
// Se engancha en `vite.config.ts` (`test.setupFiles`).
import { loadCatalog } from "./index";

await loadCatalog("en");
